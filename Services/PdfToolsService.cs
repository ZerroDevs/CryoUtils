using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SkiaSharp;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CryoUtils.Services
{
    public static class PdfToolsService
    {
        private static readonly float[] GrayscaleMatrix =
        {
            0.2126f, 0.7152f, 0.0722f, 0f, 0f,
            0.2126f, 0.7152f, 0.0722f, 0f, 0f,
            0.2126f, 0.7152f, 0.0722f, 0f, 0f,
            0f,      0f,      0f,      1f, 0f
        };

        public static async Task<int> PdfToImagesAsync(string pdfPath, string outputDir, string format = "PNG")
        {
            Directory.CreateDirectory(outputDir);
            string ext = format.ToLowerInvariant().Contains("jp") ? "jpg" : "png";
            var encFormat = ext == "jpg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;

            int rendered = 0;
            await ForEachRenderedPageAsync(pdfPath, 2.0, (pageIndex, bitmap) =>
            {
                string outPath = Path.Combine(outputDir, $"page_{pageIndex + 1:D3}.{ext}");
                using var outFs = File.Create(outPath);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(encFormat, 90);
                encoded.SaveTo(outFs);
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
            using var doc = SKDocument.CreatePdf(outStream);

            int count = 0;
            foreach (var imgPath in list)
            {
                if (!File.Exists(imgPath))
                {
                    continue;
                }

                using var bitmap = SKBitmap.Decode(imgPath);
                if (bitmap == null)
                {
                    continue;
                }

                using var canvas = doc.BeginPage(bitmap.Width, bitmap.Height);
                canvas.DrawBitmap(bitmap, 0, 0);
                doc.EndPage();
                count++;
            }

            doc.Close();
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
            using var outDoc = SKDocument.CreatePdf(outStream);
            await ForEachRenderedPageAsync(pdfPath, 2.0, (pageIndex, bitmap) =>
            {
                if (!pages.Contains((int)pageIndex))
                {
                    return;
                }

                using var canvas = outDoc.BeginPage(bitmap.Width, bitmap.Height);
                using var image = SKImage.FromBitmap(bitmap);
                canvas.DrawBitmap(bitmap, 0, 0);
                outDoc.EndPage();
            }, pages.Select(page => (uint)page).ToHashSet()).ConfigureAwait(false);
            outDoc.Close();
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
            using var outDoc = SKDocument.CreatePdf(outStream);
            int pages = 0;
            foreach (string input in inputs)
            {
                await ForEachRenderedPageAsync(input, 2.0, (_, bitmap) =>
                {
                    using var canvas = outDoc.BeginPage(bitmap.Width, bitmap.Height);
                    using var image = SKImage.FromBitmap(bitmap);
                    canvas.DrawBitmap(bitmap, 0, 0);
                    outDoc.EndPage();
                    pages++;
                }).ConfigureAwait(false);
            }
            outDoc.Close();
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

                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                rendered[(int)pageIndex] = ImageIO.DecodeBytes(png.ToArray());
            }, order.Select(page => (uint)page).ToHashSet()).ConfigureAwait(false);

            EnsureParentDir(outputPath);
            using var outStream = File.Create(outputPath);
            using var outDoc = SKDocument.CreatePdf(outStream);
            int pages = 0;
            foreach (int pageIndex in order)
            {
                if (!rendered.TryGetValue(pageIndex, out var bitmap))
                {
                    continue;
                }
                using var canvas = outDoc.BeginPage(bitmap.PixelWidth, bitmap.PixelHeight);
                using var skBitmap = ToSkBitmap(bitmap);
                using var image = SKImage.FromBitmap(skBitmap);
                canvas.DrawImage(image, 0, 0);
                outDoc.EndPage();
                pages++;
            }
            outDoc.Close();
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

                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                rendered.Add((pageIndex, ImageIO.DecodeBytes(png.ToArray())));
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
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
            var doc = await PdfDocument.LoadFromFileAsync(file);
            return doc.PageCount;
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
            using var outDoc = SKDocument.CreatePdf(outStream);
            using var paint = grayscale
                ? new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(GrayscaleMatrix) }
                : null;

            await ForEachRenderedPageAsync(pdfPath, scale, (_, bitmap) =>
            {
                bool swapDims = rotationDegrees == 90 || rotationDegrees == 270;
                int pageW = swapDims ? bitmap.Height : bitmap.Width;
                int pageH = swapDims ? bitmap.Width : bitmap.Height;

                using var canvas = outDoc.BeginPage(pageW, pageH);
                using var srcImage = SKImage.FromBitmap(bitmap);
                using var jpegData = srcImage.Encode(SKEncodedImageFormat.Jpeg, quality);
                using var compressedBitmap = SKBitmap.Decode(jpegData);

                if (rotationDegrees != 0)
                {
                    canvas.Save();
                    canvas.Translate(pageW / 2f, pageH / 2f);
                    canvas.RotateDegrees(rotationDegrees);
                    canvas.DrawBitmap(compressedBitmap, -bitmap.Width / 2f, -bitmap.Height / 2f, paint);
                    canvas.Restore();
                }
                else
                {
                    canvas.DrawBitmap(compressedBitmap, 0, 0, paint);
                }

                outDoc.EndPage();
            }).ConfigureAwait(false);

            outDoc.Close();
            long newSize = new FileInfo(outputPath).Length;
            return originalSize - newSize;
        }

        private static async Task ForEachRenderedPageAsync(
            string pdfPath,
            double scale,
            Action<uint, SKBitmap> onPage,
            ISet<uint>? pages = null)
        {
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found", pdfPath);
            }

            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
            var doc = await PdfDocument.LoadFromFileAsync(file);

            for (uint i = 0; i < doc.PageCount; i++)
            {
                if (pages != null && !pages.Contains(i))
                {
                    continue;
                }
                using var page = doc.GetPage(i);
                using var stream = new InMemoryRandomAccessStream();

                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(100, page.Size.Width * scale),
                    DestinationHeight = (uint)Math.Max(100, page.Size.Height * scale)
                };

                await page.RenderToStreamAsync(stream, options);

                using var netStream = stream.AsStreamForRead();
                using var skData = SKData.Create(netStream);
                using var bitmap = SKBitmap.Decode(skData);
                if (bitmap != null)
                {
                    onPage(i, bitmap);
                }
            }
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

        private static SKBitmap ToSkBitmap(BitmapSource source)
        {
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);
            return SKBitmap.Decode(stream.ToArray())
                ?? throw new InvalidOperationException("Could not encode a PDF page.");
        }
    }
}
