using System;
using System.IO;
using System.Windows.Media.Imaging;
using SkiaSharp;

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

            using var bitmap = DecodeSkBitmap(inputPath);
            string fmt = targetFormat.TrimStart('.').ToUpperInvariant();
            if (fmt == "ICO")
            {
                EnsureParentDir(outputPath);
                SaveAsIco(bitmap, outputPath);
                return;
            }

            var skFormat = FormatFromName(fmt);
            int quality = fmt is "JPG" or "JPEG" or "JFIF" or "WEBP" ? 92 : 100;
            EncodeAndSave(bitmap, outputPath, skFormat, quality);
        }

        public static void StripExif(string inputPath, string outputPath)
        {
            using var bitmap = DecodeSkBitmap(inputPath);
            var format = FormatFromPath(inputPath);
            EncodeAndSave(bitmap, outputPath, format, 92);
        }

        public static void RemoveBackground(string inputPath, string outputPath, byte tolerance = 35)
        {
            using var srcBitmap = DecodeSkBitmap(inputPath);
            using var bitmap = ToRgbaBitmap(srcBitmap);

            int w = bitmap.Width;
            int h = bitmap.Height;

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

            EncodeAndSave(bitmap, outputPath, SKEncodedImageFormat.Png, 100);
        }

        public static (int Width, int Height) ResizePreset(string inputPath, string outputPath, string preset)
        {
            using var source = DecodeSkBitmap(inputPath);
            int targetW = source.Width;
            int targetH = source.Height;

            switch (preset.ToLowerInvariant())
            {
                case "50pct":
                    targetW = Math.Max(1, source.Width / 2);
                    targetH = Math.Max(1, source.Height / 2);
                    break;
                case "w1920":
                    if (source.Width > 1920)
                    {
                        targetW = 1920;
                        targetH = Math.Max(1, (int)Math.Round(source.Height * (1920.0 / source.Width)));
                    }
                    break;
                case "s512":
                    int maxSide = Math.Max(source.Width, source.Height);
                    if (maxSide > 512)
                    {
                        double scale = 512.0 / maxSide;
                        targetW = Math.Max(1, (int)Math.Round(source.Width * scale));
                        targetH = Math.Max(1, (int)Math.Round(source.Height * scale));
                    }
                    break;
            }

            using var resized = (targetW == source.Width && targetH == source.Height)
                ? null
                : source.Resize(new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.High);

            var target = resized ?? source;
            if (Path.GetExtension(outputPath).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                EnsureParentDir(outputPath);
                SaveAsIco(target, outputPath);
            }
            else
            {
                EncodeAndSave(target, outputPath, FormatFromPath(outputPath), 92);
            }

            return (target.Width, target.Height);
        }

        public static (int Width, int Height) TrimTransparentMargins(string inputPath, string outputPath)
        {
            using var srcBitmap = DecodeSkBitmap(inputPath);
            using var bitmap = ToRgbaBitmap(srcBitmap);

            int w = bitmap.Width;
            int h = bitmap.Height;
            int minX = w, minY = h, maxX = -1, maxY = -1;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (bitmap.GetPixel(x, y).Alpha > 0)
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
                EncodeAndSave(bitmap, outputPath, SKEncodedImageFormat.Png, 100);
                return (w, h);
            }

            int cropW = maxX - minX + 1;
            int cropH = maxY - minY + 1;
            using var cropped = new SKBitmap(cropW, cropH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(cropped))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(
                    bitmap,
                    new SKRect(minX, minY, maxX + 1, maxY + 1),
                    new SKRect(0, 0, cropW, cropH));
            }

            EncodeAndSave(cropped, outputPath, SKEncodedImageFormat.Png, 100);
            return (cropW, cropH);
        }

        public static void FlattenWhite(string inputPath, string outputPath)
        {
            using var src = DecodeSkBitmap(inputPath);
            using var flat = new SKBitmap(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(flat))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(src, 0, 0);
            }

            EncodeAndSave(flat, outputPath, SKEncodedImageFormat.Jpeg, 92);
        }

        public static int ExtractGifFrames(string inputPath, string outputDir)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("GIF file not found", inputPath);
            }

            Directory.CreateDirectory(outputDir);
            using var stream = File.OpenRead(inputPath);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidOperationException("Could not decode GIF.");

            int frameCount = Math.Max(1, codec.FrameCount);
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var frameBuffer = new SKBitmap(info);
            using var canvasBitmap = new SKBitmap(info);
            using var canvas = new SKCanvas(canvasBitmap);
            canvas.Clear(SKColors.Transparent);

            for (int i = 0; i < frameCount; i++)
            {
                var opts = codec.FrameCount > 0 ? new SKCodecOptions(i) : new SKCodecOptions();
                var result = codec.GetPixels(info, frameBuffer.GetPixels(), opts);
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    continue;
                }

                bool resetCanvas = i == 0;
                if (codec.FrameCount > 0 && i < codec.FrameInfo.Length)
                {
                    resetCanvas = codec.FrameInfo[i].RequiredFrame == -1;
                }

                if (resetCanvas)
                {
                    canvas.Clear(SKColors.Transparent);
                }

                canvas.DrawBitmap(frameBuffer, 0, 0);
                string outPath = Path.Combine(outputDir, $"frame_{i + 1:D3}.png");
                EncodeAndSave(canvasBitmap, outPath, SKEncodedImageFormat.Png, 100);
            }

            return frameCount;
        }

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

            byte[] bytes = File.ReadAllBytes(inputPath);
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }

        public static BitmapSource LoadWpfBitmap(string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("Image file not found", inputPath);
            }

            byte[] bytes = File.ReadAllBytes(inputPath);
            try
            {
                return ImageIO.DecodeBytes(bytes);
            }
            catch
            {
                // Fallback via SkiaSharp (handles WebP or exotic bitmaps reliably)
                using var skBitmap = SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("Unsupported image format.");
                using var img = SKImage.FromBitmap(skBitmap);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                return ImageIO.DecodeBytes(data.ToArray());
            }
        }

        private static SKBitmap DecodeSkBitmap(string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                throw new FileNotFoundException("Image file not found", inputPath);
            }

            using var inStream = File.OpenRead(inputPath);
            return SKBitmap.Decode(inStream) ?? throw new InvalidOperationException("Could not decode image format");
        }

        private static SKBitmap ToRgbaBitmap(SKBitmap source)
        {
            var rgba = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(rgba);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0);
            return rgba;
        }

        private static void EncodeAndSave(SKBitmap bitmap, string outputPath, SKEncodedImageFormat format, int quality)
        {
            EnsureParentDir(outputPath);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(format, quality);
            using var outFs = File.Create(outputPath);
            data.SaveTo(outFs);
        }

        private static void EnsureParentDir(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static SKEncodedImageFormat FormatFromName(string fmt) => fmt switch
        {
            "PNG" => SKEncodedImageFormat.Png,
            "WEBP" => SKEncodedImageFormat.Webp,
            "BMP" => SKEncodedImageFormat.Bmp,
            "JFIF" or "JPG" or "JPEG" => SKEncodedImageFormat.Jpeg,
            _ => SKEncodedImageFormat.Png
        };

        private static SKEncodedImageFormat FormatFromPath(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" or ".jfif" => SKEncodedImageFormat.Jpeg,
                ".webp" => SKEncodedImageFormat.Webp,
                ".bmp" => SKEncodedImageFormat.Bmp,
                _ => SKEncodedImageFormat.Png
            };

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
