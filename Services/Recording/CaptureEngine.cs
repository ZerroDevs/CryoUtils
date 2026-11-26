using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

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
    /// Windows.Graphics.Capture monitor capture with a CPU readback. The monitor
    /// frame is cropped to the selected physical region before it is handed to
    /// the encoder. WGC is deliberately kept behind this small interface so a
    /// GDI fallback can be used on older or blocked systems.
    /// </summary>
    internal sealed class WgcCaptureEngine : ICaptureEngine
    {
        private static readonly Guid GraphicsCaptureItemGuid =
            new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        private readonly IntPtr _monitor;
        private readonly NativeMethods.RECT _monitorBounds;
        private readonly System.Windows.Int32Rect _region;
        private const int FramePoolBufferCount = 4;
        private const int FrameQueueCapacity = 4;
        private readonly SemaphoreSlim _copyGate = new(1, 1);
        private BlockingCollection<CaptureFrame>? _frames;
        private Thread? _worker;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private IDirect3DDevice? _device;
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

        public WgcCaptureEngine(IntPtr monitor, NativeMethods.RECT monitorBounds, System.Windows.Int32Rect region)
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

            var item = CreateItem(_monitor);
            var size = item.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                size = new SizeInt32
                {
                    Width = Math.Max(1, _monitorBounds.Width),
                    Height = Math.Max(1, _monitorBounds.Height)
                };
            }

            var device = CreateDevice(size.Width, size.Height);
            _device = device.Device;
            _readback = device.Readback;
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                FramePoolBufferCount,
                size);
            _session = _framePool.CreateCaptureSession(item);
            _session.IsCursorCaptureEnabled = true;
            _framePool.FrameArrived += FramePool_FrameArrived;
            _running = true;
            _session.StartCapture();
        }

        private async void FramePool_FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!_running)
            {
                return;
            }

            var frame = sender.TryGetNextFrame();
            if (frame == null)
            {
                return;
            }

            if (_paused)
            {
                // Keep draining the pool while paused. Leaving a frame pending
                // fills the pool and stops FrameArrived from ever firing again.
                frame.Dispose();
                return;
            }

            long waitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!await _copyGate.WaitAsync(50).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _dropped);
                frame.Dispose();
                return;
            }

            LastWaitTicks = System.Diagnostics.Stopwatch.GetTimestamp() - waitStarted;
            try
            {
                using (frame)
                {
                    if (_readback != null)
                    {
                        long readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                        bool read = _readback.TryRead(
                            frame.Surface,
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

                    using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                    if (bitmap == null)
                    {
                        return;
                    }

                    int width = bitmap.PixelWidth;
                    int height = bitmap.PixelHeight;
                    int stride = width * 4;
                    var pixels = new byte[stride * height];
                    var buffer = new Windows.Storage.Streams.Buffer((uint)pixels.Length);
                    bitmap.CopyToBuffer(buffer);
                    using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer))
                    {
                        reader.ReadBytes(pixels);
                    }

                    EnqueuePixels(pixels, width, height);
                }
            }
            catch
            {
                // A transient frame or readback failure must not stop a recording.
                Interlocked.Increment(ref _dropped);
            }
            finally
            {
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

        private static GraphicsCaptureItem CreateItem(IntPtr monitor)
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            Guid iid = GraphicsCaptureItemGuid;
            IntPtr pointer = interop.CreateForMonitor(monitor, ref iid);
            try
            {
                return GraphicsCaptureItem.FromAbi(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }

        private static (IDirect3DDevice Device, D3D11Readback? Readback) CreateDevice(
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
                try
                {
                    return (MarshalInterface<IDirect3DDevice>.FromAbi(inspectable), readback);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
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
                if (_framePool != null)
                {
                    _framePool.FrameArrived -= FramePool_FrameArrived;
                }
                _session?.Dispose();
                _framePool?.Dispose();
                _readback?.Dispose();
                _readback = null;
                _device?.Dispose();
                _session = null;
                _framePool = null;
                _device = null;
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
            // A frame copy can still complete after Stop. Leave the semaphore
            // alive so its finally block can release it safely.
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

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
            IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
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
