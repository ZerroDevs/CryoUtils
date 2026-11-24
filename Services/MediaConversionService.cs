using System;
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
        public static async Task VideoToAudioAsync(string videoPath, string outputPath)
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
            var destFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(dir ?? "."));
            var destFile = await destFolder.CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.ReplaceExisting);

            var transcoder = new MediaTranscoder();
            var profile = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);

            var prep = await transcoder.PrepareFileTranscodeAsync(srcFile, destFile, profile);
            if (!prep.CanTranscode)
            {
                throw new InvalidOperationException($"Cannot transcode video: {prep.FailureReason}");
            }

            await prep.TranscodeAsync();
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
                duration = TimeSpan.FromSeconds(60); // Cap GIF conversion at 60s
            }

            double stepSeconds = 1.0 / Math.Clamp(fps, 5, 30);
            var frames = new System.Collections.Generic.List<BitmapSource>();

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
    }
}
