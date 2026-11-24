using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace CryoUtils.Services
{
    public sealed record QrDecodeResult(string Text, string FormatName, int Width, int Height);

    public static class QrCodeService
    {
        public static QrDecodeResult? Decode(BitmapSource bitmap)
        {
            try
            {
                var converted = bitmap.Format == PixelFormats.Bgra32
                    ? bitmap
                    : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);

                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                var luminance = new RGBLuminanceSource(
                    pixels,
                    width,
                    height,
                    RGBLuminanceSource.BitmapFormat.BGRA32);

                var binarizer = new HybridBinarizer(luminance);
                var binaryBitmap = new BinaryBitmap(binarizer);

                var hints = new Dictionary<DecodeHintType, object>
                {
                    { DecodeHintType.TRY_HARDER, true },
                    { DecodeHintType.POSSIBLE_FORMATS, new List<BarcodeFormat> { BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX } }
                };

                var reader = new MultiFormatReader();
                var result = reader.decode(binaryBitmap, hints);

                if (result == null)
                {
                    // Fallback attempt without hints
                    result = reader.decode(binaryBitmap);
                }

                if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                {
                    return new QrDecodeResult(
                        result.Text,
                        result.BarcodeFormat.ToString(),
                        width,
                        height);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public static BitmapSource Encode(
            string text,
            int size = 256,
            ErrorCorrectionLevel? level = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Text cannot be empty.", nameof(text));
            }

            int targetSize = Math.Clamp(size, 64, 2048);
            var ecLevel = level ?? ErrorCorrectionLevel.M;

            var writer = new BarcodeWriterGeneric
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Width = targetSize,
                    Height = targetSize,
                    Margin = 1,
                    CharacterSet = "UTF-8",
                    ErrorCorrection = ecLevel
                }
            };

            BitMatrix matrix = writer.Encode(text);
            int width = matrix.Width;
            int height = matrix.Height;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    byte color = matrix[x, y] ? (byte)0 : (byte)255;
                    int index = row + x * 4;
                    pixels[index] = color;         // B
                    pixels[index + 1] = color;     // G
                    pixels[index + 2] = color;     // R
                    pixels[index + 3] = 255;       // A
                }
            }

            var output = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);
            output.Freeze();
            return output;
        }
    }
}
