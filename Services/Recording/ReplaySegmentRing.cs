using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CryoUtils.Services.Recording
{
    public sealed class ReplaySegmentInfo
    {
        public string Path { get; init; } = "";
        public long StartTicks { get; init; }
        public long EndTicks { get; init; }
        public DateTime StartedAtUtc { get; init; }
        public bool HasAudio { get; init; }
        public long Bytes { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }

        public TimeSpan Duration => TimeSpan.FromTicks(Math.Max(0, EndTicks - StartTicks));
    }

    /// <summary>
    /// Disk-backed keyframe-aligned MP4 segments. Each segment is a complete
    /// file, which lets the replay saver remux compressed samples instead of
    /// decoding and re-encoding the whole buffer.
    /// </summary>
    public sealed class ReplaySegmentRing : IDisposable
    {
        private readonly object _gate = new();
        private readonly RecordingOptions _options;
        private readonly string _directory;
        private readonly int _width;
        private readonly int _height;
        private readonly int _bitrate;
        private readonly List<ReplaySegmentInfo> _segments = new();
        private readonly HashSet<string> _pinned = new(StringComparer.OrdinalIgnoreCase);
        private readonly int _retentionCount;
        private MfEncoder? _encoder;
        private long _segmentStartTicks;
        private long _lastTick;
        private bool _started;
        private bool _disposed;

        public bool HasAudio { get; private set; }
        public string LastError { get; private set; } = "";
        public long SegmentCount
        {
            get
            {
                lock (_gate)
                {
                    return _segments.Count;
                }
            }
        }

        public ReplaySegmentRing(
            string directory,
            RecordingOptions options,
            int width,
            int height,
            int bitrate)
        {
            _directory = directory;
            _options = options;
            _width = Math.Max(2, width & ~1);
            _height = Math.Max(2, height & ~1);
            _bitrate = bitrate;
            _retentionCount = ReplayMath.RetentionCount(
                options.NormalizedReplaySeconds,
                RecordingOptions.SegmentSeconds);
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            Directory.CreateDirectory(_directory);
            foreach (string stale in Directory.EnumerateFiles(_directory, "*.mp4"))
            {
                try
                {
                    File.Delete(stale);
                }
                catch
                {
                }
            }

            _segmentStartTicks = 0;
            _lastTick = 0;
            _started = true;
            StartSegment(0);
        }

        private void StartSegment(long startTicks)
        {
            string path = Path.Combine(
                _directory,
                $"segment_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.mp4");
            bool wantsAudio = _options.RecordSystemAudio || _options.RecordMicrophone;
            _encoder = new MfEncoder(
                path,
                _width,
                _height,
                _options.NormalizedFps,
                _bitrate,
                wantsAudio);
            _segmentStartTicks = startTicks;
            HasAudio = _encoder.HasAudio;
            if (wantsAudio && !HasAudio)
            {
                LastError = string.IsNullOrEmpty(_encoder.LastError)
                    ? "Audio encoder unavailable; replay is video only."
                    : _encoder.LastError;
            }
        }

        public void WriteVideo(byte[] bgra, long elapsedTicks)
        {
            lock (_gate)
            {
                if (!_started || _disposed || _encoder == null)
                {
                    return;
                }

                RotateIfNeeded(elapsedTicks);
                long local = Math.Max(0, elapsedTicks - _segmentStartTicks);
                try
                {
                    _encoder.WriteVideo(bgra, local);
                    _lastTick = Math.Max(_lastTick, elapsedTicks);
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }
        }

        public void WriteAudio(short[] samples, long elapsedTicks)
        {
            lock (_gate)
            {
                if (!_started || _disposed || _encoder == null || !_encoder.HasAudio)
                {
                    return;
                }

                RotateIfNeeded(elapsedTicks);
                long local = Math.Max(0, elapsedTicks - _segmentStartTicks);
                try
                {
                    _encoder.WriteAudio(samples, local);
                    _lastTick = Math.Max(_lastTick, elapsedTicks);
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }
        }

        private void RotateIfNeeded(long elapsedTicks)
        {
            if (_encoder == null)
            {
                return;
            }

            long segmentTicks = TimeSpan.FromSeconds(RecordingOptions.SegmentSeconds).Ticks;
            if (elapsedTicks - _segmentStartTicks < segmentTicks)
            {
                return;
            }

            FinishCurrentSegment(elapsedTicks);
            StartSegment(elapsedTicks);
            TrimToRetention();
        }

        private void FinishCurrentSegment(long endTicks)
        {
            var encoder = _encoder;
            if (encoder == null)
            {
                return;
            }

            string path = encoder.Path;
            try
            {
                encoder.Finish();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            try
            {
                long size = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (!encoder.IsFinalized)
                {
                    LastError = string.IsNullOrEmpty(encoder.LastError)
                        ? "A replay segment could not be finalized."
                        : encoder.LastError;
                    TryDelete(path);
                }
                else if (size > 0 && endTicks > _segmentStartTicks)
                {
                    _segments.Add(new ReplaySegmentInfo
                    {
                        Path = path,
                        StartTicks = _segmentStartTicks,
                        EndTicks = endTicks,
                        StartedAtUtc = DateTime.UtcNow - TimeSpan.FromTicks(endTicks - _segmentStartTicks),
                        HasAudio = encoder.HasAudio,
                        Bytes = size,
                        Width = _width,
                        Height = _height
                    });
                }
            }
            finally
            {
                encoder.Dispose();
                _encoder = null;
            }
        }

        private void TrimToRetention()
        {
            while (_segments.Count > _retentionCount)
            {
                var oldest = _segments.FirstOrDefault(segment => !_pinned.Contains(segment.Path));
                if (oldest == null)
                {
                    break;
                }

                _segments.Remove(oldest);
                TryDelete(oldest.Path);
            }
        }

        public void Pin(IEnumerable<ReplaySegmentInfo> segments)
        {
            lock (_gate)
            {
                foreach (var segment in segments)
                {
                    _pinned.Add(segment.Path);
                }
            }
        }

        public void Unpin(IEnumerable<ReplaySegmentInfo> segments)
        {
            lock (_gate)
            {
                foreach (var segment in segments)
                {
                                            _pinned.Remove(segment.Path);
                    if (_disposed || !_segments.Any(item =>
                            string.Equals(item.Path, segment.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        TryDelete(segment.Path);
                    }
                }
            }
        }

        /// <summary>
        /// Finalizes the partial segment so a save includes everything captured
        /// up to the moment the user asked for it. Capture immediately continues
        /// in a fresh segment.
        /// </summary>
        public void SealCurrent()
        {
            lock (_gate)
            {
                if (!_started || _encoder == null || _lastTick <= _segmentStartTicks)
                {
                    return;
                }

                long end = _lastTick;
                FinishCurrentSegment(end);
                StartSegment(end);
                TrimToRetention();
            }
        }

        public IReadOnlyList<ReplaySegmentInfo> Snapshot(int replaySeconds)
        {
            lock (_gate)
            {
                if (_segments.Count == 0)
                {
                    return Array.Empty<ReplaySegmentInfo>();
                }

                long newest = _segments[^1].EndTicks;
                long requested = TimeSpan.FromSeconds(Math.Clamp(
                    replaySeconds,
                    RecordingOptions.MinimumReplaySeconds,
                    RecordingOptions.MaximumReplaySeconds)).Ticks;
                long cutoff = newest - requested;
                var result = _segments
                    .Where(segment => segment.EndTicks >= cutoff)
                    .ToList();
                if (result.Count == 0)
                {
                    result.Add(_segments[^1]);
                }
                return result;
            }
        }

        public TimeSpan BufferedDuration
        {
            get
            {
                lock (_gate)
                {
                    if (_segments.Count == 0)
                    {
                        return TimeSpan.Zero;
                    }

                    return TimeSpan.FromTicks(_segments[^1].EndTicks - _segments[0].StartTicks);
                }
            }
        }

        public long BufferedBytes
        {
            get
            {
                lock (_gate)
                {
                    return _segments.Sum(segment => segment.Bytes);
                }
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (!_started)
                {
                    return;
                }

                if (_lastTick > _segmentStartTicks)
                {
                    FinishCurrentSegment(_lastTick);
                }
                else
                {
                    string? path = _encoder?.Path;
                    _encoder?.Dispose();
                    _encoder = null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        TryDelete(path);
                    }
                }

                _started = false;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                foreach (var segment in _segments)
                {
                    if (!_pinned.Contains(segment.Path))
                    {
                        TryDelete(segment.Path);
                    }
                }
                _segments.Clear();
                string? current = _encoder?.Path;
                _encoder?.Dispose();
                _encoder = null;
                if (!string.IsNullOrEmpty(current))
                {
                    TryDelete(current);
                }
                _started = false;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_started)
                {
                    if (_lastTick > _segmentStartTicks)
                    {
                        FinishCurrentSegment(_lastTick);
                    }
                    else
                    {
                        string? path = _encoder?.Path;
                        _encoder?.Dispose();
                        _encoder = null;
                        if (!string.IsNullOrEmpty(path))
                        {
                            TryDelete(path);
                        }
                    }
                    _started = false;
                }

                foreach (var segment in _segments)
                {
                    if (!_pinned.Contains(segment.Path))
                    {
                        TryDelete(segment.Path);
                    }
                }
                _segments.Clear();
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
