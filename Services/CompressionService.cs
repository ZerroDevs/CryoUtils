using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace CryoUtils.Services
{
    public sealed class CompressionResult
    {
        public byte[] Data { get; init; } = Array.Empty<byte>();
        public string Extension { get; init; } = ".png";
        public long OriginalBytes { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public BitmapSource? Preview { get; init; }
        public long CompressedBytes => Data.LongLength;
        public double SavedPercent => OriginalBytes <= 0
            ? 0
            : (1.0 - CompressedBytes / (double)OriginalBytes) * 100;
    }

    public static class CompressionService
    {
        public static Task<CompressionResult> CompressAsync(
            string inputPath,
            string preset,
            int quality,
            int scalePercent)
        {
            return Task.Run(() => Compress(inputPath, preset, quality, scalePercent));
        }

        private static CompressionResult Compress(
            string inputPath,
            string preset,
            int quality,
            int scalePercent)
        {
            string extension = Path.GetExtension(inputPath).ToLowerInvariant();
            long originalBytes = new FileInfo(inputPath).Length;
            int baseQuality = Math.Clamp(quality, 20, 100);
            int baseScale = Math.Clamp(scalePercent, 20, 100);
            long? targetBytes = preset switch
            {
                "Discord" => 10L * 1024 * 1024,
                "WhatsApp" => 1536L * 1024,
                _ => null
            };

            if (preset == "WhatsApp")
            {
                baseQuality = Math.Min(baseQuality, 72);
                baseScale = Math.Min(baseScale, 82);
            }
            else if (preset == "Discord")
            {
                baseQuality = Math.Min(baseQuality, 88);
            }

            if (string.Equals(extension, ".gif", StringComparison.Ordinal))
            {
                return CompressGif(inputPath, originalBytes, baseScale, targetBytes);
            }

            return CompressRaster(inputPath, extension, originalBytes, baseQuality, baseScale, targetBytes);
        }

        private static CompressionResult CompressRaster(
            string inputPath,
            string extension,
            long originalBytes,
            int quality,
            int scalePercent,
            long? targetBytes)
        {
            using var source = SKBitmap.Decode(inputPath);
            if (source == null)
            {
                throw new InvalidOperationException("Unsupported image.");
            }
            byte[] original = File.ReadAllBytes(inputPath);

            string outputExtension = extension switch
            {
                ".jpg" or ".jpeg" => ".jpg",
                ".webp" => ".webp",
                _ => ".png"
            };

            byte[] best = Array.Empty<byte>();
            int bestWidth = source.Width;
            int bestHeight = source.Height;
            double scale = scalePercent / 100.0;
            int attemptQuality = quality;

            for (int attempt = 0; attempt < 7; attempt++)
            {
                int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                using var resized = width == source.Width && height == source.Height
                    ? null
                    : source.Resize(new SKImageInfo(width, height), SKFilterQuality.High);
                using var image = SKImage.FromBitmap(resized ?? source);
                using var data = image.Encode(
                    outputExtension == ".jpg"
                        ? SKEncodedImageFormat.Jpeg
                        : outputExtension == ".webp"
                            ? SKEncodedImageFormat.Webp
                            : SKEncodedImageFormat.Png,
                    outputExtension == ".png" ? 100 : attemptQuality);
                best = data.ToArray();
                bestWidth = width;
                bestHeight = height;

                if (targetBytes == null || best.LongLength <= targetBytes.Value)
                {
                    break;
                }

                attemptQuality = Math.Max(25, attemptQuality - 12);
                scale = Math.Max(0.25, scale * 0.86);
            }

            if (best.LongLength >= original.LongLength)
            {
                best = original;
                bestWidth = source.Width;
                bestHeight = source.Height;
            }

            var preview = TryPreview(best);
            return new CompressionResult
            {
                Data = best,
                Extension = outputExtension,
                OriginalBytes = originalBytes,
                Width = bestWidth,
                Height = bestHeight,
                Preview = preview
            };
        }

        private static CompressionResult CompressGif(
            string inputPath,
            long originalBytes,
            int scalePercent,
            long? targetBytes)
        {
            using var stream = File.OpenRead(inputPath);
            var decoder = new GifBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                throw new InvalidOperationException("Empty GIF.");
            }

            byte[] original = File.ReadAllBytes(inputPath);
            int fps = ReadGifFps(decoder.Frames[0]);
            byte[] best = Array.Empty<byte>();
            int width = decoder.Frames[0].PixelWidth;
            int height = decoder.Frames[0].PixelHeight;
            double scale = Math.Clamp(scalePercent / 100.0, 0.2, 1.0);

            for (int attempt = 0; attempt < 6; attempt++)
            {
                var frames = new List<BitmapSource>(decoder.Frames.Count);
                foreach (var frame in decoder.Frames)
                {
                    if (Math.Abs(scale - 1) < 0.001)
                    {
                        frames.Add(frame);
                    }
                    else
                    {
                        var resized = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                        resized.Freeze();
                        frames.Add(resized);
                    }
                }

                width = frames[0].PixelWidth;
                height = frames[0].PixelHeight;
                using var output = new MemoryStream();
                using (var writer = new GifWriter(output, width, height, fps, frames))
                {
                    foreach (var frame in frames)
                    {
                        writer.AddFrame(frame);
                    }
                    writer.Finish();
                }
                best = output.ToArray();
                if (targetBytes == null || best.LongLength <= targetBytes.Value)
                {
                    break;
                }
                scale = Math.Max(0.2, scale * 0.82);
            }

            if (best.LongLength >= original.LongLength)
            {
                best = original;
                width = decoder.Frames[0].PixelWidth;
                height = decoder.Frames[0].PixelHeight;
            }

            return new CompressionResult
            {
                Data = best,
                Extension = ".gif",
                OriginalBytes = originalBytes,
                Width = width,
                Height = height,
                Preview = TryPreview(best)
            };
        }

        private static int ReadGifFps(BitmapSource frame)
        {
            try
            {
                if (frame.Metadata is BitmapMetadata metadata)
                {
                    object? delay = metadata.GetQuery("/grctlext/Delay");
                    if (delay is ushort hundredths && hundredths > 0)
                    {
                        return Math.Clamp((int)Math.Round(100.0 / hundredths), 5, 30);
                    }
                }
            }
            catch
            {
            }
            return 12;
        }

        private static BitmapSource? TryPreview(byte[] data)
        {
            try
            {
                return ImageIO.DecodeBytes(data);
            }
            catch
            {
                return null;
            }
        }
    }
}
