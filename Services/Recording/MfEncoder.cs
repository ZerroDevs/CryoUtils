using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CryoUtils.Services.Recording
{
    /// <summary>
    /// Live H.264/AAC MP4 writer. Video is supplied as top-down BGRA frames;
    /// the writer flips rows for Media Foundation's RGB32 input convention.
    /// </summary>
    public sealed class MfEncoder : IDisposable
    {
        private const int AudioSampleRate = 48_000;
        private const int AudioChannels = 2;
        private const int AudioBytesPerFrame = AudioChannels * 2;
        private readonly object _gate = new();
        private IMFSinkWriter? _writer;
        private AacMftEncoder? _aac;
        private uint _videoStream;
        private uint _audioStream;
        private int _width;
        private int _height;
        private int _fps;
        private bool _audioEnabled;
        private bool _began;
        private bool _finalized;
        private long _lastVideoTime = -1;
        private long _lastAudioTime = -1;

        public bool HasAudio => _audioEnabled;
        public string? LastError { get; private set; }
        public string Path { get; }
        public int Width => _width;
        public int Height => _height;
        public long FramesWritten { get; private set; }
        public long BytesWritten
        {
            get
            {
                try
                {
                    var info = new FileInfo(Path);
                    return info.Exists ? info.Length : 0;
                }
                catch
                {
                    return 0;
                }
            }
        }

        public MfEncoder(string path, int width, int height, int fps, int bitrate, bool includeAudio)
        {
            Path = path;
            _width = Math.Max(2, width & ~1);
            _height = Math.Max(2, height & ~1);
            _fps = Math.Clamp(fps, 15, 60);

            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RunOnMta(() => OpenWithFallback(includeAudio, bitrate));
        }

        private static void RunOnMta(Action action)
        {
            Exception? failure = null;
            using var completed = new ManualResetEventSlim(false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    MfRuntime.EnsureStarted();
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    completed.Set();
                }
            });
            completed.Wait();
            if (failure != null)
            {
                throw new InvalidOperationException(failure.Message, failure);
            }
        }

        private void OpenWithFallback(bool includeAudio, int bitrate)
        {
            try
            {
                Open(includeAudio, useHardware: true, bitrate);
            }
            catch (Exception first)
            {
                LastError = first.Message;
                CloseWriter();
                TryDelete(Path);
                try
                {
                    Open(includeAudio, useHardware: false, bitrate);
                }
                catch (Exception second)
                {
                    LastError = second.Message;
                    CloseWriter();
                    TryDelete(Path);
                    if (!includeAudio)
                    {
                        throw;
                    }

                    // Audio is optional. A failed AAC stream must not leave the
                    // writer with a stream that has no input type.
                    try
                    {
                        Open(includeAudio: false, useHardware: false, bitrate);
                    }
                    catch
                    {
                        LastError = second.Message;
                        CloseWriter();
                        TryDelete(Path);
                        throw;
                    }
                }
            }
        }

        private void Open(bool includeAudio, bool useHardware, int bitrate)
        {
            _audioEnabled = false;
            _began = false;
            _finalized = false;
            _lastVideoTime = -1;
            _lastAudioTime = -1;
            IMFAttributes? attributes = null;
            try
            {
                if (useHardware)
                {
                    int hr = MfApi.MFCreateAttributes(out attributes, 1);
                    if (hr >= 0 && attributes != null)
                    {
                        attributes.SetUInt32(MfGuids.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                    }
                }

                int result = MfApi.MFCreateSinkWriterFromURL(Path, IntPtr.Zero, attributes, out var writer);
                Marshal.ThrowExceptionForHR(result);
                _writer = writer;

                var outputVideo = CreateVideoType(bitrate);
                result = _writer.AddStream(outputVideo, out _videoStream);
                Marshal.ThrowExceptionForHR(result);
                var inputVideo = CreateVideoInputType();
                result = _writer.SetInputMediaType(_videoStream, inputVideo, null);
                Marshal.ThrowExceptionForHR(result);
                ReleaseCom(outputVideo);
                ReleaseCom(inputVideo);

                if (includeAudio)
                {
                    var aac = new AacMftEncoder();
                    try
                    {
                        var outputAudio = aac.OutputType;
                        result = _writer.AddStream(outputAudio, out _audioStream);
                        if (result < 0)
                        {
                            throw new InvalidOperationException(
                                $"AAC output type was rejected by the MP4 sink (0x{result:X8}).");
                        }
                        result = _writer.SetInputMediaType(_audioStream, outputAudio, null);
                        if (result < 0)
                        {
                            throw new InvalidOperationException(
                                $"AAC input type was rejected by the MP4 sink (0x{result:X8}).");
                        }
                    }
                    catch
                    {
                        aac.Dispose();
                        throw;
                    }

                    _aac = aac;
                    _audioEnabled = true;
                }

                result = _writer.BeginWriting();
                Marshal.ThrowExceptionForHR(result);
                _began = true;
            }
            finally
            {
                if (attributes != null)
                {
                    ReleaseCom(attributes);
                }
            }
        }

        private IMFMediaType CreateVideoType(int bitrate)
        {
            int result = MfApi.MFCreateMediaType(out var type);
            Marshal.ThrowExceptionForHR(result);
            type.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video);
            type.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_H264);
            type.SetUInt32(MfGuids.MF_MT_AVG_BITRATE, (uint)Math.Max(1_000_000, bitrate));
            type.SetUInt32(MfGuids.MF_MT_INTERLACE_MODE, 2);
            type.SetUInt64(MfGuids.MF_MT_FRAME_SIZE, ((ulong)(uint)_width << 32) | (uint)_height);
            type.SetUInt64(MfGuids.MF_MT_FRAME_RATE, ((ulong)(uint)_fps << 32) | 1);
            type.SetUInt64(MfGuids.MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);
            type.SetUInt32(MfGuids.MF_MT_MAX_KEYFRAME_SPACING, (uint)(_fps * 2));
            return type;
        }

        private IMFMediaType CreateVideoInputType()
        {
            int result = MfApi.MFCreateMediaType(out var type);
            Marshal.ThrowExceptionForHR(result);
            type.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video);
            type.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_RGB32);
            type.SetUInt32(MfGuids.MF_MT_INTERLACE_MODE, 2);
            type.SetUInt64(MfGuids.MF_MT_FRAME_SIZE, ((ulong)(uint)_width << 32) | (uint)_height);
            type.SetUInt64(MfGuids.MF_MT_FRAME_RATE, ((ulong)(uint)_fps << 32) | 1);
            type.SetUInt64(MfGuids.MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);
            return type;
        }

        public void WriteVideo(byte[] bgra, long timeTicks)
        {
            if (bgra == null || bgra.Length < _width * _height * 4)
            {
                return;
            }

            lock (_gate)
            {
                if (_writer == null || _finalized)
                {
                    return;
                }

                long time = Math.Max(0, timeTicks);
                if (_lastVideoTime >= 0 && time <= _lastVideoTime)
                {
                    time = _lastVideoTime + (10_000_000L / _fps);
                }

                long duration = Math.Max(1, 10_000_000L / _fps);
                int stride = _width * 4;
                int bufferSize = stride * _height;
                int result = MfApi.MFCreateMemoryBuffer((uint)bufferSize, out var buffer);
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    result = buffer.Lock(out IntPtr pointer, out _, out _);
                    Marshal.ThrowExceptionForHR(result);
                    try
                    {
                        for (int y = 0; y < _height; y++)
                        {
                            int sourceRow = (_height - 1 - y) * stride;
                            Marshal.Copy(bgra, sourceRow, pointer + y * stride, stride);
                        }
                    }
                    finally
                    {
                        buffer.Unlock();
                    }

                    result = buffer.SetCurrentLength((uint)bufferSize);
                    Marshal.ThrowExceptionForHR(result);
                    result = MfApi.MFCreateSample(out var sample);
                    Marshal.ThrowExceptionForHR(result);
                    try
                    {
                        sample.AddBuffer(buffer);
                        sample.SetSampleTime(time);
                        sample.SetSampleDuration(duration);
                        result = _writer.WriteSample(_videoStream, sample);
                        Marshal.ThrowExceptionForHR(result);
                    }
                    finally
                    {
                        ReleaseCom(sample);
                    }
                }
                finally
                {
                    ReleaseCom(buffer);
                }

                _lastVideoTime = time;
                FramesWritten++;
            }
        }

        public void WriteAudio(short[] samples, long timeTicks)
        {
            if (!_audioEnabled || _aac == null || samples == null || samples.Length == 0)
            {
                return;
            }

            lock (_gate)
            {
                if (_writer == null || _finalized)
                {
                    return;
                }

                long time = Math.Max(0, timeTicks);
                if (_lastAudioTime >= 0 && time < _lastAudioTime)
                {
                    time = _lastAudioTime;
                }

                int frames = samples.Length / AudioChannels;
                if (frames <= 0)
                {
                    return;
                }

                _lastAudioTime = time + (long)frames * 10_000_000L / AudioSampleRate;
                _aac.Encode(samples, WriteCompressedAudio);
            }
        }

        private void WriteCompressedAudio(IMFSample sample)
        {
            if (_writer == null)
            {
                return;
            }

            int result = _writer.WriteSample(_audioStream, sample);
            if (result < 0)
            {
                throw new InvalidOperationException($"AAC sample was rejected by the MP4 sink (0x{result:X8}).");
            }
        }

        public void Finish()
        {
            RunOnMta(FinishCore);
        }

        private void FinishCore()
        {
            lock (_gate)
            {
                if (_writer == null || _finalized)
                {
                    return;
                }

                try
                {
                    if (_began)
                    {
                        FillSilentAudio();
                        _aac?.Drain(WriteCompressedAudio);
                        int result = _writer.DoFinalize();
                        if (result >= 0)
                        {
                            _finalized = true;
                        }
                        else
                        {
                            string message = $"MP4 finalization failed (0x{result:X8}).";
                            LastError = string.IsNullOrEmpty(LastError) ? message : LastError + " | " + message;
                        }
                    }
                }
                finally
                {
                    CloseWriter();
                }
            }
        }

        private void FillSilentAudio()
        {
            if (!_audioEnabled || _lastVideoTime < 0)
            {
                return;
            }

            long position = _lastAudioTime < 0 ? 0 : _lastAudioTime;
            while (position < _lastVideoTime)
            {
                int frames = (int)Math.Min(
                    4_800L,
                    Math.Max(1L, (_lastVideoTime - position) * AudioSampleRate / 10_000_000L));
                var silence = new short[frames * AudioChannels];
                WriteAudio(silence, position);
                long next = position + (long)frames * 10_000_000L / AudioSampleRate;
                if (next <= position)
                {
                    break;
                }
                position = next;
            }
        }

        private void CloseWriter()
        {
            if (_writer == null)
            {
                return;
            }

            try
            {
                _writer.Flush(0);
            }
            catch
            {
            }

            ReleaseCom(_writer);
            _writer = null;
            _aac?.Dispose();
            _aac = null;
            _began = false;
        }

        public bool IsFinalized => _finalized;

        public void Dispose()
        {
            Finish();
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

        private static void ReleaseCom(object? value)
        {
            if (value != null)
            {
                try
                {
                    Marshal.ReleaseComObject(value);
                }
                catch
                {
                }
            }
        }
    }
}
