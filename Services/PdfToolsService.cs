using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public static class PdfToolsService
    {
        public static async Task<int> PdfToImagesAsync(string pdfPath, string outputDir, string format = "PNG")
        {
            Directory.CreateDirectory(outputDir);
            string ext = format.ToLowerInvariant().Contains("jp") ? "jpg" : "png";

            int rendered = 0;
            await ForEachRenderedPageAsync(pdfPath, 2.0, (pageIndex, bitmap) =>
            {
                string outPath = Path.Combine(outputDir, $"page_{pageIndex + 1:D3}.{ext}");
                byte[] encoded = ext == "jpg"
                    ? ImageIO.EncodeJpeg(bitmap, 90)
                    : ImageIO.EncodePng(bitmap);
                File.WriteAllBytes(outPath, encoded);
                rendered++;
            }).ConfigureAwait(false);

            return rendered;
        }

        public static int ImagesToPdf(IEnumerable<string> imagePaths, string outputPath)
        {
            var list = new List<string>(imagePaths);
            if (list.Count == 0)
            {
                return 0;
            }

            EnsureParentDir(outputPath);
            using var outStream = File.Create(outputPath);
            using var writer = new SimplePdfWriter(outStream);

            int count = 0;
            foreach (var imgPath in list)
            {
                if (!File.Exists(imgPath))
                {
                    continue;
                }

                var bitmap = ImageIO.LoadFileBitmap(imgPath);
                writer.AddBitmapPage(bitmap, preferJpeg: false);
                count++;
            }

            writer.Finish();
            return count;
        }

        public static Task<long> CompressPdfAsync(string pdfPath, string outputPath, int quality = 70) =>
            TransformPdfPagesAsync(pdfPath, outputPath, 1.5, Math.Clamp(quality, 30, 95), grayscale: false, rotationDegrees: 0);

        public static Task<long> ConvertPdfToGrayscaleAsync(string pdfPath, string outputPath, int quality = 75) =>
            TransformPdfPagesAsync(pdfPath, outputPath, 1.5, Math.Clamp(quality, 30, 95), grayscale: true, rotationDegrees: 0);

        public static Task<long> RotatePdfAsync(string pdfPath, string outputPath, int degrees) =>
            TransformPdfPagesAsync(pdfPath, outputPath, 1.8, 85, grayscale: false, rotationDegrees: ((degrees % 360) + 360) % 360);

        public static async Task<int> ExtractPdfPagesAsync(string pdfPath, string outputPath, string pageRange)
        {
            EnsureDistinctOutput(pdfPath, outputPath);
            uint pageCount = await GetPageCountAsync(pdfPath).ConfigureAwait(false);
            var pages = ParsePageRange(pageRange, pageCount);
            if (pages.Count == 0)
            {
                throw new InvalidOperationException("The page range did not select any pages.");
            }

            EnsureParentDir(outputPath);
            using var outStream = File.Create(outputPath);
            using var writer = new SimplePdfWriter(outStream);
            await ForEachRenderedPageAsync(pdfPath, 2.0, (pageIndex, bitmap) =>
            {
                if (!pages.Contains((int)pageIndex))
                {
                    return;
                }

                writer.AddBitmapPage(bitmap, preferJpeg: false);
            }, pages.Select(page => (uint)page).ToHashSet()).ConfigureAwait(false);
            writer.Finish();
            return pages.Count;
        }

        public static async Task<int> MergePdfsAsync(IEnumerable<string> pdfPaths, string outputPath)
        {
            var inputs = pdfPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (inputs.Count == 0)
            {
                throw new InvalidOperationException("No PDF files were selected.");
            }

            foreach (string input in inputs)
            {
                EnsureDistinctOutput(input, outputPath);
            }

            EnsureParentDir(outputPath);
            using var outStream = File.Create(outputPath);
            using var writer = new SimplePdfWriter(outStream);
            int pages = 0;
            foreach (string input in inputs)
            {
                await ForEachRenderedPageAsync(input, 2.0, (_, bitmap) =>
                {
                    writer.AddBitmapPage(bitmap, preferJpeg: false);
                    pages++;
                }).ConfigureAwait(false);
            }
            writer.Finish();
            return pages;
        }

        public static async Task<int> SplitPdfAsync(string pdfPath, string outputDirectory, IEnumerable<string> ranges)
        {
            Directory.CreateDirectory(outputDirectory);
            int index = 1;
            int parts = 0;
            foreach (string range in ranges)
            {
                string output = Path.Combine(outputDirectory, $"part_{index++}.pdf");
                await ExtractPdfPagesAsync(pdfPath, output, range).ConfigureAwait(false);
                parts++;
            }
            return parts;
        }

        public static async Task<int> ReorderPdfAsync(string pdfPath, string outputPath, string pageOrder)
        {
            EnsureDistinctOutput(pdfPath, outputPath);
            uint pageCount = await GetPageCountAsync(pdfPath).ConfigureAwait(false);
            var order = ParsePageRange(pageOrder, pageCount);
            if (order.Count == 0)
            {
                throw new InvalidOperationException("The page order did not select any pages.");
            }

            var rendered = new Dictionary<int, BitmapSource>();
            await ForEachRenderedPageAsync(pdfPath, 1.5, (pageIndex, bitmap) =>
            {
                if (!order.Contains((int)pageIndex))
                {
                    return;
                }

                rendered[(int)pageIndex] = bitmap;
            }, order.Select(page => (uint)page).ToHashSet()).ConfigureAwait(false);

            EnsureParentDir(outputPath);
            using var outStream = File.Create(outputPath);
            using var writer = new SimplePdfWriter(outStream);
            int pages = 0;
            foreach (int pageIndex in order)
            {
                if (!rendered.TryGetValue(pageIndex, out var bitmap))
                {
                    continue;
                }
                writer.AddBitmapPage(bitmap, preferJpeg: false);
                pages++;
            }
            writer.Finish();
            return pages;
        }

        public static async Task<string> OcrPdfAsync(string pdfPath, string pageRange)
        {
            uint pageCount = await GetPageCountAsync(pdfPath).ConfigureAwait(false);
            var pages = ParsePageRange(pageRange, pageCount);
            var text = new StringBuilder();
            var rendered = new List<(uint Page, BitmapSource Image)>();
            await ForEachRenderedPageAsync(pdfPath, 2.0, (pageIndex, bitmap) =>
            {
                if (!pages.Contains((int)pageIndex))
                {
                    return;
                }

                rendered.Add((pageIndex, bitmap));
            }, pages.Select(page => (uint)page).ToHashSet()).ConfigureAwait(false);

            foreach (var page in rendered)
            {
                string pageText = await OcrService.RecognizeAsync(page.Image).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    text.AppendLine($"--- Page {page.Page + 1} ---");
                    text.AppendLine(pageText);
                }
            }
            return text.ToString().Trim();
        }

        public static List<int> ParsePageRange(string? range, uint pageCount)
        {
            var pages = new List<int>();
            if (pageCount == 0)
            {
                return pages;
            }

            if (string.IsNullOrWhiteSpace(range))
            {
                return Enumerable.Range(0, (int)pageCount).ToList();
            }

            foreach (string token in range.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = token.Trim();
                if (part.Contains('-', StringComparison.Ordinal))
                {
                    string[] bounds = part.Split('-', 2);
                    if (int.TryParse(bounds[0].Trim(), out int start) &&
                        int.TryParse(bounds[1].Trim(), out int end))
                    {
                        for (int page = Math.Max(1, start); page <= Math.Min((int)pageCount, end); page++)
                        {
                            if (!pages.Contains(page - 1))
                            {
                                pages.Add(page - 1);
                            }
                        }
                    }
                }
                else if (int.TryParse(part, out int page) && page >= 1 && page <= pageCount)
                {
                    if (!pages.Contains(page - 1))
                    {
                        pages.Add(page - 1);
                    }
                }
            }

            return pages;
        }

        private static async Task<uint> GetPageCountAsync(string pdfPath)
        {
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found", pdfPath);
            }
            return await WinRtBridge.GetPdfPageCountAsync(Path.GetFullPath(pdfPath)).ConfigureAwait(false);
        }

        private static async Task<long> TransformPdfPagesAsync(
            string pdfPath,
            string outputPath,
            double scale,
            int quality,
            bool grayscale,
            int rotationDegrees)
        {
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found", pdfPath);
            }

            long originalSize = new FileInfo(pdfPath).Length;
            EnsureParentDir(outputPath);

            using var outStream = File.Create(outputPath);
            using var writer = new SimplePdfWriter(outStream);

            await ForEachRenderedPageAsync(pdfPath, scale, (_, bitmap) =>
            {
                BitmapSource processed = bitmap;
                if (grayscale)
                {
                    var gray = new FormatConvertedBitmap(processed, PixelFormats.Gray8, null, 0);
                    gray.Freeze();
                    processed = gray;
                }

                if (rotationDegrees != 0)
                {
                    var rotated = new TransformedBitmap(processed, new RotateTransform(rotationDegrees));
                    rotated.Freeze();
                    processed = rotated;
                }

                writer.AddBitmapPage(processed, preferJpeg: true, jpegQuality: quality);
            }).ConfigureAwait(false);

            writer.Finish();
            long newSize = new FileInfo(outputPath).Length;
            return originalSize - newSize;
        }

        private static async Task ForEachRenderedPageAsync(
            string pdfPath,
            double scale,
            Action<uint, BitmapSource> onPage,
            ISet<uint>? pages = null)
        {
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found", pdfPath);
            }

            await WinRtBridge.RenderPdfPagesAsync(
                Path.GetFullPath(pdfPath),
                scale,
                (pageIndex, pngBytes) => onPage(pageIndex, ImageIO.DecodeBytes(pngBytes)),
                pages).ConfigureAwait(false);
        }

        private static void EnsureParentDir(string outputPath)
        {
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static void EnsureDistinctOutput(string inputPath, string outputPath)
        {
            if (string.Equals(
                    Path.GetFullPath(inputPath),
                    Path.GetFullPath(outputPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The output path must be different from the source PDF.");
            }
        }
    }
}
