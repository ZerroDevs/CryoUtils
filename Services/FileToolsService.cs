using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace CryoUtils.Services
{
    public static class FileToolsService
    {
        private const int MaxGifFrames = 240;

        private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".jfif"
        };

        public static void FormatJson(string inputPath, string outputPath, bool indent)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("JSON file not found", inputPath);
            }

            byte[] raw = File.ReadAllBytes(inputPath);
            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            EnsureParentDir(outputPath);
            using var outFs = File.Create(outputPath);
            using var writer = new Utf8JsonWriter(outFs, new JsonWriterOptions { Indented = indent });
            doc.WriteTo(writer);
            writer.Flush();
        }

        public static void FormatXml(string inputPath, string outputPath)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("XML file not found", inputPath);
            }

            var doc = XDocument.Load(inputPath);
            EnsureParentDir(outputPath);
            doc.Save(outputPath, SaveOptions.None);
        }

        public static string ComputeHash(string inputPath, HashAlgorithmName algorithm)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("File not found", inputPath);
            }

            using var hasher = IncrementalHash.CreateHash(algorithm);
            using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
            byte[] buffer = new byte[65536];
            int read;
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
            }

            return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }

        public static string CreateUniquePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            string directory = Path.GetDirectoryName(path) ?? ".";
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int suffix = 2; suffix < 1000; suffix++)
            {
                string candidate = Path.Combine(directory, $"{stem}_{suffix}{extension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{extension}");
        }

        public static List<string> CollectSortedImages(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
            }

            var files = Directory.EnumerateFiles(folderPath)
                .Where(f => SupportedImageExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            files.Sort((a, b) => NativeMethods.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
            return files;
        }

        public static int FolderImagesToGif(
            string folderPath,
            string outputPath,
            int fps = 4,
            int maxWidth = 640,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var images = CollectSortedImages(folderPath);
            if (images.Count == 0)
            {
                return 0;
            }

            var first = ImageIO.LoadFileBitmap(images[0]);
            double scale = Math.Min(
                1.0,
                Math.Min(maxWidth / (double)first.PixelWidth, maxWidth / (double)first.PixelHeight));
            int targetW = Math.Max(2, (int)Math.Round(first.PixelWidth * scale));
            int targetH = Math.Max(2, (int)Math.Round(first.PixelHeight * scale));

            var frames = new List<BitmapSource>();
            int total = Math.Min(images.Count, MaxGifFrames);
            progress?.Report(0);
            foreach (var imgPath in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (frames.Count >= MaxGifFrames)
                {
                    break;
                }

                BitmapSource? src;
                try
                {
                    src = ImageIO.LoadFileBitmap(imgPath);
                }
                catch
                {
                    continue;
                }

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, targetW, targetH));
                    double fitScale = Math.Min(targetW / (double)src.PixelWidth, targetH / (double)src.PixelHeight);
                    double drawW = src.PixelWidth * fitScale;
                    double drawH = src.PixelHeight * fitScale;
                    double left = (targetW - drawW) / 2.0;
                    double top = (targetH - drawH) / 2.0;
                    dc.DrawImage(src, new Rect(left, top, drawW, drawH));
                }

                var fitted = new RenderTargetBitmap(targetW, targetH, 96, 96, PixelFormats.Pbgra32);
                fitted.Render(visual);
                fitted.Freeze();
                frames.Add(fitted);
                if (total > 0)
                {
                    progress?.Report((int)Math.Round(frames.Count * 100.0 / total));
                }
            }

            if (frames.Count == 0)
            {
                return 0;
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100);
            EnsureParentDir(outputPath);
            using (var fs = File.Create(outputPath))
            using (var writer = new GifWriter(fs, targetW, targetH, Math.Clamp(fps, 1, 20), frames))
            {
                foreach (var frame in frames)
                {
                    writer.AddFrame(frame);
                }
                writer.Finish();
            }

            return frames.Count;
        }

        public static async Task<(int Count, long SavedBytes)> BatchCompressFolderAsync(string folderPath)
        {
            var images = CollectSortedImages(folderPath);
            if (images.Count == 0)
            {
                return (0, 0);
            }

            var settings = SettingsService.Instance.Current;
            string outDir = Path.Combine(folderPath, "_compressed");
            Directory.CreateDirectory(outDir);

            int count = 0;
            long totalSaved = 0;
            foreach (var imgPath in images)
            {
                try
                {
                    var res = await CompressionService.CompressAsync(
                        imgPath,
                        settings.CompressorPreset,
                        settings.CompressorQuality,
                        settings.CompressorScale).ConfigureAwait(false);

                    string outName = Path.GetFileNameWithoutExtension(imgPath) + res.Extension;
                    await File.WriteAllBytesAsync(Path.Combine(outDir, outName), res.Data).ConfigureAwait(false);
                    totalSaved += Math.Max(0, res.OriginalBytes - res.CompressedBytes);
                    count++;
                }
                catch
                {
                }
            }

            return (count, totalSaved);
        }

        private static void EnsureParentDir(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
