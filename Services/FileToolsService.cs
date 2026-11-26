using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using SkiaSharp;

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

            using var first = SKBitmap.Decode(images[0]) ?? throw new InvalidOperationException("Could not decode first image.");
            double scale = Math.Min(
                1.0,
                Math.Min(maxWidth / (double)first.Width, maxWidth / (double)first.Height));
            int targetW = Math.Max(2, (int)Math.Round(first.Width * scale));
            int targetH = Math.Max(2, (int)Math.Round(first.Height * scale));

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

                using var src = SKBitmap.Decode(imgPath);
                if (src == null)
                {
                    continue;
                }

                using var fitted = new SKBitmap(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var canvas = new SKCanvas(fitted))
                {
                    canvas.Clear(SKColors.Black);
                    float fitScale = Math.Min(targetW / (float)src.Width, targetH / (float)src.Height);
                    float drawW = src.Width * fitScale;
                    float drawH = src.Height * fitScale;
                    float left = (targetW - drawW) / 2f;
                    float top = (targetH - drawH) / 2f;
                    canvas.DrawBitmap(src, new SKRect(left, top, left + drawW, top + drawH));
                }

                using var skImg = SKImage.FromBitmap(fitted);
                using var png = skImg.Encode(SKEncodedImageFormat.Png, 100);
                frames.Add(ImageIO.DecodeBytes(png.ToArray()));
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
