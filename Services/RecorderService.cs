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
    public sealed class RecordingResult
    {
        public List<byte[]> PngFrames { get; init; } = new();
        public int Width { get; init; }
        public int Height { get; init; }
        public int Fps { get; init; }
        public Int32Rect Region { get; init; }
    }

    public sealed class RecorderService : IDisposable
    {
        private const long MemoryGuardBytes = 380L * 1024 * 1024;

        private readonly object _frameLock = new();
        private readonly List<byte[]> _frames = new();
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
                    return _frames.Count;
                }
            }
        }

        public long EstimatedBytes
        {
            get
            {
                lock (_frameLock)
                {
                    if (_frames.Count == 0)
                    {
                        return 0;
                    }
                    long average = _totalBytes / _frames.Count;
                    return average * _frames.Count * 65 / 100;
                }
            }
        }

        public void Start(Int32Rect region, int fps, int maxSeconds)
        {
            if (IsRecording)
            {
                return;
            }

            _region = region;
            _fps = Math.Clamp(fps, 5, 30);
            _maxSeconds = Math.Clamp(maxSeconds, 3, 120);
            _frames.Clear();
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
                            lock (_frameLock)
                            {
                                _frames.Add(png);
                                _totalBytes += png.Length;
                            }

                            if (_totalBytes >= MemoryGuardBytes)
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

            List<byte[]> frames;
            lock (_frameLock)
            {
                frames = new List<byte[]>(_frames);
                _frames.Clear();
            }

            return new RecordingResult
            {
                PngFrames = frames,
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
                _frames.Clear();
                _totalBytes = 0;
            }
            Changed?.Invoke();
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
        }
    }
}
