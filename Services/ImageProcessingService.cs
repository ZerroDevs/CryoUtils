using System;
using System.IO;
using SkiaSharp;

namespace CryoUtils.Services
{
    public static class ImageProcessingService
    {
        public static void ConvertFormat(string inputPath, string outputPath, string targetFormat)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("Image file not found", inputPath);
            }

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var inStream = File.OpenRead(inputPath);
            using var bitmap = SKBitmap.Decode(inStream);
            if (bitmap == null)
            {
                throw new InvalidOperationException("Could not decode image format");
            }

            string fmt = targetFormat.TrimStart('.').ToUpperInvariant();
            if (fmt == "ICO")
            {
                SaveAsIco(bitmap, outputPath);
                return;
            }

            var skFormat = fmt switch
            {
                "PNG" => SKEncodedImageFormat.Png,
                "WEBP" => SKEncodedImageFormat.Webp,
                "BMP" => SKEncodedImageFormat.Bmp,
                "JFIF" or "JPG" or "JPEG" => SKEncodedImageFormat.Jpeg,
                _ => SKEncodedImageFormat.Png
            };

            int quality = fmt is "JPG" or "JPEG" or "JFIF" or "WEBP" ? 92 : 100;
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(skFormat, quality);
            using var outFs = File.Create(outputPath);
            data.SaveTo(outFs);
        }

        public static void StripExif(string inputPath, string outputPath)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("Image file not found", inputPath);
            }

            using var inFs = File.OpenRead(inputPath);
            using var bitmap = SKBitmap.Decode(inFs);
            if (bitmap == null)
            {
                throw new InvalidOperationException("Could not decode image");
            }

            string ext = Path.GetExtension(inputPath).ToLowerInvariant();
            var format = ext switch
            {
                ".jpg" or ".jpeg" or ".jfif" => SKEncodedImageFormat.Jpeg,
                ".webp" => SKEncodedImageFormat.Webp,
                ".bmp" => SKEncodedImageFormat.Bmp,
                _ => SKEncodedImageFormat.Png
            };

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, 92);
            using var outFs = File.Create(outputPath);
            data.SaveTo(outFs);
        }

        public static void RemoveBackground(string inputPath, string outputPath, byte tolerance = 35)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("Image file not found", inputPath);
            }

            using var inFs = File.OpenRead(inputPath);
            using var srcBitmap = SKBitmap.Decode(inFs);
            if (srcBitmap == null)
            {
                throw new InvalidOperationException("Could not decode image");
            }

            // Ensure 32-bit RGBA
            using var bitmap = new SKBitmap(srcBitmap.Width, srcBitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.DrawBitmap(srcBitmap, 0, 0);
            }

            int w = bitmap.Width;
            int h = bitmap.Height;

            // Sample corners
            var c1 = bitmap.GetPixel(0, 0);
            var c2 = bitmap.GetPixel(Math.Max(0, w - 1), 0);
            var c3 = bitmap.GetPixel(0, Math.Max(0, h - 1));
            var c4 = bitmap.GetPixel(Math.Max(0, w - 1), Math.Max(0, h - 1));

            int bgR = (c1.Red + c2.Red + c3.Red + c4.Red) / 4;
            int bgG = (c1.Green + c2.Green + c3.Green + c4.Green) / 4;
            int bgB = (c1.Blue + c2.Blue + c3.Blue + c4.Blue) / 4;

            double tol = Math.Max(10, (int)tolerance);
            double feather = 18.0;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var px = bitmap.GetPixel(x, y);
                    int dr = px.Red - bgR;
                    int dg = px.Green - bgG;
                    int db = px.Blue - bgB;
                    double dist = Math.Sqrt(dr * dr + dg * dg + db * db);

                    if (dist <= tol)
                    {
                        bitmap.SetPixel(x, y, new SKColor(px.Red, px.Green, px.Blue, 0));
                    }
                    else if (dist < tol + feather)
                    {
                        double alphaFactor = (dist - tol) / feather;
                        byte newAlpha = (byte)Math.Clamp(px.Alpha * alphaFactor, 0, 255);
                        bitmap.SetPixel(x, y, new SKColor(px.Red, px.Green, px.Blue, newAlpha));
                    }
                }
            }

            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var outFs = File.Create(outputPath);
            data.SaveTo(outFs);
        }

        private static void SaveAsIco(SKBitmap source, string outputPath)
        {
            int iconSize = Math.Min(256, Math.Max(source.Width, source.Height));
            using var resized = new SKBitmap(iconSize, iconSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(resized))
            {
                canvas.Clear(SKColors.Transparent);
                float scale = Math.Min(iconSize / (float)source.Width, iconSize / (float)source.Height);
                float targetW = source.Width * scale;
                float targetH = source.Height * scale;
                float left = (iconSize - targetW) / 2f;
                float top = (iconSize - targetH) / 2f;
                canvas.DrawBitmap(source, new SKRect(left, top, left + targetW, top + targetH));
            }

            using var image = SKImage.FromBitmap(resized);
            using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
            byte[] pngBytes = pngData.ToArray();

            using var fs = File.Create(outputPath);
            using var bw = new BinaryWriter(fs);

            // ICONDIR header
            bw.Write((ushort)0); // reserved
            bw.Write((ushort)1); // type 1 = icon
            bw.Write((ushort)1); // count = 1

            // ICONDIRENTRY (16 bytes)
            byte wByte = iconSize >= 256 ? (byte)0 : (byte)iconSize;
            byte hByte = iconSize >= 256 ? (byte)0 : (byte)iconSize;
            bw.Write(wByte);
            bw.Write(hByte);
            bw.Write((byte)0); // colors
            bw.Write((byte)0); // reserved
            bw.Write((ushort)1); // color planes
            bw.Write((ushort)32); // bpp
            bw.Write((uint)pngBytes.Length); // size
            bw.Write((uint)22); // offset (6 + 16 = 22)

            bw.Write(pngBytes);
        }
    }
}
