using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace CryoUtils.Services
{
    public static class MediaConversionService
    {
        public static Task VideoToAudioAsync(string videoPath, string outputPath) =>
            ConvertAudioAsync(videoPath, outputPath, "MP3");

        public static async Task ConvertAudioAsync(string inputPath, string outputPath, string targetFormat)
        {
            var profile = targetFormat.ToUpperInvariant() switch
            {
                "WAV" => MediaEncodingProfile.CreateWav(AudioEncodingQuality.High),
                "M4A" => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High),
                _ => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High)
            };

            await TranscodeFileAsync(inputPath, outputPath, profile).ConfigureAwait(false);
        }

        public static async Task<long> CompressVideoForDiscordAsync(
            string videoPath,
            string outputPath,
            long targetMaxBytes = 9_500_000)
        {
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("Video file not found", videoPath);
            }

            long originalBytes = new FileInfo(videoPath).Length;
            var srcFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(videoPath));
            var clip = await MediaClip.CreateFromFileAsync(srcFile);
            double durationSec = Math.Max(1.0, clip.OriginalDuration.TotalSeconds);
            var srcVideoProps = clip.GetVideoEncodingProperties();

            uint audioBitrate = 128_000;
            double totalBitsPerSec = (targetMaxBytes * 8.0) / durationSec;
            uint targetVideoBitrate = (uint)Math.Clamp(totalBitsPerSec - audioBitrate, 350_000, 6_000_000);

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            if (srcVideoProps != null && srcVideoProps.Width > 0 && srcVideoProps.Height > 0 && srcVideoProps.Width < 1280)
            {
                profile.Video.Width = Math.Max(16, (srcVideoProps.Width + 1) & ~1u);
                profile.Video.Height = Math.Max(16, (srcVideoProps.Height + 1) & ~1u);
            }
            profile.Video.Bitrate = targetVideoBitrate;
            if (profile.Audio != null)
            {
                profile.Audio.Bitrate = audioBitrate;
            }

            await TranscodeFileAsync(videoPath, outputPath, profile).ConfigureAwait(false);
            long compressedBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : originalBytes;
            return originalBytes - compressedBytes;
        }

        public static async Task MuteVideoAsync(string videoPath, string outputPath)
        {
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("Video file not found", videoPath);
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".";
            Directory.CreateDirectory(dir);

            var srcFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(videoPath));
            var destFolder = await StorageFolder.GetFolderFromPathAsync(dir);
            var destFile = await destFolder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.ReplaceExisting);

            var clip = await MediaClip.CreateFromFileAsync(srcFile);
            clip.Volume = 0;

            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            var profile = composition.CreateDefaultEncodingProfile();
            if (profile?.Video == null || profile.Video.Width == 0 || profile.Video.Height == 0)
            {
                profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            }

            var status = await composition.RenderToFileAsync(destFile, MediaTrimmingPreference.Precise, profile);
            if (status != TranscodeFailureReason.None)
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                throw new InvalidOperationException($"Mute failed: {status}");
            }
        }

        public static async Task SaveThumbnailAsync(string videoPath, string outputPath)
        {
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("Video file not found", videoPath);
            }

            EnsureDifferentVideoOutput(videoPath, outputPath);

            var srcFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(videoPath));
            var clip = await MediaClip.CreateFromFileAsync(srcFile);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            double captureSec = Math.Min(2.0, composition.Duration.TotalSeconds * 0.25);
            using var thumbStream = await composition.GetThumbnailAsync(
                TimeSpan.FromSeconds(Math.Max(0, captureSec)),
                0,
                0,
                VideoFramePrecision.NearestFrame);

            using var netStream = thumbStream.AsStreamForRead();
            var decoder = BitmapDecoder.Create(
                netStream,
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                throw new InvalidOperationException("Could not capture video frame.");
            }

            var frame = decoder.Frames[0];
            frame.Freeze();

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            ImageIO.SaveBitmap(frame, outputPath);
        }

        public static async Task TrimVideoAsync(
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

            var srcFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(videoPath));
            var clip = await MediaClip.CreateFromFileAsync(srcFile);
            double duration = clip.OriginalDuration.TotalSeconds;
            double start = Math.Clamp(startSeconds, 0, Math.Max(0, duration - 0.1));
            double end = endSeconds <= 0 ? duration : Math.Clamp(endSeconds, start + 0.1, duration);
            if (end <= start)
            {
                throw new ArgumentOutOfRangeException(nameof(endSeconds), "The end time must be after the start time.");
            }

            clip.TrimTimeFromStart = TimeSpan.FromSeconds(start);
            clip.TrimTimeFromEnd = TimeSpan.FromSeconds(Math.Max(0, duration - end));

            var composition = new MediaComposition();
            composition.Clips.Add(clip);
            string fullOut = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullOut) ?? ".";
            Directory.CreateDirectory(directory);
            var destinationFolder = await StorageFolder.GetFolderFromPathAsync(directory);
            var destination = await destinationFolder.CreateFileAsync(
                Path.GetFileName(fullOut),
                CreationCollisionOption.ReplaceExisting);
            var profile = composition.CreateDefaultEncodingProfile();
            var status = await composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, profile);
            if (status != TranscodeFailureReason.None)
            {
                try { File.Delete(fullOut); } catch { }
                throw new InvalidOperationException($"Video trim failed: {status}");
            }
        }

        private const int MaxGifFrames = 240;

        public static async Task<int> VideoToGifAsync(
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

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var srcFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(videoPath));
            var clip = await MediaClip.CreateFromFileAsync(srcFile);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            TimeSpan duration = composition.Duration;
            double start = Math.Clamp(startSeconds, 0, Math.Max(0, duration.TotalSeconds - 0.05));
            double end = endSeconds <= 0
                ? duration.TotalSeconds
                : Math.Clamp(endSeconds, start + 0.05, duration.TotalSeconds);
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
                var time = TimeSpan.FromSeconds(start + t);
                using var thumbStream = await composition.GetThumbnailAsync(
                    time,
                    maxWidth,
                    0,
                    VideoFramePrecision.NearestFrame);

                using var netStream = thumbStream.AsStreamForRead();
                var decoder = BitmapDecoder.Create(
                    netStream,
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);

                if (decoder.Frames.Count > 0)
                {
                    frames.Add(FitGifFrame(decoder.Frames[0], maxWidth));
                }
            }

            if (frames.Count == 0)
            {
                return 0;
            }

            progress?.Report(100);
            using (var fs = File.Create(outputPath))
            using (var writer = new GifWriter(fs, frames[0].PixelWidth, frames[0].PixelHeight, actualFps, frames))
            {
                foreach (var frame in frames)
                {
                    writer.AddFrame(frame);
                }
                writer.Finish();
            }

            return frames.Count;
        }

        private static void EnsureDifferentVideoOutput(string videoPath, string outputPath)
        {
            if (string.Equals(
                    Path.GetFullPath(videoPath),
                    Path.GetFullPath(outputPath),
                    StringComparison.OrdinalIgnoreCase))
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
            var resized = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
            resized.Freeze();
            return resized;
        }

        private static async Task TranscodeFileAsync(string inputPath, string outputPath, MediaEncodingProfile profile)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("Media file not found", inputPath);
            }

            string fullOut = Path.GetFullPath(outputPath);
            string dir = Path.GetDirectoryName(fullOut) ?? ".";
            Directory.CreateDirectory(dir);

            var srcFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(inputPath));
            var destFolder = await StorageFolder.GetFolderFromPathAsync(dir);
            var destFile = await destFolder.CreateFileAsync(Path.GetFileName(fullOut), CreationCollisionOption.ReplaceExisting);

            var transcoder = new MediaTranscoder();
            var prep = await transcoder.PrepareFileTranscodeAsync(srcFile, destFile, profile);
            if (!prep.CanTranscode)
            {
                throw new InvalidOperationException($"Cannot transcode media: {prep.FailureReason}");
            }

            await prep.TranscodeAsync();
        }
    }
}
