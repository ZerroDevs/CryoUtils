using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services.Recording
{
    /// <summary>
    /// Always-on, disk-backed instant replay. The service owns a segment ring
    /// and only stops the capture when the user turns the buffer off or exits.
    /// </summary>
    public sealed class ReplayService : IDisposable
    {
        private static ReplayService? _instance;
        public static ReplayService Instance => _instance ??= new ReplayService();

        private readonly object _gate = new();
        private readonly Stopwatch _clock = new();
        private ICaptureEngine? _engine;
        private ReplaySegmentRing? _ring;
        private CursorEffectTracker? _cursor;
        private WebcamCaptureService? _webcam;
        private AudioMixer? _audio;
        private RecordingOptions? _options;
        private Int32Rect _region;
        private int _width;
        private int _height;
        private bool _running;
        private bool _paused;
        private long _frames;
        private string _lastSavedPath = "";
        private string _status = "";

        public event Action? StateChanged;
        public event Action<string>? StatusChanged;

        public bool IsRunning => _running;
        public bool IsPaused => _paused;
        public bool IsUsingWgc => _engine?.IsUsingWgc == true;
        public string LastError { get; private set; } = "";
        public string LastSavedPath => _lastSavedPath;
        public string Status => _status;
        public int BufferedSeconds => _ring == null
            ? 0
            : (int)Math.Min(int.MaxValue, _ring.BufferedDuration.TotalSeconds);
        public bool HasAudio => _ring?.HasAudio == true;
        public long BufferedBytes => _ring?.BufferedBytes ?? 0;
        public int Width => _width;
        public int Height => _height;
        public long Frames => _frames;

        private ReplayService()
        {
        }

        public bool Start()
        {
            lock (_gate)
            {
                if (_running)
                {
                    return true;
                }

                var settings = SettingsService.Instance.Current;
                var options = RecordingOptions.FromSettings(settings);
                LastError = "";
                var monitor = ResolveMonitor(options.ReplayMonitor);
                if (monitor == null)
                {
                    LastError = "No display is available for instant replay.";
                    return false;
                }

                _region = new Int32Rect(
                    monitor.Bounds.Left,
                    monitor.Bounds.Top,
                    monitor.Bounds.Width,
                    monitor.Bounds.Height);
                var size = options.ResolveSize(_region.Width, _region.Height);
                _width = size.Width;
                _height = size.Height;
                int bitrate = options.ResolveBitrate(_width, _height);
                long estimate = ReplayMath.EstimatedDiskBytes(bitrate, options.NormalizedReplaySeconds);
                if (options.ReplayDiskBudgetBytes > 0)
                {
                    estimate = Math.Min(estimate, options.ReplayDiskBudgetBytes);
                }
                string? root = Path.GetPathRoot(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CryoUtils"));
                try
                {
                    long free = new DriveInfo(root ?? "C:").AvailableFreeSpace;
                    if (!ReplayMath.HasEnoughFreeSpace(free, estimate))
                    {
                        LastError = LocalizationService.Get("Rec_ReplayDiskFull");
                        return false;
                    }
                }
                catch
                {
                }

                try
                {
                    string directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CryoUtils",
                        "ReplayBuffer");
                    Directory.CreateDirectory(directory);
                    _options = options;
                    _ring = new ReplaySegmentRing(directory, options, _width, _height, bitrate);
                    _ring.Start();
                    _clock.Restart();
                    _cursor = new CursorEffectTracker(_options.KeystrokeHud);
                    _cursor.Start();
                    _frames = 0;
                    _paused = false;

                    if (options.WebcamBubble)
                    {
                        _ = StartWebcamAsync();
                    }

                    if ((options.RecordSystemAudio || options.RecordMicrophone) && !_ring.HasAudio)
                    {
                        LastError = string.IsNullOrEmpty(_ring.LastError)
                            ? "Audio encoder unavailable; replay is video only."
                            : _ring.LastError;
                    }
                    else if (options.RecordSystemAudio || options.RecordMicrophone)
                    {
                        _audio = new AudioMixer(
                            options.RecordSystemAudio,
                            options.RecordMicrophone,
                            options.SystemAudioDeviceId,
                            options.MicrophoneDeviceId);
                        _audio.MixedSamples += OnAudio;
                        if (!_audio.Start())
                        {
                            LastError = _audio.LastError;
                        }
                    }

                    StartCapture(monitor);
                    _running = true;
                    _status = "Replay buffer running";
                    StateChanged?.Invoke();
                    StatusChanged?.Invoke(_status);
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    StopInternal();
                    return false;
                }
            }
        }

        private void StartCapture(MonitorInfo monitor)
        {
            var wgc = new WgcCaptureEngine(monitor.Handle, monitor.Bounds, _region);
            try
            {
                wgc.FrameArrived += OnFrame;
                wgc.Start();
                _engine = wgc;
            }
            catch (Exception ex)
            {
                wgc.Dispose();
                LastError = ex.Message;
                var gdi = new GdiCaptureEngine(_region, Math.Min(30, _options?.NormalizedFps ?? 30));
                gdi.FrameArrived += OnFrame;
                gdi.Start();
                _engine = gdi;
            }
        }

        private async Task StartWebcamAsync()
        {
            try
            {
                _webcam = new WebcamCaptureService();
                if (!await _webcam.StartAsync().ConfigureAwait(false))
                {
                    LastError = _webcam.LastError;
                }
            }
            catch
            {
            }
        }

        private void OnFrame(byte[] pixels, int width, int height)
        {
            if (!_running || _paused || _ring == null || _options == null)
            {
                return;
            }

            try
            {
                var keyHud = _cursor?.SnapshotKeyHud() ?? ("", 0.0);
                var composed = FrameComposer.Compose(
                    pixels,
                    width,
                    height,
                    _width,
                    _height,
                    _region.X,
                    _region.Y,
                    _options,
                    _cursor?.Snapshot() ?? Array.Empty<ClickPulse>(),
                    _webcam,
                    keyHud.Text,
                    keyHud.Alpha);
                long ticks = _clock.Elapsed.Ticks;
                _ring.WriteVideo(composed, ticks);
                _frames++;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private void OnAudio(short[] samples)
        {
            if (!_running || _paused || _ring == null)
            {
                return;
            }

            _ring.WriteAudio(samples, _clock.Elapsed.Ticks);
        }

        public void Pause()
        {
            if (!_running || _paused)
            {
                return;
            }

            _paused = true;
            _engine?.Pause();
            _audio?.Pause();
            StateChanged?.Invoke();
        }

        public void Resume()
        {
            if (!_running || !_paused)
            {
                return;
            }

            _paused = false;
            _engine?.Resume();
            _audio?.Resume();
            StateChanged?.Invoke();
        }

        public void Stop()
        {
            lock (_gate)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            _running = false;
            _paused = false;
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
                _ring?.Stop();
                _ring?.Dispose();
                _ring = null;
                _clock.Stop();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            _status = "Replay buffer stopped";
            StateChanged?.Invoke();
            StatusChanged?.Invoke(_status);
        }

        public async Task<string> SaveLastAsync(
            int? seconds = null,
            string? outputPath = null,
            bool copyToClipboard = false,
            bool addToLibrary = true)
        {
            if (!_running || _ring == null || _options == null)
            {
                throw new InvalidOperationException("Instant Replay is not running.");
            }

            int duration = Math.Clamp(
                seconds ?? _options.NormalizedReplaySeconds,
                RecordingOptions.MinimumReplaySeconds,
                RecordingOptions.MaximumReplaySeconds);
            var ring = _ring;
            ring.SealCurrent();
            var snapshot = ring.Snapshot(duration);
            if (snapshot.Count == 0)
            {
                throw new InvalidOperationException("The replay buffer is still warming up.");
            }

            ring.Pin(snapshot);
            string path = outputPath ?? CreateDefaultOutputPath();
            try
            {
                try
                {
                    await Task.Run(() => ReplayRemuxer.Remux(snapshot, path)).ConfigureAwait(false);
                }
                catch
                {
                    try
                    {
                        await ReplayRemuxer.ReencodeAsync(snapshot, path).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LastError = ex.Message;
                        throw;
                    }
                }
            }
            finally
            {
                ring.Unpin(snapshot);
            }

            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                throw new InvalidOperationException("The replay file was not written.");
            }

            _lastSavedPath = path;
            _status = $"Replay saved: {Path.GetFileName(path)}";
            StatusChanged?.Invoke(_status);
            if (addToLibrary)
            {
                await AddToLibraryAsync(path).ConfigureAwait(false);
            }

            if (copyToClipboard)
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Clipboard.SetFileDropList(new StringCollection { path });
                    }
                    catch
                    {
                    }
                }));
            }

            return path;
        }

        private static string CreateDefaultOutputPath()
        {
            string folder;
            try
            {
                folder = SettingsService.Instance.EffectiveSaveFolder;
            }
            catch
            {
                folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    "CryoUtils");
            }

            Directory.CreateDirectory(folder);
            return Path.Combine(
                folder,
                ScreenCaptureService.CreateTimestampedFileName(".mp4"));
        }

        public static async Task AddToLibraryAsync(string path)
        {
            string thumbnailPath = Path.Combine(
                Path.GetTempPath(),
                "CryoUtils",
                $"replay_thumb_{Guid.NewGuid():N}.png");
            var frame = ImageIO.CreateVideoThumbnailPlaceholder(320, 180);
            try
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
                    await MediaConversionService.SaveThumbnailAsync(path, thumbnailPath)
                        .ConfigureAwait(false);
                    frame = ImageIO.LoadThumbnail(thumbnailPath, 320);
                }
                catch
                {
                    // The MP4 is still valid; a poster frame is optional.
                }

                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    RecentCaptureService.Instance.Add(
                        frame,
                        path,
                        LocalizationService.Get("Rec_ReplayTitle"));
                    try
                    {
                        CaptureLibraryService.Instance.Record(
                            path,
                            "CryoUtils",
                            LocalizationService.Get("Rec_ReplayTitle"));
                    }
                    catch
                    {
                        // Library metadata is optional; the saved replay is still visible.
                    }
                }));
            }
            finally
            {
                try
                {
                    if (File.Exists(thumbnailPath))
                    {
                        File.Delete(thumbnailPath);
                    }
                }
                catch
                {
                }
            }
        }

        private static MonitorInfo? ResolveMonitor(string? preference)
        {
            var monitors = ScreenCaptureService.GetMonitors();
            if (monitors.Count == 0)
            {
                return null;
            }

            if (string.Equals(preference, "Cursor", StringComparison.OrdinalIgnoreCase))
            {
                NativeMethods.GetCursorPos(out var cursor);
                var underCursor = monitors.FirstOrDefault(monitor =>
                    cursor.X >= monitor.Bounds.Left && cursor.X < monitor.Bounds.Right &&
                    cursor.Y >= monitor.Bounds.Top && cursor.Y < monitor.Bounds.Bottom);
                if (underCursor != null)
                {
                    return underCursor;
                }
            }

            return monitors[0];
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
