using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace CryoUtils.Services.Recording
{
    /// <summary>
    /// One normal MP4 recording. Video is encoded while it is captured; the
    /// temporary MP4 is moved to its final location by the caller.
    /// </summary>
    public sealed class RecordingSession : IDisposable
    {
        private readonly Int32Rect _region;
        private readonly RecordingOptions _options;
        private readonly Stopwatch _clock = new();
        private readonly object _gate = new();
        private ICaptureEngine? _engine;
        private MfEncoder? _encoder;
        private CursorEffectTracker? _cursor;
        private WebcamCaptureService? _webcam;
        private AudioMixer? _audio;
        private long _pausedTicks;
        private long _pauseStartedTicks;
        private volatile bool _paused;
        private volatile bool _stopping;
        private volatile bool _started;
        private long _frames;
        private long _drops;
        private int _blackStreak;
        private int _nonBlackStreak;
        private bool _looksBlack;
        private string _tempPath = "";
        private MonitorInfo? _monitor;

        public event Action? Changed;
        public event Action<string>? StatusChanged;
        public bool IsRecording => _started && !_stopping;
        public bool IsPaused => _paused;
        public bool IsUsingWgc => _engine?.IsUsingWgc == true;
        public bool HasAudio => _encoder?.HasAudio == true;
        public float AudioLevel => _audio?.CurrentPeakLevel ?? 0f;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Fps => _options.NormalizedFps;
        public long Frames => _frames;
        public long DroppedFrames => _drops + (_engine?.DroppedFrames ?? 0);
        public bool LooksBlack => _looksBlack;
        public string TemporaryPath => _tempPath;
        public string LastError { get; private set; } = "";
        public bool FinalizeFailed { get; private set; }

        public TimeSpan Elapsed
        {
            get
            {
                if (!_started)
                {
                    return TimeSpan.Zero;
                }

                long paused = _pausedTicks;
                if (_paused)
                {
                    paused += _clock.ElapsedTicks - _pauseStartedTicks;
                }
                return TimeSpan.FromTicks(Math.Max(0, _clock.ElapsedTicks - paused));
            }
        }

        public RecordingSession(Int32Rect region, RecordingOptions options)
        {
            _region = region;
            _options = options;
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            var monitor = FindMonitor(_region);
            if (monitor == null)
            {
                throw new InvalidOperationException("The selected region is not on a display.");
            }
            _monitor = monitor;

            var size = _options.ResolveSize(_region.Width, _region.Height);
            Width = size.Width;
            Height = size.Height;
            int bitrate = _options.ResolveBitrate(Width, Height);
            _tempPath = Path.Combine(
                Path.GetTempPath(),
                "CryoUtils",
                $"recording_{Guid.NewGuid():N}.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(_tempPath)!);
            EnsureDiskSpace(bitrate);

            try
            {
                _encoder = new MfEncoder(
                    _tempPath,
                    Width,
                    Height,
                    _options.NormalizedFps,
                    bitrate,
                    _options.RecordSystemAudio || _options.RecordMicrophone);
                _cursor = new CursorEffectTracker(_options.KeystrokeHud);
                _cursor.Start();
                _clock.Start();
                _started = true;
                _blackStreak = 0;
                _nonBlackStreak = 0;
                _looksBlack = false;

                if (_options.WebcamBubble)
                {
                    _ = StartWebcamAsync();
                }

                if (_options.RecordSystemAudio || _options.RecordMicrophone)
                {
                    if (!_encoder.HasAudio)
                    {
                        LastError = string.IsNullOrEmpty(_encoder.LastError)
                            ? "Audio encoder unavailable; recording video only."
                            : _encoder.LastError;
                        StatusChanged?.Invoke(LocalizationService.Get("Rec_AudioUnavailable"));
                    }
                    else
                    {
                        _audio = new AudioMixer(
                            _options.RecordSystemAudio,
                            _options.RecordMicrophone,
                            _options.SystemAudioDeviceId,
                            _options.MicrophoneDeviceId);
                        _audio.MixedSamples += OnAudio;
                        if (!_audio.Start())
                        {
                            LastError = _audio.LastError;
                            StatusChanged?.Invoke(LocalizationService.Get("Rec_AudioUnavailable"));
                        }
                    }
                }

                StartCapture(monitor);
            }
            catch
            {
                StopInternal(deleteFile: true);
                throw;
            }
        }

        private void EnsureDiskSpace(int bitrate)
        {
            try
            {
                long required = Math.Max(1L, bitrate) * _options.NormalizedMaxSeconds / 8L;
                string? root = Path.GetPathRoot(_tempPath);
                long free = new DriveInfo(root ?? "C:").AvailableFreeSpace;
                if (!ReplayMath.HasEnoughFreeSpace(free, required))
                {
                    throw new InvalidOperationException(
                        LocalizationService.Get("Rec_NoDiskSpace"));
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // A drive-info failure should not block an otherwise valid capture.
            }
        }

        private void StartCapture(MonitorInfo monitor)
        {
            bool crossMonitor =
                _region.X < monitor.Bounds.Left ||
                _region.Y < monitor.Bounds.Top ||
                _region.X + _region.Width > monitor.Bounds.Right ||
                _region.Y + _region.Height > monitor.Bounds.Bottom;

            if (!crossMonitor && monitor.Handle != IntPtr.Zero)
            {
                var wgc = new WgcCaptureEngine(monitor.Handle, monitor.Bounds, _region);
                try
                {
                    wgc.FrameArrived += OnFrame;
                    wgc.Start();
                    _engine = wgc;
                    return;
                }
                catch (Exception ex)
                {
                    wgc.Dispose();
                    LastError = ex.Message;
                }
            }

            var gdi = new GdiCaptureEngine(_region, Math.Min(30, _options.NormalizedFps));
            gdi.FrameArrived += OnFrame;
            gdi.Start();
            _engine = gdi;
            if (crossMonitor)
            {
                StatusChanged?.Invoke(LocalizationService.Get("Rec_CrossMonitorFallback"));
            }
            else
            {
                StatusChanged?.Invoke(LocalizationService.Get("Rec_GdiFallback"));
            }
        }

        private async Task StartWebcamAsync()
        {
            try
            {
                _webcam = new WebcamCaptureService();
                if (!await _webcam.StartAsync().ConfigureAwait(false))
                {
                    StatusChanged?.Invoke(_webcam.LastError);
                }
            }
            catch
            {
            }
        }


        private void OnFrame(byte[] pixels, int width, int height)
        {
            if (_paused || _stopping || _encoder == null)
            {
                return;
            }

            try
            {
                UpdateBlackState(pixels);
                var clicks = _cursor?.Snapshot();
                var keyHud = _cursor?.SnapshotKeyHud() ?? ("", 0.0);
                var composed = FrameComposer.Compose(
                    pixels,
                    width,
                    height,
                    Width,
                    Height,
                    _region.X,
                    _region.Y,
                    _options,
                    clicks ?? Array.Empty<ClickPulse>(),
                    _webcam,
                    keyHud.Text,
                    keyHud.Alpha);
                long time = Elapsed.Ticks;
                _encoder.WriteVideo(composed, time);
                _frames++;
                if ((_frames & 0x0F) == 0)
                {
                    Changed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _drops++;
                LastError = ex.Message;
            }
        }

        private void UpdateBlackState(byte[] pixels)
        {
            if (pixels.Length < 64)
            {
                return;
            }

            int totalPixels = pixels.Length / 4;
            int step = Math.Max(1, totalPixels / 4096);
            int samples = 0;
            int dark = 0;
            for (int pixel = 0; pixel < totalPixels; pixel += step)
            {
                int offset = pixel * 4;
                if (pixels[offset] <= 8 &&
                    pixels[offset + 1] <= 8 &&
                    pixels[offset + 2] <= 8)
                {
                    dark++;
                }
                samples++;
            }

            bool mostlyBlack = samples > 0 && dark >= samples * 0.995;
            if (mostlyBlack)
            {
                _nonBlackStreak = 0;
                _blackStreak++;
                if (!_looksBlack && _blackStreak >= 10)
                {
                    _looksBlack = true;
                    Changed?.Invoke();
                }
            }
            else
            {
                _blackStreak = 0;
                if (_looksBlack)
                {
                    _nonBlackStreak++;
                    if (_nonBlackStreak >= 30)
                    {
                        _looksBlack = false;
                        Changed?.Invoke();
                    }
                }
            }
        }

        private void OnAudio(short[] samples)
        {
            if (_paused || _stopping || _encoder == null)
            {
                return;
            }

            try
            {
                _encoder.WriteAudio(samples, Elapsed.Ticks);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        public void Pause()
        {
            if (!_started || _paused || _stopping)
            {
                return;
            }

            _paused = true;
            _pauseStartedTicks = _clock.ElapsedTicks;
            _engine?.Pause();
            _audio?.Pause();
            Changed?.Invoke();
        }

        public void Resume()
        {
            if (!_started || !_paused || _stopping)
            {
                return;
            }

            _pausedTicks += _clock.ElapsedTicks - _pauseStartedTicks;
            _paused = false;
            _engine?.Resume();
            _audio?.Resume();
            Changed?.Invoke();
        }

        public async Task<string> StopAsync()
        {
            if (!_started)
            {
                return _tempPath;
            }

            _stopping = true;
            var path = _tempPath;
            await Task.Run(() => StopInternal(deleteFile: false)).ConfigureAwait(false);
            return path;
        }

        private void StopInternal(bool deleteFile)
        {
            _started = false;
            _stopping = true;
            try
            {
                _engine?.Stop();
                _engine?.Dispose();
                _engine = null;
                _cursor?.Dispose();
                _cursor = null;
                _audio?.Dispose();
                _audio = null;
                _webcam?.Dispose();
                _webcam = null;
                if (_encoder != null)
                {
                    _encoder.Finish();
                    FinalizeFailed = !_encoder.IsFinalized;
                    if (FinalizeFailed)
                    {
                        LastError = string.IsNullOrEmpty(_encoder.LastError)
                            ? "The MP4 file could not be finalized."
                            : _encoder.LastError;
                    }

                    _encoder.Dispose();
                    _encoder = null;
                }
                _clock.Stop();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            if (deleteFile)
            {
                try
                {
                    if (File.Exists(_tempPath))
                    {
                        File.Delete(_tempPath);
                    }
                }
                catch
                {
                }
            }

            _stopping = false;
            Changed?.Invoke();
        }

        public void Cancel()
        {
            if (!_started)
            {
                return;
            }

            StopInternal(deleteFile: true);
        }

        public void Dispose()
        {
            if (_started)
            {
                StopInternal(deleteFile: true);
            }
            _audio?.Dispose();
            _webcam?.Dispose();
        }

        private static MonitorInfo? FindMonitor(Int32Rect region)
        {
            var monitors = ScreenCaptureService.GetMonitors();
            int centerX = region.X + region.Width / 2;
            int centerY = region.Y + region.Height / 2;
            foreach (var monitor in monitors)
            {
                if (centerX >= monitor.Bounds.Left && centerX < monitor.Bounds.Right &&
                    centerY >= monitor.Bounds.Top && centerY < monitor.Bounds.Bottom)
                {
                    return monitor;
                }
            }

            return monitors.Count > 0 ? monitors[0] : null;
        }
    }
}
