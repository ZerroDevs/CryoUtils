using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Services.Recording;

namespace CryoUtils.Services
{
    /// <summary>
    /// Native Media Foundation media conversion, trimming, thumbnail, and GIF pipeline,
    /// replacing Microsoft.Windows.SDK.NET MediaComposition / MediaTranscoder.
    /// </summary>
    public static class MediaConversionService
    {
        private const int MaxGifFrames = 240;

        public static Task VideoToAudioAsync(string videoPath, string outputPath) =>
            ConvertAudioAsync(videoPath, outputPath, "MP3");

        public static Task ConvertAudioAsync(string inputPath, string outputPath, string targetFormat)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("Media file not found", inputPath);
            }

            string fmt = targetFormat.ToUpperInvariant();
            return Task.Run(() =>
            {
                MfRuntime.EnsureStarted();
                EnsureParentDir(outputPath);

                int hr = MfApi.MFCreateSourceReaderFromURL(Path.GetFullPath(inputPath), null, out var reader);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    ConfigureAudioPcmReader(reader, out uint sampleRate, out uint channels);
                    if (fmt == "WAV")
                    {
                        WriteWaveFromReader(reader, outputPath, sampleRate, channels);
                        return;
                    }

                    Guid targetSubType = fmt == "M4A" ? MfGuids.MFAudioFormat_AAC : MfGuids.MFAudioFormat_MP3;
                    WriteEncodedAudioFromReader(reader, outputPath, targetSubType, sampleRate, channels);
                }
                finally
                {
                    Marshal.ReleaseComObject(reader);
                }
            });
        }

        public static Task<long> CompressVideoForDiscordAsync(
            string videoPath,
            string outputPath,
            long targetMaxBytes = 9_500_000)
        {
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("Video file not found", videoPath);
            }

            return Task.Run(() =>
            {
                MfRuntime.EnsureStarted();
                EnsureParentDir(outputPath);
                long originalBytes = new FileInfo(videoPath).Length;

                using var _ = new ComReleaser();
                var reader = _.Track(OpenTranscodeReader(videoPath));
                double durationSec = Math.Max(1.0, ReadDurationSeconds(reader));
                uint audioBitrate = 128_000;
                double totalBitsPerSec = (targetMaxBytes * 8.0) / durationSec;
                uint targetVideoBitrate = (uint)Math.Clamp(totalBitsPerSec - audioBitrate, 350_000, 6_000_000);
                TranscodeRange(
                    reader,
                    outputPath,
                    start100ns: 0,
                    end100ns: long.MaxValue,
                    maxWidth: 1280,
                    maxHeight: 720,
                    videoBitrate: targetVideoBitrate,
                    includeAudio: true);

                long compressedBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : originalBytes;
                return originalBytes - compressedBytes;
            });
        }

        public static Task MuteVideoAsync(string videoPath, string outputPath)
        {
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("Video file not found", videoPath);
            }

            return Task.Run(() =>
            {
                MfRuntime.EnsureStarted();
                EnsureParentDir(outputPath);

                using var _ = new ComReleaser();
                var reader = _.Track(OpenTranscodeReader(videoPath));
                TranscodeRange(
                    reader,
                    outputPath,
                    start100ns: 0,
                    end100ns: long.MaxValue,
                    maxWidth: int.MaxValue,
                    maxHeight: int.MaxValue,
                    videoBitrate: 0,
                    includeAudio: false);
            });
        }

        public static Task SaveThumbnailAsync(string videoPath, string outputPath)
        {
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("Video file not found", videoPath);
            }

            EnsureDifferentVideoOutput(videoPath, outputPath);
            return Task.Run(() =>
            {
                MfRuntime.EnsureStarted();
                EnsureParentDir(outputPath);

                var reader = OpenRgbVideoReader(videoPath, out uint vidStream, out int width, out int height, out int stride, out double durationSec, out bool isNv12);
                try
                {
                    long seek100ns = (long)(Math.Min(2.0, Math.Max(0, durationSec * 0.25)) * 10_000_000.0);
                    var frame = ReadRgbFrameAt(reader, vidStream, seek100ns, width, height, stride, isNv12);
                    if (frame == null && seek100ns > 0)
                    {
                        frame = ReadRgbFrameAt(reader, vidStream, 0, width, height, stride, isNv12);
                    }
                    if (frame == null)
                    {
                        throw new InvalidOperationException("Could not capture video frame.");
                    }

                    ImageIO.SaveBitmap(frame, outputPath);
                }
                finally
                {
                    Marshal.ReleaseComObject(reader);
                }
            });
        }

        public static Task TrimVideoAsync(
            string videoPath,
            string outputPath,
            double startSeconds,
            double endSeconds)
        {
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("Video file not found", videoPath);
            }

            EnsureDifferentVideoOutput(videoPath, outputPath);
            return Task.Run(() =>
            {
                MfRuntime.EnsureStarted();
                EnsureParentDir(outputPath);

                using var _ = new ComReleaser();
                var reader = _.Track(OpenTranscodeReader(videoPath));
                double duration = ReadDurationSeconds(reader);
                if (duration <= 0.1)
                {
                    duration = Math.Max(1.0, endSeconds > 0 ? endSeconds : 1.0);
                }

                double start = Math.Clamp(startSeconds, 0, Math.Max(0, duration - 0.1));
                double end = endSeconds <= 0 ? duration : Math.Clamp(endSeconds, start + 0.1, duration);
                if (end <= start)
                {
                    throw new ArgumentOutOfRangeException(nameof(endSeconds), "The end time must be after the start time.");
                }

                TranscodeRange(
                    reader,
                    outputPath,
                    start100ns: (long)(start * 10_000_000.0),
                    end100ns: (long)(end * 10_000_000.0),
                    maxWidth: int.MaxValue,
                    maxHeight: int.MaxValue,
                    videoBitrate: 0,
                    includeAudio: true);
            });
        }

        public static Task<int> VideoToGifAsync(
            string videoPath,
            string outputPath,
            int fps = 12,
            int maxWidth = 640,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default,
            double startSeconds = 0,
            double endSeconds = 0)
        {
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("Video file not found", videoPath);
            }

            EnsureDifferentVideoOutput(videoPath, outputPath);
            return Task.Run(() =>
            {
                MfRuntime.EnsureStarted();
                EnsureParentDir(outputPath);

                var reader = OpenRgbVideoReader(videoPath, out uint vidStream, out int width, out int height, out int stride, out double duration, out bool isNv12);
                try
                {
                    if (duration <= 0)
                    {
                        duration = endSeconds > 0 ? endSeconds : 10.0;
                    }

                    double start = Math.Clamp(startSeconds, 0, Math.Max(0, duration - 0.05));
                    double end = endSeconds <= 0 ? duration : Math.Clamp(endSeconds, start + 0.05, duration);
                    if (end - start > 60)
                    {
                        end = start + 60;
                    }

                    int actualFps = Math.Clamp(fps, 5, 30);
                    double stepSeconds = 1.0 / actualFps;
                    double limitedSeconds = Math.Min(end - start, MaxGifFrames / (double)actualFps);
                    var frames = new List<BitmapSource>();
                    progress?.Report(0);

                    for (double t = 0; t < limitedSeconds && frames.Count < MaxGifFrames; t += stepSeconds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (limitedSeconds > 0)
                        {
                            progress?.Report((int)Math.Round(t / limitedSeconds * 100));
                        }

                        long target100ns = (long)((start + t) * 10_000_000.0);
                        var frame = ReadRgbFrameAt(reader, vidStream, target100ns, width, height, stride, isNv12);
                        if (frame != null)
                        {
                            frames.Add(FitGifFrame(frame, maxWidth));
                        }
                    }

                    if (frames.Count == 0)
                    {
                        return 0;
                    }

                    progress?.Report(100);
                    using var fs = File.Create(outputPath);
                    using var writer = new GifWriter(fs, frames[0].PixelWidth, frames[0].PixelHeight, actualFps, frames);
                    foreach (var f in frames)
                    {
                        writer.AddFrame(f);
                    }
                    writer.Finish();
                    return frames.Count;
                }
                finally
                {
                    Marshal.ReleaseComObject(reader);
                }
            }, cancellationToken);
        }

        internal static void MergeSegmentsReencode(IReadOnlyList<string> segmentPaths, string outputPath)
        {
            if (segmentPaths.Count == 0)
            {
                throw new InvalidOperationException("No replay segments to encode.");
            }

            MfRuntime.EnsureStarted();
            EnsureParentDir(outputPath);

            IMFSinkWriter? writer = null;
            AacMftEncoder? aac = null;
            uint outVideoStream = 0;
            uint outAudioStream = 0;
            bool hasAudioOut = false;
            long[] streamOffset = { 0, 0 };

            try
            {
                for (int s = 0; s < segmentPaths.Count; s++)
                {
                    using var _ = new ComReleaser();
                    var reader = _.Track(OpenTranscodeReader(segmentPaths[s]));
                    ConfigureTranscodeReader(
                        reader,
                        out uint inVid, out Guid vidSubtype, out uint width, out uint height, out uint fpsNum, out uint fpsDen, out uint bitrate,
                        out uint inAud, out bool segHasAudio, out uint sampleRate, out uint channels);

                    if (s == 0)
                    {
                        writer = CreateMp4SinkWriterWithFallback(
                            outputPath,
                            vidSubtype, width, height, fpsNum, fpsDen, bitrate > 0 ? bitrate : 6_000_000u,
                            segHasAudio,
                            out outVideoStream, out outAudioStream, out aac, out hasAudioOut);
                        Marshal.ThrowExceptionForHR(writer.BeginWriting());
                    }

                    long maxVid = 0;
                    long maxAud = 0;
                    PumpTranscodeSamples(
                        reader, writer!, aac, inVid, inAud, outVideoStream, outAudioStream, hasAudioOut && segHasAudio,
                        sampleRate, channels,
                        start100ns: 0, end100ns: long.MaxValue,
                        videoOffset: streamOffset[0],
                        ref maxVid, ref maxAud);
                    streamOffset[0] = Math.Max(streamOffset[0] + 1, maxVid + 333_333);
                    streamOffset[1] = Math.Max(streamOffset[1] + 1, maxAud + 213_333);
                }

                aac?.Drain(sample => Marshal.ThrowExceptionForHR(writer!.WriteSample(outAudioStream, sample)));
                Marshal.ThrowExceptionForHR(writer!.DoFinalize());
            }
            finally
            {
                aac?.Dispose();
                if (writer != null) Marshal.ReleaseComObject(writer);
            }
        }

        private static void ConfigureAudioPcmReader(IMFSourceReader reader, out uint sampleRate, out uint channels)
        {
            reader.SetStreamSelection(unchecked((uint)MfGuids.MF_SOURCE_READER_ALL_STREAMS), false);
            int hr = reader.SetStreamSelection(unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM), true);
            Marshal.ThrowExceptionForHR(hr);

            hr = reader.GetNativeMediaType(unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, out var nativeType);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                Guid srKey = MfGuids.MF_MT_AUDIO_SAMPLES_PER_SECOND;
                Guid chKey = MfGuids.MF_MT_AUDIO_NUM_CHANNELS;
                sampleRate = nativeType.GetUINT32(ref srKey, out uint r) >= 0 && r > 0 ? r : 48_000u;
                channels = nativeType.GetUINT32(ref chKey, out uint c) >= 0 && c > 0 ? Math.Min(c, 2u) : 2u;
            }
            finally
            {
                Marshal.ReleaseComObject(nativeType);
            }

            hr = MfApi.MFCreateMediaType(out var pcmType);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                pcmType.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Audio);
                pcmType.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFAudioFormat_PCM);
                pcmType.SetUInt32(MfGuids.MF_MT_AUDIO_NUM_CHANNELS, channels);
                pcmType.SetUInt32(MfGuids.MF_MT_AUDIO_SAMPLES_PER_SECOND, sampleRate);
                pcmType.SetUInt32(MfGuids.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                uint blockAlign = channels * 2;
                pcmType.SetUInt32(MfGuids.MF_MT_AUDIO_BLOCK_ALIGNMENT, blockAlign);
                pcmType.SetUInt32(MfGuids.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, sampleRate * blockAlign);
                pcmType.SetUInt32(MfGuids.MF_MT_ALL_SAMPLES_INDEPENDENT, 1);

                hr = reader.SetCurrentMediaType(
                    unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                    IntPtr.Zero,
                    pcmType);
                Marshal.ThrowExceptionForHR(hr);
            }
            finally
            {
                Marshal.ReleaseComObject(pcmType);
            }
        }

        private static void WriteWaveFromReader(IMFSourceReader reader, string outputPath, uint sampleRate, uint channels)
        {
            using var fs = File.Create(outputPath);
            using var bw = new BinaryWriter(fs);
            bw.Write(new byte[44]); // Reserve RIFF header

            long dataBytes = 0;
            while (true)
            {
                int hr = reader.ReadSample(unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, out _, out uint flags, out _, out var sample);
                if (hr < 0 || (flags & MfGuids.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                if (sample == null) continue;
                try
                {
                    sample.ConvertToContiguousBuffer(out var buf);
                    try
                    {
                        buf.Lock(out IntPtr ptr, out _, out uint len);
                        try
                        {
                            if (len > 0)
                            {
                                byte[] bytes = new byte[len];
                                Marshal.Copy(ptr, bytes, 0, (int)len);
                                bw.Write(bytes);
                                dataBytes += len;
                            }
                        }
                        finally { buf.Unlock(); }
                    }
                    finally { Marshal.ReleaseComObject(buf); }
                }
                finally { Marshal.ReleaseComObject(sample); }
            }

            fs.Seek(0, SeekOrigin.Begin);
            uint blockAlign = channels * 2;
            uint byteRate = sampleRate * blockAlign;
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write((uint)(36 + dataBytes));
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16u);
            bw.Write((ushort)1); // PCM
            bw.Write((ushort)channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write((ushort)blockAlign);
            bw.Write((ushort)16);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write((uint)dataBytes);
        }

        private static void WriteEncodedAudioFromReader(
            IMFSourceReader reader,
            string outputPath,
            Guid targetSubType,
            uint sampleRate,
            uint channels)
        {
            if (targetSubType == MfGuids.MFAudioFormat_AAC)
            {
                using var aac = new AacMftEncoder();
                int hrAac = MfApi.MFCreateSinkWriterFromURL(Path.GetFullPath(outputPath), IntPtr.Zero, null, out var aacWriter);
                Marshal.ThrowExceptionForHR(hrAac);
                try
                {
                    Marshal.ThrowExceptionForHR(aacWriter.AddStream(aac.OutputType, out uint aacStream));
                    Marshal.ThrowExceptionForHR(aacWriter.SetInputMediaType(aacStream, aac.OutputType, null));
                    Marshal.ThrowExceptionForHR(aacWriter.BeginWriting());
                    while (true)
                    {
                        int hrRead = reader.ReadSample(unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, out _, out uint flags, out _, out var sample);
                        if (hrRead < 0 || (flags & MfGuids.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                        if (sample == null) continue;
                        try
                        {
                            short[] pcm = ExtractPcm48kStereo(sample, sampleRate, channels);
                            if (pcm.Length > 0)
                            {
                                aac.Encode(pcm, encoded => Marshal.ThrowExceptionForHR(aacWriter.WriteSample(aacStream, encoded)));
                            }
                        }
                        finally { Marshal.ReleaseComObject(sample); }
                    }
                    aac.Drain(encoded => Marshal.ThrowExceptionForHR(aacWriter.WriteSample(aacStream, encoded)));
                    Marshal.ThrowExceptionForHR(aacWriter.DoFinalize());
                }
                finally { Marshal.ReleaseComObject(aacWriter); }
                return;
            }

            int hr = MfApi.MFCreateSinkWriterFromURL(Path.GetFullPath(outputPath), IntPtr.Zero, null, out var writer);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                MfApi.MFCreateMediaType(out var outType);
                outType.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Audio);
                outType.SetGuid(MfGuids.MF_MT_SUBTYPE, targetSubType);
                outType.SetUInt32(MfGuids.MF_MT_AUDIO_NUM_CHANNELS, channels);
                outType.SetUInt32(MfGuids.MF_MT_AUDIO_SAMPLES_PER_SECOND, sampleRate);
                outType.SetUInt32(MfGuids.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                outType.SetUInt32(MfGuids.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 24_000); // 192 kbps MP3

                Marshal.ThrowExceptionForHR(writer.AddStream(outType, out uint streamIndex));
                Marshal.ReleaseComObject(outType);

                reader.GetCurrentMediaType(unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM), out var inType);
                try
                {
                    Marshal.ThrowExceptionForHR(writer.SetInputMediaType(streamIndex, inType, null));
                }
                finally { Marshal.ReleaseComObject(inType); }

                Marshal.ThrowExceptionForHR(writer.BeginWriting());
                while (true)
                {
                    hr = reader.ReadSample(unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, out _, out uint flags, out _, out var sample);
                    if (hr < 0 || (flags & MfGuids.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                    if (sample == null) continue;
                    try { Marshal.ThrowExceptionForHR(writer.WriteSample(streamIndex, sample)); }
                    finally { Marshal.ReleaseComObject(sample); }
                }
                Marshal.ThrowExceptionForHR(writer.DoFinalize());
            }
            finally { Marshal.ReleaseComObject(writer); }
        }

        private static IMFSourceReader OpenRgbVideoReader(
            string videoPath,
            out uint vidStream,
            out int width,
            out int height,
            out int stride,
            out double durationSec,
            out bool isNv12)
        {
            int hr = MfApi.MFCreateAttributes(out var attrs, 1);
            Marshal.ThrowExceptionForHR(hr);
            attrs.SetUInt32(MfGuids.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            hr = MfApi.MFCreateSourceReaderFromURL(Path.GetFullPath(videoPath), attrs, out var reader);
            Marshal.ReleaseComObject(attrs);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                durationSec = ReadDurationSeconds(reader);
                vidStream = unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
                for (uint idx = 0; idx < 8; idx++)
                {
                    if (reader.GetNativeMediaType(idx, 0, out var nativeMt) < 0) break;
                    try
                    {
                        Guid majorKey = MfGuids.MF_MT_MAJOR_TYPE;
                        if (nativeMt.GetGUID(ref majorKey, out Guid major) >= 0 && major == MfGuids.MFMediaType_Video)
                        {
                            vidStream = idx;
                            break;
                        }
                    }
                    finally { Marshal.ReleaseComObject(nativeMt); }
                }

                reader.SetStreamSelection(unchecked((uint)MfGuids.MF_SOURCE_READER_ALL_STREAMS), false);
                Marshal.ThrowExceptionForHR(reader.SetStreamSelection(vidStream, true));

                isNv12 = false;
                MfApi.MFCreateMediaType(out var rgbType);
                try
                {
                    rgbType.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video);
                    rgbType.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_RGB32);
                    hr = reader.SetCurrentMediaType(vidStream, IntPtr.Zero, rgbType);
                    if (hr < 0)
                    {
                        rgbType.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_NV12);
                        Marshal.ThrowExceptionForHR(reader.SetCurrentMediaType(vidStream, IntPtr.Zero, rgbType));
                        isNv12 = true;
                    }
                }
                finally { Marshal.ReleaseComObject(rgbType); }

                Marshal.ThrowExceptionForHR(reader.GetCurrentMediaType(vidStream, out var current));
                try
                {
                    Guid fsKey = MfGuids.MF_MT_FRAME_SIZE;
                    current.GetUINT64(ref fsKey, out ulong packed);
                    width = Math.Max(1, (int)(packed >> 32));
                    height = Math.Max(1, (int)(packed & 0xFFFFFFFF));

                    Guid strideKey = MfGuids.MF_MT_DEFAULT_STRIDE;
                    int defaultMin = isNv12 ? width : width * 4;
                    stride = current.GetUINT32(ref strideKey, out uint rawStride) >= 0 && rawStride != 0
                        ? unchecked((int)rawStride)
                        : defaultMin;
                }
                finally { Marshal.ReleaseComObject(current); }

                return reader;
            }
            catch
            {
                Marshal.ReleaseComObject(reader);
                throw;
            }
        }

        private static BitmapSource? ReadRgbFrameAt(
            IMFSourceReader reader,
            uint vidStream,
            long target100ns,
            int width,
            int height,
            int defaultStride,
            bool isNv12)
        {
            SetPosition100ns(reader, target100ns);
            while (true)
            {
                int hr = reader.ReadSample(vidStream, 0, out _, out uint flags, out long timestamp, out var sample);
                if (hr < 0 || (flags & MfGuids.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    return null;
                }
                if (sample == null)
                {
                    continue;
                }

                try
                {
                    if (timestamp < target100ns - 2_000_000 && target100ns > 0)
                    {
                        continue;
                    }

                    sample.ConvertToContiguousBuffer(out var buf);
                    try
                    {
                        buf.Lock(out IntPtr ptr, out _, out uint len);
                        try
                        {
                            int dstStride = width * 4;
                            byte[] bgra = new byte[dstStride * height];

                            if (isNv12)
                            {
                                int rowAbs = Math.Max(width, Math.Abs(defaultStride));
                                int uvOffset = rowAbs * height;
                                if (len < (uint)(uvOffset + rowAbs * (height / 2))) return null;

                                byte[] nv12 = new byte[len];
                                Marshal.Copy(ptr, nv12, 0, (int)len);
                                for (int y = 0; y < height; y++)
                                {
                                    int yRow = y * rowAbs;
                                    int uvRow = uvOffset + (y >> 1) * rowAbs;
                                    for (int x = 0; x < width; x++)
                                    {
                                        int c = nv12[yRow + x] - 16;
                                        int uvCol = (x & ~1);
                                        int d = nv12[uvRow + uvCol] - 128;
                                        int e = nv12[uvRow + uvCol + 1] - 128;
                                        int r = Math.Clamp((298 * c + 409 * e + 128) >> 8, 0, 255);
                                        int g = Math.Clamp((298 * c - 100 * d - 208 * e + 128) >> 8, 0, 255);
                                        int b = Math.Clamp((298 * c + 516 * d + 128) >> 8, 0, 255);
                                        int dst = y * dstStride + x * 4;
                                        bgra[dst] = (byte)b;
                                        bgra[dst + 1] = (byte)g;
                                        bgra[dst + 2] = (byte)r;
                                        bgra[dst + 3] = 255;
                                    }
                                }
                            }
                            else
                            {
                                int rowAbs = Math.Abs(defaultStride);
                                if (rowAbs < dstStride) rowAbs = dstStride;
                                if (len < (uint)(rowAbs * height)) return null;

                                bool bottomUp = defaultStride < 0;
                                for (int y = 0; y < height; y++)
                                {
                                    int srcRow = bottomUp ? (height - 1 - y) : y;
                                    Marshal.Copy(IntPtr.Add(ptr, srcRow * rowAbs), bgra, y * dstStride, dstStride);
                                }
                            }

                            var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, bgra, dstStride);
                            bmp.Freeze();
                            return bmp;
                        }
                        finally { buf.Unlock(); }
                    }
                    finally { Marshal.ReleaseComObject(buf); }
                }
                finally { Marshal.ReleaseComObject(sample); }
            }
        }

        private static IMFSourceReader OpenTranscodeReader(string videoPath)
        {
            int hr = MfApi.MFCreateAttributes(out var attrs, 2);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                attrs.SetUInt32(MfGuids.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
                hr = MfApi.MFCreateSourceReaderFromURL(Path.GetFullPath(videoPath), attrs, out var reader);
                Marshal.ThrowExceptionForHR(hr);
                return reader;
            }
            finally
            {
                Marshal.ReleaseComObject(attrs);
            }
        }

        private static void TranscodeRange(
            IMFSourceReader reader,
            string outputPath,
            long start100ns,
            long end100ns,
            int maxWidth,
            int maxHeight,
            uint videoBitrate,
            bool includeAudio)
        {
            ConfigureTranscodeReader(
                reader,
                out uint inVid, out Guid vidSubtype, out uint srcW, out uint srcH, out uint fpsNum, out uint fpsDen, out uint srcBitrate,
                out uint inAud, out bool hasAudio, out uint sampleRate, out uint channels);

            uint outW = srcW;
            uint outH = srcH;
            if (outW > maxWidth || outH > maxHeight)
            {
                double scale = Math.Min(maxWidth / (double)outW, maxHeight / (double)outH);
                outW = Math.Max(16, (uint)(((int)(outW * scale) + 1) & ~1));
                outH = Math.Max(16, (uint)(((int)(outH * scale) + 1) & ~1));
            }

            // If scaling is requested, ask reader for RGB32 so the sink Video Processor MFT scales cleanly.
            if ((outW != srcW || outH != srcH) && vidSubtype != MfGuids.MFVideoFormat_RGB32)
            {
                MfApi.MFCreateMediaType(out var rgbVid);
                try
                {
                    rgbVid.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video);
                    rgbVid.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_RGB32);
                    if (reader.SetCurrentMediaType(inVid, IntPtr.Zero, rgbVid) >= 0)
                    {
                        vidSubtype = MfGuids.MFVideoFormat_RGB32;
                    }
                }
                finally { Marshal.ReleaseComObject(rgbVid); }
            }

            uint targetBitrate = videoBitrate > 0 ? videoBitrate : (srcBitrate > 0 ? srcBitrate : 6_000_000u);
            var writer = CreateMp4SinkWriterWithFallback(
                outputPath,
                vidSubtype, srcW, srcH, outW, outH, fpsNum, fpsDen, targetBitrate,
                includeAudio && hasAudio,
                out uint outVidStream, out uint outAudStream, out var aac, out bool emitAudio);

            try
            {
                Marshal.ThrowExceptionForHR(writer.BeginWriting());
                if (start100ns > 0)
                {
                    SetPosition100ns(reader, start100ns);
                }

                long maxV = 0;
                long maxA = 0;
                PumpTranscodeSamples(
                    reader, writer, aac, inVid, inAud, outVidStream, outAudStream, emitAudio,
                    sampleRate, channels,
                    start100ns, end100ns, -start100ns, ref maxV, ref maxA);
                aac?.Drain(sample => Marshal.ThrowExceptionForHR(writer.WriteSample(outAudStream, sample)));
                Marshal.ThrowExceptionForHR(writer.DoFinalize());
            }
            catch
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                throw;
            }
            finally
            {
                aac?.Dispose();
                Marshal.ReleaseComObject(writer);
            }
        }

        private static void ConfigureTranscodeReader(
            IMFSourceReader reader,
            out uint vidStream, out Guid videoSubtype, out uint width, out uint height, out uint fpsNum, out uint fpsDen, out uint bitrate,
            out uint audStream, out bool hasAudio, out uint sampleRate, out uint channels)
        {
            vidStream = unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
            audStream = unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM);
            videoSubtype = MfGuids.MFVideoFormat_NV12;
            width = 1280; height = 720; fpsNum = 30; fpsDen = 1; bitrate = 6_000_000;
            sampleRate = 48_000; channels = 2; hasAudio = false;

            reader.SetStreamSelection(unchecked((uint)MfGuids.MF_SOURCE_READER_ALL_STREAMS), false);
            Marshal.ThrowExceptionForHR(reader.SetStreamSelection(vidStream, true));

            Marshal.ThrowExceptionForHR(reader.GetNativeMediaType(vidStream, 0, out var nativeVid));
            try
            {
                Guid fsKey = MfGuids.MF_MT_FRAME_SIZE;
                Guid frKey = MfGuids.MF_MT_FRAME_RATE;
                Guid brKey = MfGuids.MF_MT_AVG_BITRATE;
                if (nativeVid.GetUINT64(ref fsKey, out ulong packedSize) >= 0)
                {
                    width = Math.Max(16, (uint)(packedSize >> 32));
                    height = Math.Max(16, (uint)(packedSize & 0xFFFFFFFF));
                }
                if (nativeVid.GetUINT64(ref frKey, out ulong packedRate) >= 0 && (packedRate & 0xFFFFFFFF) > 0)
                {
                    fpsNum = Math.Max(1, (uint)(packedRate >> 32));
                    fpsDen = Math.Max(1, (uint)(packedRate & 0xFFFFFFFF));
                }
                if (nativeVid.GetUINT32(ref brKey, out uint b) >= 0 && b > 0) bitrate = b;
            }
            finally { Marshal.ReleaseComObject(nativeVid); }

            // Request uncompressed NV12 (fast, top-down) or RGB32 fallback.
            MfApi.MFCreateMediaType(out var decVid);
            try
            {
                decVid.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video);
                decVid.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_NV12);
                int hr = reader.SetCurrentMediaType(vidStream, IntPtr.Zero, decVid);
                if (hr < 0)
                {
                    decVid.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_RGB32);
                    Marshal.ThrowExceptionForHR(reader.SetCurrentMediaType(vidStream, IntPtr.Zero, decVid));
                    videoSubtype = MfGuids.MFVideoFormat_RGB32;
                }
            }
            finally { Marshal.ReleaseComObject(decVid); }

            if (reader.SetStreamSelection(audStream, true) >= 0 &&
                reader.GetNativeMediaType(audStream, 0, out var nativeAud) >= 0)
            {
                try
                {
                    Guid srKey = MfGuids.MF_MT_AUDIO_SAMPLES_PER_SECOND;
                    Guid chKey = MfGuids.MF_MT_AUDIO_NUM_CHANNELS;
                    sampleRate = nativeAud.GetUINT32(ref srKey, out uint r) >= 0 && r > 0 ? r : 48_000u;
                    channels = nativeAud.GetUINT32(ref chKey, out uint c) >= 0 && c > 0 ? Math.Min(c, 2u) : 2u;

                    MfApi.MFCreateMediaType(out var pcm);
                    try
                    {
                        pcm.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Audio);
                        pcm.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFAudioFormat_PCM);
                        pcm.SetUInt32(MfGuids.MF_MT_AUDIO_NUM_CHANNELS, channels);
                        pcm.SetUInt32(MfGuids.MF_MT_AUDIO_SAMPLES_PER_SECOND, sampleRate);
                        pcm.SetUInt32(MfGuids.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                        pcm.SetUInt32(MfGuids.MF_MT_AUDIO_BLOCK_ALIGNMENT, channels * 2);
                        pcm.SetUInt32(MfGuids.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, sampleRate * channels * 2);
                        pcm.SetUInt32(MfGuids.MF_MT_ALL_SAMPLES_INDEPENDENT, 1);
                        hasAudio = reader.SetCurrentMediaType(audStream, IntPtr.Zero, pcm) >= 0;
                    }
                    finally { Marshal.ReleaseComObject(pcm); }
                }
                finally { Marshal.ReleaseComObject(nativeAud); }
            }

            // Resolve actual first stream indices when reading ANY_STREAM
            for (uint idx = 0; idx < 8; idx++)
            {
                if (reader.GetCurrentMediaType(idx, out var mt) < 0) break;
                try
                {
                    Guid majorKey = MfGuids.MF_MT_MAJOR_TYPE;
                    if (mt.GetGUID(ref majorKey, out Guid major) >= 0)
                    {
                        if (major == MfGuids.MFMediaType_Video) vidStream = idx;
                        else if (major == MfGuids.MFMediaType_Audio) audStream = idx;
                    }
                }
                finally { Marshal.ReleaseComObject(mt); }
            }
        }

        private static IMFSinkWriter CreateMp4SinkWriterWithFallback(
            string outputPath,
            Guid inVideoSubtype, uint width, uint height, uint fpsNum, uint fpsDen, uint videoBitrate,
            bool includeAudio,
            out uint videoStreamIndex, out uint audioStreamIndex, out AacMftEncoder? aac, out bool hasAudioOut) =>
            CreateMp4SinkWriterWithFallback(
                outputPath,
                inVideoSubtype, width, height, width, height, fpsNum, fpsDen, videoBitrate,
                includeAudio,
                out videoStreamIndex, out audioStreamIndex, out aac, out hasAudioOut);

        private static IMFSinkWriter CreateMp4SinkWriterWithFallback(
            string outputPath,
            Guid inVideoSubtype, uint srcW, uint srcH, uint outW, uint outH, uint fpsNum, uint fpsDen, uint videoBitrate,
            bool includeAudio,
            out uint videoStreamIndex, out uint audioStreamIndex, out AacMftEncoder? aac, out bool hasAudioOut)
        {
            Exception? lastError = null;
            foreach (bool useHardware in new[] { true, false })
            {
                try
                {
                    return CreateMp4SinkWriterCore(
                        outputPath, inVideoSubtype, srcW, srcH, outW, outH, fpsNum, fpsDen, videoBitrate,
                        includeAudio, useHardware,
                        out videoStreamIndex, out audioStreamIndex, out aac, out hasAudioOut);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                }
            }

            throw lastError!;
        }

        private static IMFSinkWriter CreateMp4SinkWriterCore(
            string outputPath,
            Guid inVideoSubtype, uint srcW, uint srcH, uint outW, uint outH, uint fpsNum, uint fpsDen, uint videoBitrate,
            bool includeAudio, bool useHardware,
            out uint videoStreamIndex, out uint audioStreamIndex, out AacMftEncoder? aac, out bool hasAudioOut)
        {
            audioStreamIndex = 0;
            aac = null;
            hasAudioOut = false;

            IMFAttributes? attrs = null;
            if (useHardware)
            {
                MfApi.MFCreateAttributes(out attrs, 1);
                attrs.SetUInt32(MfGuids.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
            }

            int hr = MfApi.MFCreateSinkWriterFromURL(Path.GetFullPath(outputPath), IntPtr.Zero, attrs, out var writer);
            if (attrs != null) Marshal.ReleaseComObject(attrs);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                MfApi.MFCreateMediaType(out var outVid);
                outVid.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video);
                outVid.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_H264);
                outVid.SetUInt32(MfGuids.MF_MT_AVG_BITRATE, videoBitrate);
                outVid.SetUInt32(MfGuids.MF_MT_INTERLACE_MODE, 2);
                outVid.SetUInt64(MfGuids.MF_MT_FRAME_SIZE, ((ulong)outW << 32) | outH);
                outVid.SetUInt64(MfGuids.MF_MT_FRAME_RATE, ((ulong)fpsNum << 32) | fpsDen);
                outVid.SetUInt64(MfGuids.MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);
                Marshal.ThrowExceptionForHR(writer.AddStream(outVid, out videoStreamIndex));
                Marshal.ReleaseComObject(outVid);

                MfApi.MFCreateMediaType(out var inVid);
                inVid.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video);
                inVid.SetGuid(MfGuids.MF_MT_SUBTYPE, inVideoSubtype);
                inVid.SetUInt32(MfGuids.MF_MT_INTERLACE_MODE, 2);
                inVid.SetUInt64(MfGuids.MF_MT_FRAME_SIZE, ((ulong)srcW << 32) | srcH);
                inVid.SetUInt64(MfGuids.MF_MT_FRAME_RATE, ((ulong)fpsNum << 32) | fpsDen);
                inVid.SetUInt64(MfGuids.MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);
                Marshal.ThrowExceptionForHR(writer.SetInputMediaType(videoStreamIndex, inVid, null));
                Marshal.ReleaseComObject(inVid);

                if (includeAudio)
                {
                    aac = new AacMftEncoder();
                    Marshal.ThrowExceptionForHR(writer.AddStream(aac.OutputType, out audioStreamIndex));
                    Marshal.ThrowExceptionForHR(writer.SetInputMediaType(audioStreamIndex, aac.OutputType, null));
                    hasAudioOut = true;
                }

                return writer;
            }
            catch
            {
                aac?.Dispose();
                aac = null;
                Marshal.ReleaseComObject(writer);
                throw;
            }
        }

        private static void PumpTranscodeSamples(
            IMFSourceReader reader,
            IMFSinkWriter writer,
            AacMftEncoder? aac,
            uint inVidStream,
            uint inAudStream,
            uint outVidStream,
            uint outAudStream,
            bool emitAudio,
            uint sampleRate,
            uint channels,
            long start100ns,
            long end100ns,
            long videoOffset,
            ref long maxVidTime,
            ref long maxAudTime)
        {
            long lastVid = -1;
            while (true)
            {
                int hr = reader.ReadSample(unchecked((uint)MfGuids.MF_SOURCE_READER_ANY_STREAM), 0, out uint streamIdx, out uint flags, out long ts, out var sample);
                if (hr < 0 || (flags & MfGuids.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                if (sample == null) continue;

                try
                {
                    if (ts < start100ns) continue;
                    if (ts > end100ns) break;

                    bool isVid = streamIdx == inVidStream;
                    bool isAud = emitAudio && aac != null && streamIdx == inAudStream;
                    if (!isVid && !isAud) continue;

                    if (isVid)
                    {
                        long shifted = Math.Max(0, ts + videoOffset);
                        if (shifted <= lastVid) shifted = lastVid + 1;
                        lastVid = shifted;
                        maxVidTime = Math.Max(maxVidTime, shifted);
                        sample.SetSampleTime(shifted);
                        Marshal.ThrowExceptionForHR(writer.WriteSample(outVidStream, sample));
                    }
                    else
                    {
                        short[] pcm = ExtractPcm48kStereo(sample, sampleRate, channels);
                        if (pcm.Length > 0)
                        {
                            long durationTicks = (long)pcm.Length / 2 * 10_000_000L / 48_000L;
                            maxAudTime += durationTicks;
                            aac!.Encode(pcm, encoded => Marshal.ThrowExceptionForHR(writer.WriteSample(outAudStream, encoded)));
                        }
                    }
                }
                finally { Marshal.ReleaseComObject(sample); }
            }
        }

        private static short[] ExtractPcm48kStereo(IMFSample sample, uint sampleRate, uint channels)
        {
            sample.ConvertToContiguousBuffer(out var buf);
            try
            {
                buf.Lock(out IntPtr ptr, out _, out uint len);
                try
                {
                    if (len < 2) return Array.Empty<short>();
                    int totalShorts = (int)(len / 2);
                    var raw = new short[totalShorts];
                    Marshal.Copy(ptr, raw, 0, totalShorts);

                    int inFrames = totalShorts / Math.Max(1, (int)channels);
                    if (inFrames == 0) return Array.Empty<short>();

                    if (sampleRate == 48_000 && channels == 2)
                    {
                        return raw;
                    }

                    int outFrames = Math.Max(1, (int)Math.Round(inFrames * (48_000.0 / Math.Max(8000, sampleRate))));
                    var output = new short[outFrames * 2];
                    for (int i = 0; i < outFrames; i++)
                    {
                        double srcPos = inFrames > 1 ? i * ((double)(inFrames - 1) / Math.Max(1, outFrames - 1)) : 0;
                        int i0 = Math.Min((int)srcPos, inFrames - 1);
                        int i1 = Math.Min(i0 + 1, inFrames - 1);
                        double frac = srcPos - i0;

                        if (channels == 1)
                        {
                            short s = (short)(raw[i0] + (raw[i1] - raw[i0]) * frac);
                            output[i * 2] = s;
                            output[i * 2 + 1] = s;
                        }
                        else
                        {
                            output[i * 2] = (short)(raw[i0 * 2] + (raw[i1 * 2] - raw[i0 * 2]) * frac);
                            output[i * 2 + 1] = (short)(raw[i0 * 2 + 1] + (raw[i1 * 2 + 1] - raw[i0 * 2 + 1]) * frac);
                        }
                    }

                    return output;
                }
                finally { buf.Unlock(); }
            }
            finally { Marshal.ReleaseComObject(buf); }
        }

        private sealed class ComReleaser : IDisposable
        {
            private readonly List<object> _items = new();
            public T Track<T>(T comObj) where T : class
            {
                _items.Add(comObj);
                return comObj;
            }
            public void Dispose()
            {
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    try { Marshal.ReleaseComObject(_items[i]); } catch { }
                }
            }
        }

        private static unsafe double ReadDurationSeconds(IMFSourceReader reader)
        {
            MfPropVariant pv = default;
            Guid durKey = MfGuids.MF_PD_DURATION;
            if (reader.GetPresentationAttribute(unchecked((uint)MfGuids.MF_SOURCE_READER_MEDIASOURCE), ref durKey, (IntPtr)(&pv)) >= 0)
            {
                try
                {
                    if (pv.vt == 20 || pv.vt == 21) // VT_I8 / VT_UI8
                    {
                        return Math.Max(0, pv.hVal / 10_000_000.0);
                    }
                }
                finally { MfApi.PropVariantClear((IntPtr)(&pv)); }
            }
            return 0;
        }

        private static unsafe void SetPosition100ns(IMFSourceReader reader, long pos100ns)
        {
            MfPropVariant pv = new MfPropVariant { vt = 20, hVal = Math.Max(0, pos100ns) };
            Guid empty = Guid.Empty;
            reader.SetCurrentPosition(ref empty, (IntPtr)(&pv));
        }

        private static void EnsureParentDir(string path)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        private static void EnsureDifferentVideoOutput(string videoPath, string outputPath)
        {
            if (string.Equals(Path.GetFullPath(videoPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The output path must be different from the source video.");
            }
        }

        private static BitmapSource FitGifFrame(BitmapSource frame, int maxWidth)
        {
            int side = Math.Min(Math.Max(2, maxWidth), 640);
            int longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (longest <= side)
            {
                frame.Freeze();
                return frame;
            }

            double scale = side / (double)longest;
            var resized = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            resized.Freeze();
            return resized;
        }
    }
}
