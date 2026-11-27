using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services.Recording
{
    internal interface ICaptureEngine : IDisposable
    {
        event Action<byte[], int, int>? FrameArrived;
        bool IsRunning { get; }
        bool IsUsingWgc { get; }
        int DroppedFrames { get; }
        void Start();
        void Pause();
        void Resume();
        void Stop();
    }

    /// <summary>
    /// Windows.Graphics.Capture monitor capture with CPU readback via WinRtBridge,
    /// eliminating the 24.2 MB Microsoft.Windows.SDK.NET projection.
    /// </summary>
    internal sealed class WgcCaptureEngine : ICaptureEngine
    {
        private readonly IntPtr _monitor;
        private readonly NativeMethods.RECT _monitorBounds;
        private readonly Int32Rect _region;
        private const int FramePoolBufferCount = 4;
        private const int FrameQueueCapacity = 4;
        private readonly SemaphoreSlim _copyGate = new(1, 1);
        private BlockingCollection<CaptureFrame>? _frames;
        private Thread? _worker;
        private IntPtr _framePool;
        private IntPtr _session;
        private IntPtr _device;
        private WinRtBridge.AgileTypedEventHandler? _frameHandler;
        private long _frameToken;
        private D3D11Readback? _readback;
        private bool _running;
        private volatile bool _paused;
        private int _dropped;

        internal long LastWaitTicks { get; private set; }
        internal long LastReadTicks { get; private set; }
        internal long LastTotalTicks { get; private set; }

        public event Action<byte[], int, int>? FrameArrived;
        public bool IsRunning => _running;
        public bool IsUsingWgc => true;
        public int DroppedFrames => Volatile.Read(ref _dropped);

        public WgcCaptureEngine(IntPtr monitor, NativeMethods.RECT monitorBounds, Int32Rect region)
        {
            _monitor = monitor;
            _monitorBounds = monitorBounds;
            _region = region;
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _frames = new BlockingCollection<CaptureFrame>(FrameQueueCapacity);
            _worker = new Thread(ProcessFrames)
            {
                IsBackground = true,
                Name = "CryoUtils WGC consumer"
            };
            _worker.Start();

            IntPtr item = WinRtBridge.CreateCaptureItemForMonitor(_monitor, out var size);
            try
            {
                if (size.Width <= 0 || size.Height <= 0)
                {
                    size = new WinRtBridge.SizeInt32
                    {
                        Width = Math.Max(1, _monitorBounds.Width),
                        Height = Math.Max(1, _monitorBounds.Height)
                    };
                }

                var device = CreateDevice(size.Width, size.Height);
                _device = device.DevicePtr;
                _readback = device.Readback;
                _framePool = WinRtBridge.CreateFreeThreadedFramePool(_device, FramePoolBufferCount, size);
                _session = WinRtBridge.CreateCaptureSession(_framePool, item);
                _frameHandler = new WinRtBridge.AgileTypedEventHandler(OnFrameArrived);
                _frameToken = WinRtBridge.AddFrameArrivedHandler(_framePool, _frameHandler.ComPointer);
                _running = true;
                WinRtBridge.StartCaptureSession(_session);
            }
            finally
            {
                WinRtBridge.SafeRelease(ref item);
            }
        }

        private async void OnFrameArrived()
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            IntPtr pool = _framePool;
            if (!_running || pool == IntPtr.Zero)
            {
                return;
            }

            IntPtr frame = WinRtBridge.TryGetNextCaptureFrame(pool);
            if (frame == IntPtr.Zero)
            {
                return;
            }

            if (_paused)
            {
                WinRtBridge.CloseAndRelease(ref frame);
                return;
            }

            long waitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!await _copyGate.WaitAsync(50).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _dropped);
                WinRtBridge.CloseAndRelease(ref frame);
                return;
            }

            LastWaitTicks = System.Diagnostics.Stopwatch.GetTimestamp() - waitStarted;
            IntPtr surface = IntPtr.Zero;
            try
            {
                surface = WinRtBridge.GetCaptureFrameSurface(frame);
                if (surface == IntPtr.Zero)
                {
                    return;
                }

                if (_readback != null)
                {
                    long readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    bool read = _readback.TryRead(
                        surface,
                        out var gpuPixels,
                        out int gpuWidth,
                        out int gpuHeight);
                    LastReadTicks = System.Diagnostics.Stopwatch.GetTimestamp() - readStarted;
                    if (read)
                    {
                        EnqueuePixels(gpuPixels, gpuWidth, gpuHeight);
                        return;
                    }
                }

                var fallback = await WinRtBridge.ReadSurfaceFallbackAsync(surface).ConfigureAwait(false);
                if (fallback.HasValue)
                {
                    EnqueuePixels(fallback.Value.Pixels, fallback.Value.Width, fallback.Value.Height);
                }
            }
            catch
            {
                Interlocked.Increment(ref _dropped);
            }
            finally
            {
                WinRtBridge.SafeRelease(ref surface);
                WinRtBridge.CloseAndRelease(ref frame);
                LastTotalTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                _copyGate.Release();
            }
        }

        private void EnqueuePixels(byte[] pixels, int width, int height)
        {
            var queue = _frames;
            if (queue == null || !_running)
            {
                return;
            }

            var frame = new CaptureFrame(pixels, width, height);
            if (queue.TryAdd(frame))
            {
                return;
            }

            // Keep the newest frame when the encoder falls behind; the queue is
            // deliberately small so a slow consumer cannot add seconds of lag.
            if (queue.TryTake(out _))
            {
                Interlocked.Increment(ref _dropped);
            }
            if (!queue.TryAdd(frame))
            {
                Interlocked.Increment(ref _dropped);
            }
        }

        private void ProcessFrames()
        {
            var queue = _frames;
            if (queue == null)
            {
                return;
            }

            try
            {
                foreach (var frame in queue.GetConsumingEnumerable())
                {
                    if (!_running || _paused)
                    {
                        continue;
                    }

                    try
                    {
                        PublishPixels(frame.Pixels, frame.Width, frame.Height);
                    }
                    catch
                    {
                        Interlocked.Increment(ref _dropped);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void PublishPixels(byte[] pixels, int width, int height)
        {
            int x = Math.Clamp(_region.X - _monitorBounds.Left, 0, Math.Max(0, width - 1));
            int y = Math.Clamp(_region.Y - _monitorBounds.Top, 0, Math.Max(0, height - 1));
            int cropWidth = Math.Clamp(_region.Width, 1, Math.Max(1, width - x));
            int cropHeight = Math.Clamp(_region.Height, 1, Math.Max(1, height - y));
            if (x == 0 && y == 0 && cropWidth == width && cropHeight == height)
            {
                FrameArrived?.Invoke(pixels, width, height);
            }
            else
            {
                var cropped = Crop(pixels, width, x, y, cropWidth, cropHeight);
                FrameArrived?.Invoke(cropped, cropWidth, cropHeight);
            }
        }

        private static byte[] Crop(byte[] source, int sourceWidth, int x, int y, int width, int height)
        {
            var result = new byte[width * height * 4];
            int sourceStride = sourceWidth * 4;
            int destinationStride = width * 4;
            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(
                    source,
                    (y + row) * sourceStride + x * 4,
                    result,
                    row * destinationStride,
                    destinationStride);
            }

            return result;
        }

        private static (IntPtr DevicePtr, D3D11Readback? Readback) CreateDevice(
            int width,
            int height)
        {
            var levels = new[] { 0xB100, 0xB000, 0xA100, 0xA000 };
            int hr = D3D11CreateDevice(
                IntPtr.Zero,
                1,
                IntPtr.Zero,
                0x20,
                levels,
                (uint)levels.Length,
                7,
                out IntPtr d3dDevice,
                out _,
                out IntPtr context);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            IntPtr dxgiDevice = IntPtr.Zero;
            try
            {
                Guid iid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
                hr = Marshal.QueryInterface(d3dDevice, in iid, out dxgiDevice);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out IntPtr inspectable);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                var readback = D3D11Readback.TryCreate(d3dDevice, context, width, height);
                return (inspectable, readback);
            }
            finally
            {
                if (dxgiDevice != IntPtr.Zero)
                {
                    Marshal.Release(dxgiDevice);
                }
                if (context != IntPtr.Zero)
                {
                    Marshal.Release(context);
                }
                if (d3dDevice != IntPtr.Zero)
                {
                    Marshal.Release(d3dDevice);
                }
            }
        }

        public void Pause() => _paused = true;
        public void Resume() => _paused = false;

        public void Stop()
        {
            _running = false;
            _frames?.CompleteAdding();
            if (!_copyGate.Wait(1000))
            {
                _worker?.Join(3000);
                return;
            }

            try
            {
                WinRtBridge.RemoveFrameArrivedHandler(_framePool, _frameToken);
                _frameToken = 0;
                WinRtBridge.CloseAndRelease(ref _session);
                WinRtBridge.CloseAndRelease(ref _framePool);
                _frameHandler?.Dispose();
                _frameHandler = null;
                _readback?.Dispose();
                _readback = null;
                WinRtBridge.CloseAndRelease(ref _device);
                _worker?.Join(3000);
                _worker = null;
                _frames = null;
            }
            catch
            {
            }
            finally
            {
                _copyGate.Release();
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private sealed class CaptureFrame
        {
            public CaptureFrame(byte[] pixels, int width, int height)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
            }

            public byte[] Pixels { get; }
            public int Width { get; }
            public int Height { get; }
        }

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int D3D11CreateDevice(
            IntPtr adapter,
            int driverType,
            IntPtr software,
            uint flags,
            int[] featureLevels,
            uint featureLevelCount,
            uint sdkVersion,
            out IntPtr device,
            out int featureLevel,
            out IntPtr context);

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice);
    }

    internal sealed class GdiCaptureEngine : ICaptureEngine
    {
        private readonly Int32Rect _region;
        private readonly int _fps;
        private Thread? _thread;
        private volatile bool _running;
        private volatile bool _paused;

        public event Action<byte[], int, int>? FrameArrived;
        public bool IsRunning => _running;
        public bool IsUsingWgc => false;
        public int DroppedFrames => 0;

        public GdiCaptureEngine(Int32Rect region, int fps)
        {
            _region = region;
            _fps = Math.Clamp(fps, 10, 60);
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "CryoUtils GDI capture"
            };
            _thread.Start();
        }

        private void CaptureLoop()
        {
            long frameTicks = TimeSpan.TicksPerSecond / Math.Max(1, _fps);
            long next = System.Diagnostics.Stopwatch.GetTimestamp();
            while (_running)
            {
                if (_paused)
                {
                    Thread.Sleep(30);
                    next = System.Diagnostics.Stopwatch.GetTimestamp();
                    continue;
                }

                try
                {
                    var frame = ScreenCaptureService.CaptureRegion(new NativeMethods.RECT
                    {
                        Left = _region.X,
                        Top = _region.Y,
                        Right = _region.X + _region.Width,
                        Bottom = _region.Y + _region.Height
                    });
                    var converted = frame.Format == System.Windows.Media.PixelFormats.Bgra32
                        ? frame
                        : new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    int stride = converted.PixelWidth * 4;
                    var pixels = new byte[stride * converted.PixelHeight];
                    converted.CopyPixels(pixels, stride, 0);
                    FrameArrived?.Invoke(pixels, converted.PixelWidth, converted.PixelHeight);
                }
                catch
                {
                }

                next += frameTicks;
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                long waitMs = (next - now) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                if (waitMs > 0)
                {
                    Thread.Sleep((int)Math.Min(100, waitMs));
                }
                else
                {
                    next = now;
                }
            }
        }

        public void Pause() => _paused = true;
        public void Resume() => _paused = false;

        public void Stop()
        {
            _running = false;
            _thread?.Join(700);
            _thread = null;
        }

        public void Dispose() => Stop();
    }
}
