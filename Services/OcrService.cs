using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace CryoUtils.Services
{
    public static class OcrService
    {
        public static async Task<string> RecognizeAsync(BitmapSource bitmap)
        {
            var source = bitmap;
            int maxDimension = (int)OcrEngine.MaxImageDimension;
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
            var converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(pixels, stride, 0);

            using var writer = new DataWriter();
            writer.WriteBytes(pixels);
            IBuffer buffer = writer.DetachBuffer();
            using var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
                buffer,
                BitmapPixelFormat.Bgra8,
                width,
                height,
                BitmapAlphaMode.Premultiplied);

            var engines = new List<OcrEngine>();
            foreach (var language in OcrEngine.AvailableRecognizerLanguages)
            {
                try
                {
                    var engine = OcrEngine.TryCreateFromLanguage(language);
                    if (engine != null)
                    {
                        engines.Add(engine);
                    }
                }
                catch
                {
                }
            }

            if (engines.Count == 0)
            {
                var fallback = OcrEngine.TryCreateFromUserProfileLanguages();
                if (fallback != null)
                {
                    engines.Add(fallback);
                }
            }

            if (engines.Count == 0)
            {
                throw new InvalidOperationException("No Windows OCR language pack is installed.");
            }

            string best = "";
            foreach (var engine in engines)
            {
                try
                {
                    var result = await engine.RecognizeAsync(softwareBitmap);
                    string candidate = result.Lines.Count > 0
                        ? string.Join(Environment.NewLine, result.Lines.Select(line => line.Text))
                        : result.Text;
                    if (candidate.Length > best.Length)
                    {
                        best = candidate;
                    }
                }
                catch
                {
                }
            }

            return best.Trim();
        }
    }
}
