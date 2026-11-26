using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace CryoUtils.Services.Recording
{
    /// <summary>
    /// Optional default-camera preview used for the recording bubble. Camera
    /// frames stay in memory only and are never written to disk separately.
    /// </summary>
    public sealed class WebcamCaptureService : IDisposable
    {
        private readonly object _gate = new();
        private MediaCapture? _capture;
        private MediaFrameReader? _reader;
        private byte[]? _latest;
        private int _width;
        private int _height;
        private bool _running;

        public bool IsRunning => _running;
        public string LastError { get; private set; } = "";

        public async Task<bool> StartAsync()
        {
            if (_running)
            {
                return true;
            }

            try
            {
                var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                var device = devices.FirstOrDefault();
                if (device == null)
                {
                    LastError = "No camera was found.";
                    return false;
                }

                _capture = new MediaCapture();
                await _capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = device.Id,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly
                });

                var source = _capture.FrameSources.Values
                    .FirstOrDefault(item => item.Info.SourceKind == MediaFrameSourceKind.Color);
                if (source == null)
                {
                    LastError = "The camera did not expose a colour stream.";
                    Dispose();
                    return false;
                }

                _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
                _reader.FrameArrived += Reader_FrameArrived;
                await _reader.StartAsync();
                _running = true;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Dispose();
                return false;
            }
        }

        private void Reader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            try
            {
                using var frame = sender.TryAcquireLatestFrame();
                using var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
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
                using (var reader = DataReader.FromBuffer(buffer))
                {
                    reader.ReadBytes(pixels);
                }

                lock (_gate)
                {
                    _latest = pixels;
                    _width = width;
                    _height = height;
                }
            }
            catch
            {
            }
        }

        public bool TryGetFrame(out byte[] pixels, out int width, out int height)
        {
            lock (_gate)
            {
                if (_latest == null)
                {
                    pixels = Array.Empty<byte>();
                    width = 0;
                    height = 0;
                    return false;
                }

                pixels = _latest;
                width = _width;
                height = _height;
                return true;
            }
        }

        public async Task StopAsync()
        {
            _running = false;
            if (_reader != null)
            {
                _reader.FrameArrived -= Reader_FrameArrived;
                try
                {
                    await _reader.StopAsync();
                }
                catch
                {
                }
                _reader.Dispose();
                _reader = null;
            }

            _capture?.Dispose();
            _capture = null;
            lock (_gate)
            {
                _latest = null;
            }
        }

        public void Dispose()
        {
            _running = false;
            if (_reader != null)
            {
                _reader.FrameArrived -= Reader_FrameArrived;
                try
                {
                    _ = _reader.StopAsync();
                }
                catch
                {
                }
                _reader.Dispose();
                _reader = null;
            }

            _capture?.Dispose();
            _capture = null;
            lock (_gate)
            {
                _latest = null;
            }
        }
    }
}
