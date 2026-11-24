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

        private static async Task ForEachRenderedPageAsync(string pdfPath, double scale, Action<uint, SKBitmap> onPage)
        {
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file not found", pdfPath);
            }

            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
            var doc = await PdfDocument.LoadFromFileAsync(file);

            for (uint i = 0; i < doc.PageCount; i++)
            {
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
    }
}
