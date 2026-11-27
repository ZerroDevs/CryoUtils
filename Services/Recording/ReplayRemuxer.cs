using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CryoUtils.Services.Recording
{
    internal static class ReplayRemuxer
    {
        /// <summary>
        /// Copies compressed H.264/AAC samples from segment files into one MP4.
        /// No video is decoded and no encoder is started.
        /// </summary>
        public static void Remux(
            IReadOnlyList<ReplaySegmentInfo> segments,
            string outputPath)
        {
            if (segments.Count == 0)
            {
                throw new InvalidOperationException("The replay buffer is empty.");
            }

            MfRuntime.EnsureStarted();
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            IMFSinkWriter? writer = null;
            IMFSourceReader? firstReader = null;
            string stage = "open source";
            try
            {
                firstReader = OpenReader(segments[0].Path);
                stage = "read video type";
                IMFMediaType videoType = GetNativeType(
                    firstReader,
                    MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
                IMFMediaType? audioType = null;
                if (segments[0].HasAudio)
                {
                    try
                    {
                        audioType = GetNativeType(
                            firstReader,
                            MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM);
                    }
                    catch
                    {
                        audioType = null;
                    }
                }

                stage = "create writer";
                // The per-segment readers below reopen these files.
                Release(firstReader);
                firstReader = null;
                var videoOutput = Clone(videoType);
                int hr = MfApi.MFCreateSinkWriterFromURL(
                    outputPath,
                    IntPtr.Zero,
                    null,
                    out writer);
                Marshal.ThrowExceptionForHR(hr);
                stage = "add video stream";
                hr = writer.AddStream(videoOutput, out uint videoWriterStream);
                Marshal.ThrowExceptionForHR(hr);
                Release(videoOutput);
                stage = "set video input";
                var videoInput = Clone(videoType);
                hr = writer.SetInputMediaType(videoWriterStream, videoInput, null);
                Marshal.ThrowExceptionForHR(hr);
                Release(videoInput);

                uint audioWriterStream = 0;
                bool audio = audioType != null;
                if (audioType != null)
                {
                    try
                    {
                        var audioOutput = Clone(audioType);
                        hr = writer.AddStream(audioOutput, out audioWriterStream);
                        Marshal.ThrowExceptionForHR(hr);
                        Release(audioOutput);
                        var audioInput = Clone(audioType);
                        hr = writer.SetInputMediaType(audioWriterStream, audioInput, null);
                        Marshal.ThrowExceptionForHR(hr);
                        Release(audioInput);
                    }
                    catch
                    {
                        audio = false;
                    }
                }

                stage = "begin writing";
                hr = writer.BeginWriting();
                Marshal.ThrowExceptionForHR(hr);

                long origin = segments[0].StartTicks;
                long[] lastTimes = { -1, -1 };
                stage = "copy segments";
                foreach (var segment in segments)
                {
                    using var reader = new ReaderScope(segment.Path);
                    ConfigureReader(reader.Reader, audio);
                    var streams = FindStreamIndices(reader.Reader);
                    long offset = Math.Max(0, segment.StartTicks - origin);
                    CopySegment(
                        reader.Reader,
                        writer,
                        videoWriterStream,
                        audioWriterStream,
                        streams.Video,
                        streams.Audio,
                        audio,
                        offset,
                        lastTimes);
                }

                stage = "finalize";
                hr = writer.DoFinalize();
                if (hr < 0)
                {
                    throw new InvalidOperationException($"Replay finalize failed (0x{hr:X8}).");
                }
            }
            catch (Exception ex)
            {
                TryDelete(outputPath);
                throw new InvalidOperationException($"Replay remux failed at {stage}: {ex.Message}", ex);
            }
            finally
            {
                Release(writer);
                Release(firstReader);
            }
        }

        private static void CopySegment(
            IMFSourceReader reader,
            IMFSinkWriter writer,
            uint videoWriterStream,
            uint audioWriterStream,
            uint videoSourceStream,
            uint audioSourceStream,
            bool audio,
            long offset,
            long[] lastTimes)
        {
            while (true)
            {
                int hr = reader.ReadSample(
                    unchecked((uint)MfGuids.MF_SOURCE_READER_ANY_STREAM),
                    0,
                    out uint actualStream,
                    out uint flags,
                    out long timestamp,
                    out var sample);
                if (hr < 0)
                {
                    throw new InvalidOperationException($"Replay read failed (0x{hr:X8}).");
                }

                if ((flags & MfGuids.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    break;
                }

                if (sample == null)
                {
                    continue;
                }

                try
                {
                    bool isVideo = actualStream == videoSourceStream;
                    bool isAudio = audio && actualStream == audioSourceStream;
                    if (!isVideo && !isAudio)
                    {
                        continue;
                    }

                    uint outputStream = isVideo ? videoWriterStream : audioWriterStream;
                    int index = isVideo ? 0 : 1;
                    long time = offset + Math.Max(0, timestamp);
                    if (lastTimes[index] >= time)
                    {
                        time = lastTimes[index] + 1;
                    }

                    sample.SetSampleTime(time);
                    hr = writer.WriteSample(outputStream, sample);
                    if (hr < 0)
                    {
                        throw new InvalidOperationException(
                            $"Replay write failed for stream {actualStream} at {timestamp} (0x{hr:X8}).");
                    }
                    lastTimes[index] = time;
                }
                finally
                {
                    Release(sample);
                }
            }
        }

        private static (uint Video, uint Audio) FindStreamIndices(IMFSourceReader reader)
        {
            uint video = uint.MaxValue;
            uint audio = uint.MaxValue;
            for (uint index = 0; index < 16; index++)
            {
                int hr = reader.GetNativeMediaType(index, 0, out var type);
                if (hr < 0)
                {
                    break;
                }

                try
                {
                    type.GetGUID(MfGuids.MF_MT_MAJOR_TYPE, out Guid major);
                    if (major == MfGuids.MFMediaType_Video && video == uint.MaxValue)
                    {
                        video = index;
                    }
                    else if (major == MfGuids.MFMediaType_Audio && audio == uint.MaxValue)
                    {
                        audio = index;
                    }
                }
                finally
                {
                    Release(type);
                }
            }

            return (video, audio);
        }

        private static void ConfigureReader(IMFSourceReader reader, bool audio)
        {
            int hr = reader.SetStreamSelection(unchecked((uint)MfGuids.MF_SOURCE_READER_ALL_STREAMS), false);
            Marshal.ThrowExceptionForHR(hr);
            hr = reader.SetStreamSelection(
                unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                true);
            Marshal.ThrowExceptionForHR(hr);
            if (audio)
            {
                reader.SetStreamSelection(
                    unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                    true);
            }

            var videoType = GetNativeType(
                reader,
                MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            hr = reader.SetCurrentMediaType(
                unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                IntPtr.Zero,
                videoType);
            Marshal.ThrowExceptionForHR(hr);
            Release(videoType);

            if (audio)
            {
                try
                {
                    var audioType = GetNativeType(
                        reader,
                        MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM);
                    reader.SetCurrentMediaType(
                        unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                        IntPtr.Zero,
                        audioType);
                    Release(audioType);
                }
                catch
                {
                }
            }
        }

        private static IMFSourceReader OpenReader(string path)
        {
            int hr = MfApi.MFCreateSourceReaderFromURL(path, null, out var reader);
            Marshal.ThrowExceptionForHR(hr);
            return reader;
        }

        private static IMFMediaType GetNativeType(IMFSourceReader reader, int stream)
        {
            int hr = reader.GetNativeMediaType(unchecked((uint)stream), 0, out var type);
            Marshal.ThrowExceptionForHR(hr);
            return type;
        }

        private static IMFMediaType Clone(IMFMediaType source)
        {
            int hr = MfApi.MFCreateMediaType(out var copy);
            Marshal.ThrowExceptionForHR(hr);
            hr = source.CopyAllItems(copy);
            Marshal.ThrowExceptionForHR(hr);
            return copy;
        }

        /// <summary>
        /// Re-encoding fallback for a rare compressed-pass-through failure.
        /// </summary>
        public static Task ReencodeAsync(
            IReadOnlyList<ReplaySegmentInfo> segments,
            string outputPath)
        {
            var paths = new List<string>(segments.Count);
            foreach (var segment in segments)
            {
                paths.Add(Path.GetFullPath(segment.Path));
            }
            return Task.Run(() => MediaConversionService.MergeSegmentsReencode(paths, outputPath));
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

        private static void Release(object? value)
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

        private sealed class ReaderScope : IDisposable
        {
            public IMFSourceReader Reader { get; }

            public ReaderScope(string path)
            {
                Reader = OpenReader(path);
            }

            public void Dispose() => Release(Reader);
        }
    }
}
