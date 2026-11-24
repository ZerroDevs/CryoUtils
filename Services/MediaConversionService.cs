using System;
using System.Collections.Generic;
using System.IO;
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

        public static async Task<int> VideoToGifAsync(string videoPath, string outputPath, int fps = 12, int maxWidth = 640)
        {
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("Video file not found", videoPath);
            }

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
            if (duration.TotalSeconds > 60)
            {
                duration = TimeSpan.FromSeconds(60);
            }

            double stepSeconds = 1.0 / Math.Clamp(fps, 5, 30);
            var frames = new List<BitmapSource>();

            for (double t = 0; t < duration.TotalSeconds; t += stepSeconds)
            {
                var time = TimeSpan.FromSeconds(t);
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
                    var frame = decoder.Frames[0];
                    frame.Freeze();
                    frames.Add(frame);
                }
            }

            if (frames.Count == 0)
            {
                return 0;
            }

            using (var fs = File.Create(outputPath))
            using (var writer = new GifWriter(fs, frames[0].PixelWidth, frames[0].PixelHeight, fps, frames))
            {
                foreach (var frame in frames)
                {
                    writer.AddFrame(frame);
                }
                writer.Finish();
            }

            return frames.Count;
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
