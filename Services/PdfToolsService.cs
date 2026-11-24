using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CryoUtils.Services
{
    public static class PdfToolsService
    {
        public static async Task<int> PdfToImagesAsync(string pdfPath, string outputDir, string format = "PNG")
        {
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found", pdfPath);
            }

            Directory.CreateDirectory(outputDir);
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
            var doc = await PdfDocument.LoadFromFileAsync(file);

            int pageCount = (int)doc.PageCount;
            string ext = format.ToLowerInvariant().Contains("jp") ? "jpg" : "png";
            var encFormat = ext == "jpg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;

            for (uint i = 0; i < pageCount; i++)
            {
                using var page = doc.GetPage(i);
                using var stream = new InMemoryRandomAccessStream();

                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(100, page.Size.Width * 2.0),
                    DestinationHeight = (uint)Math.Max(100, page.Size.Height * 2.0)
                };

                await page.RenderToStreamAsync(stream, options);

                using var netStream = stream.AsStreamForRead();
                using var skData = SKData.Create(netStream);
                using var bitmap = SKBitmap.Decode(skData);

                string outPath = Path.Combine(outputDir, $"page_{i + 1:D3}.{ext}");
                using var outFs = File.Create(outPath);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(encFormat, 90);
                encoded.SaveTo(outFs);
            }

            return pageCount;
        }

        public static int ImagesToPdf(IEnumerable<string> imagePaths, string outputPath)
        {
            var list = new List<string>(imagePaths);
            if (list.Count == 0)
            {
                return 0;
            }

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

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

        public static async Task<long> CompressPdfAsync(string pdfPath, string outputPath, int quality = 70)
        {
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found", pdfPath);
            }

            long originalSize = new FileInfo(pdfPath).Length;
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
            var doc = await PdfDocument.LoadFromFileAsync(file);

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var outStream = File.Create(outputPath);
            using var outDoc = SKDocument.CreatePdf(outStream);

            for (uint i = 0; i < doc.PageCount; i++)
            {
                using var page = doc.GetPage(i);
                using var stream = new InMemoryRandomAccessStream();

                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(100, page.Size.Width * 1.5),
                    DestinationHeight = (uint)Math.Max(100, page.Size.Height * 1.5)
                };

                await page.RenderToStreamAsync(stream, options);

                using var netStream = stream.AsStreamForRead();
                using var skData = SKData.Create(netStream);
                using var bitmap = SKBitmap.Decode(skData);

                using var canvas = outDoc.BeginPage(bitmap.Width, bitmap.Height);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 30, 95));
                using var compressedBitmap = SKBitmap.Decode(encoded);

                canvas.DrawBitmap(compressedBitmap, 0, 0);
                outDoc.EndPage();
            }

            outDoc.Close();
            long newSize = new FileInfo(outputPath).Length;
            return originalSize - newSize;
        }
    }
}
