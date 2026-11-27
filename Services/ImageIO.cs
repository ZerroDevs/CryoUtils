using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
                case ".jfif":
                    encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                    bitmap = FlattenOnWhiteIfTransparent(bitmap);
                    break;
                case ".webp":
                    SaveWebP(bitmap, path, 92);
                    return;
                case ".gif":
                    encoder = new GifBitmapEncoder();
                    break;
                case ".bmp":
                    encoder = new BmpBitmapEncoder();
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

        public static byte[] EncodeWebP(BitmapSource bitmap, int quality) =>
            SimpleWebpEncoder.Encode(bitmap);

        public static byte[] EncodePng(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        public static byte[] EncodeJpeg(BitmapSource bitmap, int quality = 92)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
            encoder.Frames.Add(BitmapFrame.Create(FlattenOnWhiteIfTransparent(bitmap)));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        public static byte[] EncodeBmp(BitmapSource bitmap)
        {
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        public static BitmapSource FlattenOnWhiteIfTransparent(BitmapSource source)
        {
            var fmt = source.Format;
            if (fmt == PixelFormats.Bgr24 || fmt == PixelFormats.Bgr32 || fmt == PixelFormats.Gray8)
            {
                return source;
            }

            int w = Math.Max(1, source.PixelWidth);
            int h = Math.Max(1, source.PixelHeight);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawImage(source, new Rect(0, 0, w, h));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
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

        public static BitmapSource LoadFileBitmap(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Image file not found", path);
            }

            byte[] bytes = File.ReadAllBytes(path);
            return DecodeBytes(bytes);
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

            int w = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
            int h = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
            return ResizeToExact(source, w, h);
        }

        public static BitmapSource ResizeToExact(BitmapSource source, int width, int height)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            if (w == source.PixelWidth && h == source.PixelHeight)
            {
                return source;
            }

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                dc.DrawImage(source, new Rect(0, 0, w, h));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        public static BitmapSource LoadThumbnail(string path, int maxWidth = 180)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = Math.Max(1, maxWidth);
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        public static BitmapSource CreateVideoThumbnailPlaceholder(int width = 180, int height = 102)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bg = new SolidColorBrush(Color.FromRgb(18, 22, 24));
                bg.Freeze();
                dc.DrawRectangle(bg, null, new Rect(0, 0, width, height));

                double cx = width / 2.0;
                double cy = height / 2.0;
                var triangle = new StreamGeometry();
                using (var ctx = triangle.Open())
                {
                    ctx.BeginFigure(new Point(cx - 10, cy - 12), isFilled: true, isClosed: true);
                    ctx.LineTo(new Point(cx + 14, cy), isStroked: false, isSmoothJoin: false);
                    ctx.LineTo(new Point(cx - 10, cy + 12), isStroked: false, isSmoothJoin: false);
                }
                triangle.Freeze();

                var accent = new SolidColorBrush(Color.FromRgb(43, 136, 216));
                accent.Freeze();
                dc.DrawGeometry(accent, null, triangle);
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
    }
}
