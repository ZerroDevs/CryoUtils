using System;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public static class OcrService
    {
        public static async Task<string> RecognizeAsync(BitmapSource bitmap)
        {
            var source = bitmap;
            int maxDimension = (int)WinRtBridge.GetOcrMaxImageDimension();
            if (source.PixelWidth > maxDimension || source.PixelHeight > maxDimension)
            {
                double scale = Math.Min(
                    maxDimension / (double)source.PixelWidth,
                    maxDimension / (double)source.PixelHeight);
                var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                resized.Freeze();
                source = resized;
            }

            int width = source.PixelWidth;
            int height = source.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            var converted = source.Format == PixelFormats.Pbgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            converted.CopyPixels(pixels, stride, 0);

            return await WinRtBridge.RecognizeOcrAsync(pixels, width, height).ConfigureAwait(false);
        }
    }
}
