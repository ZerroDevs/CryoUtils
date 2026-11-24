using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace CryoUtils.Services
{
    public static class ImageIO
    {
        public static void SaveBitmap(BitmapSource bitmap, string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            BitmapEncoder encoder;
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                    break;
                case ".webp":
                    SaveWebP(bitmap, path, 92);
                    return;
                case ".gif":
                    encoder = new GifBitmapEncoder();
                    break;
                default:
                    encoder = new PngBitmapEncoder();
                    break;
            }

            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        public static string ExtensionForFormat(string? format)
        {
            return format switch
            {
                "JPG" => ".jpg",
                "WEBP" => ".webp",
                "GIF" => ".gif",
                "MP4" => ".mp4",
                _ => ".png"
            };
        }

        public static void SaveWebP(BitmapSource bitmap, string path, int quality)
        {
            byte[] data = EncodeWebP(bitmap, quality);
            File.WriteAllBytes(path, data);
        }

        public static byte[] EncodeWebP(BitmapSource bitmap, int quality)
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            var converted = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(pixels, stride, 0);

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var skBitmap = new SKBitmap(info);
            System.Runtime.InteropServices.Marshal.Copy(
                pixels, 0, skBitmap.GetPixels(), pixels.Length);
            using var image = SKImage.FromBitmap(skBitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, Math.Clamp(quality, 1, 100));
            return encoded.ToArray();
        }

        public static byte[] EncodePng(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        public static BitmapSource DecodeBytes(byte[] data)
        {
            using var stream = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }
            if (bytes < 1024 * 1024)
            {
                return $"{bytes / 1024.0:0.#} KB";
            }
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }

        public static BitmapSource Resize(BitmapSource source, double scale)
        {
            if (Math.Abs(scale - 1) < 0.001)
            {
                return source;
            }

            var transformed = new TransformedBitmap(
                source,
                new ScaleTransform(scale, scale));
            transformed.Freeze();
            return transformed;
        }
    }
}
