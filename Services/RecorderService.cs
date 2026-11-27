using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public sealed class RecordingResult : IDisposable
    {
        public List<string> FramePaths { get; init; } = new();
        public string TempFolder { get; init; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
        public int Fps { get; init; }
        public Int32Rect Region { get; init; }

        public void Dispose()
        {
            try
            {
                if (!string.IsNullOrEmpty(TempFolder) && Directory.Exists(TempFolder))
                {
                    Directory.Delete(TempFolder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    public sealed class RecorderService : IDisposable
    {
        private const long DiskGuardBytes = 380L * 1024 * 1024;

        private readonly object _frameLock = new();
        private readonly List<string> _framePaths = new();
        private string _tempFolder = "";
        private CancellationTokenSource? _cancellation;
        private Task? _captureTask;
        private Stopwatch? _clock;
        private Int32Rect _region;
        private int _fps = 15;
        private int _maxSeconds = 30;
        private long _totalBytes;
        private long _pausedTicks;
        private long _pauseStartedTicks;
        private volatile bool _paused;
        private volatile bool _encoding;

        public event Action? Changed;

        public bool IsRecording { get; private set; }
        public bool IsPaused => _paused;
        public bool IsEncoding => _encoding;
        public TimeSpan Elapsed
        {
            get
            {
                if (_clock == null)
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
        public int FrameCount
        {
            get
            {
                lock (_frameLock)
                {
                    return _framePaths.Count;
                }
            }
        }

        public long EstimatedBytes
        {
            get
            {
                lock (_frameLock)
                {
                    if (_framePaths.Count == 0)
                    {
                        return 0;
                    }
                    long average = _totalBytes / _framePaths.Count;
                    return average * _framePaths.Count * 65 / 100;
                }
            }
        }

        public void Start(Int32Rect region, int fps, int maxSeconds)
        {
            if (IsRecording)
            {
                return;
            }

            CleanupTempFolder();
            _region = region;
            _fps = Math.Clamp(fps, 5, 30);
            _maxSeconds = Math.Clamp(maxSeconds, 3, 120);
            _framePaths.Clear();
            _tempFolder = Path.Combine(Path.GetTempPath(), "CryoUtils", $"gif_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempFolder);
            _totalBytes = 0;
            _pausedTicks = 0;
            _pauseStartedTicks = 0;
            _paused = false;
            _encoding = false;
            IsRecording = true;
            _clock = Stopwatch.StartNew();
            _cancellation = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoopAsync(_cancellation.Token));
            Changed?.Invoke();
        }

        private async Task CaptureLoopAsync(CancellationToken token)
        {
            var nextFrame = Stopwatch.StartNew();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (Elapsed.TotalSeconds >= _maxSeconds)
                    {
                        break;
                    }

                    double frameInterval = 1000.0 / Math.Max(1, _fps);

                    if (!_paused)
                    {
                        try
                        {
                            var frame = ScreenCaptureService.CaptureRegion(new NativeMethods.RECT
                            {
                                Left = _region.X,
                                Top = _region.Y,
                                Right = _region.X + _region.Width,
                                Bottom = _region.Y + _region.Height
                            });
                            byte[] png = ImageIO.EncodePng(frame);
                            string framePath;
                            lock (_frameLock)
                            {
                                framePath = Path.Combine(_tempFolder, $"f_{_framePaths.Count:D6}.png");
                            }
                            File.WriteAllBytes(framePath, png);
                            lock (_frameLock)
                            {
                                _framePaths.Add(framePath);
                                _totalBytes += png.Length;
                            }

                            if (_totalBytes >= DiskGuardBytes)
                            {
                                break;
                            }
                        }
                        catch
                        {
                            // A transient GDI failure should not kill the whole recording.
                        }
                    }

                    Changed?.Invoke();
                    double remaining = frameInterval - nextFrame.Elapsed.TotalMilliseconds;
                    nextFrame.Restart();
                    if (remaining > 0)
                    {
                        await Task.Delay((int)remaining, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Pause()
        {
            if (_paused || _clock == null)
            {
                return;
            }

            _paused = true;
            _pauseStartedTicks = _clock.ElapsedTicks;
            Changed?.Invoke();
        }

        public void Resume()
        {
            if (!_paused || _clock == null)
            {
                return;
            }

            _pausedTicks += _clock.ElapsedTicks - _pauseStartedTicks;
            _paused = false;
            Changed?.Invoke();
        }

        public void SetFps(int fps)
        {
            _fps = Math.Clamp(fps, 5, 30);
            Changed?.Invoke();
        }

        public async Task<RecordingResult> StopAsync()
        {
            if (!IsRecording)
            {
                return new RecordingResult
                {
                    Width = _region.Width,
                    Height = _region.Height,
                    Fps = _fps,
                    Region = _region
                };
            }

            _cancellation?.Cancel();
            IsRecording = false;
            _clock?.Stop();
            if (_captureTask != null)
            {
                try
                {
                    await _captureTask.ConfigureAwait(true);
                }
                catch
                {
                }
            }

            List<string> framePaths;
            string folder;
            lock (_frameLock)
            {
                framePaths = new List<string>(_framePaths);
                _framePaths.Clear();
                folder = _tempFolder;
                _tempFolder = "";
            }

            return new RecordingResult
            {
                FramePaths = framePaths,
                TempFolder = folder,
                Width = _region.Width,
                Height = _region.Height,
                Fps = _fps,
                Region = _region
            };
        }

        public void Cancel()
        {
            _cancellation?.Cancel();
            IsRecording = false;
            _clock?.Stop();
            lock (_frameLock)
            {
                _framePaths.Clear();
                _totalBytes = 0;
            }
            CleanupTempFolder();
            Changed?.Invoke();
        }

        private void CleanupTempFolder()
        {
            string folder;
            lock (_frameLock)
            {
                folder = _tempFolder;
                _tempFolder = "";
            }
            try
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }

        public void SetEncoding(bool encoding)
        {
            _encoding = encoding;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            CleanupTempFolder();
        }
    }
}
