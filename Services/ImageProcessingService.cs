using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public static class ImageProcessingService
    {
        public static void ConvertFormat(string inputPath, string outputPath, string targetFormat)
        {
            if (string.Equals(
                    Path.GetFullPath(inputPath),
                    Path.GetFullPath(outputPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The conversion output must not overwrite the source image.");
            }

            var bitmap = ImageIO.LoadFileBitmap(inputPath);
            string fmt = targetFormat.TrimStart('.').ToUpperInvariant();
            if (fmt == "ICO")
            {
                EnsureParentDir(outputPath);
                SaveAsIco(bitmap, outputPath);
                return;
            }

            EncodeAndSave(bitmap, outputPath, fmt, fmt is "JPG" or "JPEG" or "JFIF" or "WEBP" ? 92 : 100);
        }

        public static void StripExif(string inputPath, string outputPath)
        {
            var bitmap = ImageIO.LoadFileBitmap(inputPath);
            EncodeAndSave(bitmap, outputPath, FormatFromPath(inputPath), 92);
        }

        public static void RemoveBackground(string inputPath, string outputPath, byte tolerance = 35)
        {
            var converted = EnsureBgra32(ImageIO.LoadFileBitmap(inputPath));
            int w = converted.PixelWidth;
            int h = converted.PixelHeight;
            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            converted.CopyPixels(pixels, stride, 0);

            (byte R, byte G, byte B) Sample(int x, int y)
            {
                int i = y * stride + x * 4;
                return (pixels[i + 2], pixels[i + 1], pixels[i]);
            }

            var c1 = Sample(0, 0);
            var c2 = Sample(Math.Max(0, w - 1), 0);
            var c3 = Sample(0, Math.Max(0, h - 1));
            var c4 = Sample(Math.Max(0, w - 1), Math.Max(0, h - 1));

            int bgR = (c1.R + c2.R + c3.R + c4.R) / 4;
            int bgG = (c1.G + c2.G + c3.G + c4.G) / 4;
            int bgB = (c1.B + c2.B + c3.B + c4.B) / 4;

            double tol = Math.Max(10, (int)tolerance);
            double feather = 18.0;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                int dr = pixels[i + 2] - bgR;
                int dg = pixels[i + 1] - bgG;
                int db = pixels[i] - bgB;
                double dist = Math.Sqrt(dr * dr + dg * dg + db * db);

                if (dist <= tol)
                {
                    pixels[i + 3] = 0;
                }
                else if (dist < tol + feather)
                {
                    double alphaFactor = (dist - tol) / feather;
                    pixels[i + 3] = (byte)Math.Clamp(pixels[i + 3] * alphaFactor, 0, 255);
                }
            }

            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            EncodeAndSave(result, outputPath, "PNG", 100);
        }

        public static (int Width, int Height) ResizePreset(string inputPath, string outputPath, string preset)
        {
            var source = ImageIO.LoadFileBitmap(inputPath);
            int targetW = source.PixelWidth;
            int targetH = source.PixelHeight;

            switch (preset.ToLowerInvariant())
            {
                case "50pct":
                    targetW = Math.Max(1, source.PixelWidth / 2);
                    targetH = Math.Max(1, source.PixelHeight / 2);
                    break;
                case "w1920":
                    if (source.PixelWidth > 1920)
                    {
                        targetW = 1920;
                        targetH = Math.Max(1, (int)Math.Round(source.PixelHeight * (1920.0 / source.PixelWidth)));
                    }
                    break;
                case "s512":
                    int maxSide = Math.Max(source.PixelWidth, source.PixelHeight);
                    if (maxSide > 512)
                    {
                        double scale = 512.0 / maxSide;
                        targetW = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
                        targetH = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
                    }
                    break;
            }

            var target = ImageIO.ResizeToExact(source, targetW, targetH);
            if (Path.GetExtension(outputPath).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                EnsureParentDir(outputPath);
                SaveAsIco(target, outputPath);
            }
            else
            {
                EncodeAndSave(target, outputPath, FormatFromPath(outputPath), 92);
            }

            return (target.PixelWidth, target.PixelHeight);
        }

        public static (int Width, int Height) TrimTransparentMargins(string inputPath, string outputPath)
        {
            var bitmap = EnsureBgra32(ImageIO.LoadFileBitmap(inputPath));
            int w = bitmap.PixelWidth;
            int h = bitmap.PixelHeight;
            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            bitmap.CopyPixels(pixels, stride, 0);

            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    if (pixels[row + x * 4 + 3] > 0)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY)
            {
                EncodeAndSave(bitmap, outputPath, "PNG", 100);
                return (w, h);
            }

            int cropW = maxX - minX + 1;
            int cropH = maxY - minY + 1;
            var cropped = new CroppedBitmap(bitmap, new Int32Rect(minX, minY, cropW, cropH));
            cropped.Freeze();
            EncodeAndSave(cropped, outputPath, "PNG", 100);
            return (cropW, cropH);
        }

        public static void FlattenWhite(string inputPath, string outputPath)
        {
            var src = ImageIO.LoadFileBitmap(inputPath);
            var flat = ImageIO.FlattenOnWhiteIfTransparent(src);
            EncodeAndSave(flat, outputPath, "JPG", 92);
        }

        public static int ExtractGifFrames(string inputPath, string outputDir)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("GIF file not found", inputPath);
            }

            Directory.CreateDirectory(outputDir);
            using var stream = File.OpenRead(inputPath);
            var decoder = new GifBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            int count = decoder.Frames.Count;
            if (count == 0)
            {
                throw new InvalidOperationException("Could not decode GIF.");
            }

            for (int i = 0; i < count; i++)
            {
                string outPath = Path.Combine(outputDir, $"frame_{i + 1:D3}.png");
                EncodeAndSave(decoder.Frames[i], outPath, "PNG", 100);
            }

            return count;
        }

        public static string ToBase64DataUri(byte[] bytes, string mimeType = "image/png") =>
            $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";

        public static string ToBase64DataUri(string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("Image file not found", inputPath);
            }

            string mime = Path.GetExtension(inputPath).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".ico" => "image/x-icon",
                _ => "application/octet-stream"
            };

            return ToBase64DataUri(File.ReadAllBytes(inputPath), mime);
        }

        public static BitmapSource LoadWpfBitmap(string inputPath) =>
            ImageIO.LoadFileBitmap(inputPath);

        private static BitmapSource EnsureBgra32(BitmapSource source)
        {
            if (source.Format == PixelFormats.Bgra32)
            {
                return source;
            }
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            return converted;
        }

        private static void EncodeAndSave(BitmapSource bitmap, string outputPath, string formatName, int quality)
        {
            EnsureParentDir(outputPath);
            byte[] encoded = formatName switch
            {
                "JPG" or "JPEG" or "JFIF" => ImageIO.EncodeJpeg(bitmap, quality),
                "WEBP" => ImageIO.EncodeWebP(bitmap, quality),
                "BMP" => ImageIO.EncodeBmp(bitmap),
                _ => ImageIO.EncodePng(bitmap)
            };
            File.WriteAllBytes(outputPath, encoded);
        }

        private static void EnsureParentDir(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static string FormatFromPath(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" or ".jfif" => "JPG",
                ".webp" => "WEBP",
                ".bmp" => "BMP",
                _ => "PNG"
            };

        private static void SaveAsIco(BitmapSource source, string outputPath)
        {
            int iconSize = Math.Min(256, Math.Max(source.PixelWidth, source.PixelHeight));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                double scale = Math.Min(iconSize / (double)source.PixelWidth, iconSize / (double)source.PixelHeight);
                double targetW = source.PixelWidth * scale;
                double targetH = source.PixelHeight * scale;
                double left = (iconSize - targetW) / 2.0;
                double top = (iconSize - targetH) / 2.0;
                dc.DrawImage(source, new Rect(left, top, targetW, targetH));
            }

            var resized = new RenderTargetBitmap(iconSize, iconSize, 96, 96, PixelFormats.Pbgra32);
            resized.Render(visual);
            resized.Freeze();
            byte[] pngBytes = ImageIO.EncodePng(resized);

            using var fs = File.Create(outputPath);
            using var bw = new BinaryWriter(fs);

            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)1);

            byte wByte = iconSize >= 256 ? (byte)0 : (byte)iconSize;
            byte hByte = iconSize >= 256 ? (byte)0 : (byte)iconSize;
            bw.Write(wByte);
            bw.Write(hByte);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write((uint)pngBytes.Length);
            bw.Write((uint)22);

            bw.Write(pngBytes);
        }
    }
}
