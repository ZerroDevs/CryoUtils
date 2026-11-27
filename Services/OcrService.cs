using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public sealed record OcrLanguageOption(string Tag, string DisplayName);

    public static class OcrService
    {
        public static IReadOnlyList<OcrLanguageOption> GetAvailableLanguages()
        {
            var list = new List<OcrLanguageOption>();
            foreach (var item in WinRtBridge.GetAvailableOcrLanguages())
            {
                list.Add(new OcrLanguageOption(item.Tag, item.DisplayName));
            }
            return list;
        }

        private static readonly System.Text.RegularExpressions.Regex EmailRegex =
            new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex SecretTokenRegex =
            new(@"(?:sk-[A-Za-z0-9_\-]{12,}|sk_live_[A-Za-z0-9]{12,}|gh[pousr]_[A-Za-z0-9]{12,}|github_pat_[A-Za-z0-9_]{20,}|AIza[0-9A-Za-z\-_]{16,}|AKIA[0-9A-Z]{16}|eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}(?:\.[A-Za-z0-9_\-]*)?|xox[baprs]-[A-Za-z0-9\-]{10,})", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex SsnRegex =
            new(@"^\d{3}-\d{2}-\d{4}$", System.Text.RegularExpressions.RegexOptions.Compiled);

        public static async Task<string> RecognizeAsync(BitmapSource bitmap, string? preferredLanguageTag = null)
        {
            byte[] pixels = PrepareOcrPixels(bitmap, out int width, out int height, out _);
            string? effectiveTag = preferredLanguageTag ?? SettingsService.Instance.Current.OcrLanguageTag;
            return await WinRtBridge.RecognizeOcrAsync(pixels, width, height, effectiveTag).ConfigureAwait(false);
        }

        internal static async Task<IReadOnlyList<WinRtBridge.OcrWordBox>> RecognizeWordsAsync(
            BitmapSource bitmap,
            string? preferredLanguageTag = null)
        {
            byte[] pixels = PrepareOcrPixels(bitmap, out int width, out int height, out double scale);
            string? effectiveTag = preferredLanguageTag ?? SettingsService.Instance.Current.OcrLanguageTag;
            var rawWords = await WinRtBridge.RecognizeOcrWordsAsync(pixels, width, height, effectiveTag).ConfigureAwait(false);
            if (Math.Abs(scale - 1.0) < 0.0001)
            {
                return rawWords;
            }

            double invScale = 1.0 / scale;
            var scaled = new List<WinRtBridge.OcrWordBox>(rawWords.Count);
            foreach (var word in rawWords)
            {
                var r = word.Bounds;
                scaled.Add(new WinRtBridge.OcrWordBox(
                    word.Text,
                    new System.Windows.Rect(r.X * invScale, r.Y * invScale, r.Width * invScale, r.Height * invScale)));
            }
            return scaled;
        }

        internal static IReadOnlyList<System.Windows.Rect> FindSensitiveBounds(IReadOnlyList<WinRtBridge.OcrWordBox> words)
        {
            var results = new List<System.Windows.Rect>();
            if (words.Count == 0)
            {
                return results;
            }

            for (int i = 0; i < words.Count; i++)
            {
                string raw = words[i].Text.Trim();
                if (raw.Length == 0)
                {
                    continue;
                }

                string trimmed = raw.Trim(',', ';', '.', ':', '(', ')', '[', ']', '<', '>', '"', '\'');
                if (IsSensitiveSingleToken(trimmed))
                {
                    results.Add(PadSensitiveRect(words[i].Bounds));
                }

                if ((string.Equals(trimmed, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(trimmed, "Token", StringComparison.OrdinalIgnoreCase)) &&
                    i + 1 < words.Count &&
                    SameOcrRow(words[i].Bounds, words[i + 1].Bounds) &&
                    words[i + 1].Text.Trim().Length >= 8)
                {
                    results.Add(PadSensitiveRect(words[i + 1].Bounds));
                }

                if (IsNumericSequenceChunk(raw, out int startDigits))
                {
                    int totalDigits = startDigits;
                    var combined = words[i].Bounds;
                    int endIndex = i;
                    for (int j = i + 1; j < words.Count && j <= i + 4; j++)
                    {
                        if (!SameOcrRow(words[j - 1].Bounds, words[j].Bounds))
                        {
                            break;
                        }
                        double gap = words[j].Bounds.Left - words[j - 1].Bounds.Right;
                        if (gap < -4 || gap > Math.Max(14, words[i].Bounds.Height * 2.5))
                        {
                            break;
                        }
                        if (!IsNumericSequenceChunk(words[j].Text.Trim(), out int moreDigits))
                        {
                            break;
                        }

                        totalDigits += moreDigits;
                        combined.Union(words[j].Bounds);
                        endIndex = j;
                        if (totalDigits >= 10 && totalDigits <= 19)
                        {
                            results.Add(PadSensitiveRect(combined));
                            i = endIndex;
                            break;
                        }
                    }
                }
            }

            return results;
        }

        private static bool SameOcrRow(System.Windows.Rect a, System.Windows.Rect b)
        {
            double tol = Math.Max(6, Math.Max(a.Height, b.Height) * 0.65);
            return Math.Abs((a.Top + a.Height * 0.5) - (b.Top + b.Height * 0.5)) <= tol;
        }

        private static System.Windows.Rect PadSensitiveRect(System.Windows.Rect rect)
        {
            rect.Inflate(3, 2);
            return rect;
        }

        private static bool IsSensitiveSingleToken(string token)
        {
            if (token.Length < 5)
            {
                return false;
            }

            if (EmailRegex.IsMatch(token) || SecretTokenRegex.IsMatch(token) || SsnRegex.IsMatch(token))
            {
                return true;
            }

            if (IsSingleTokenPhoneOrCard(token))
            {
                return true;
            }

            if (token.Length >= 28 && IsOpaqueSecretBlob(token))
            {
                return true;
            }

            return false;
        }

        private static bool IsSingleTokenPhoneOrCard(string token)
        {
            int digits = 0;
            bool hasSeparator = false;
            foreach (char ch in token)
            {
                if (ch is >= '0' and <= '9')
                {
                    digits++;
                }
                else if (ch is '+' or '-' or '(' or ')' or '.')
                {
                    hasSeparator = true;
                }
                else
                {
                    return false;
                }
            }

            return (digits >= 13 && digits <= 19) ||
                   (digits >= 10 && digits <= 15 && (hasSeparator || token.StartsWith('+')));
        }

        private static bool IsNumericSequenceChunk(string token, out int digitCount)
        {
            digitCount = 0;
            foreach (char ch in token)
            {
                if (ch is >= '0' and <= '9')
                {
                    digitCount++;
                }
                else if (ch is not ('+' or '-' or '(' or ')'))
                {
                    return false;
                }
            }

            return digitCount >= 2;
        }

        private static bool IsOpaqueSecretBlob(string token)
        {
            int letters = 0;
            int digits = 0;
            foreach (char ch in token)
            {
                if (char.IsLetter(ch))
                {
                    letters++;
                }
                else if (ch is >= '0' and <= '9')
                {
                    digits++;
                }
                else if (ch is not ('_' or '-' or '+' or '/' or '=' or '.'))
                {
                    return false;
                }
            }

            return letters >= 4 && digits >= 4;
        }

        private static byte[] PrepareOcrPixels(
            BitmapSource bitmap,
            out int width,
            out int height,
            out double scale)
        {
            var source = bitmap;
            scale = 1.0;
            int maxDimension = (int)WinRtBridge.GetOcrMaxImageDimension();
            if (source.PixelWidth > maxDimension || source.PixelHeight > maxDimension)
            {
                scale = Math.Min(
                    maxDimension / (double)source.PixelWidth,
                    maxDimension / (double)source.PixelHeight);
                var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                resized.Freeze();
                source = resized;
            }

            width = source.PixelWidth;
            height = source.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            var converted = source.Format == PixelFormats.Pbgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            converted.CopyPixels(pixels, stride, 0);
            return pixels;
        }
    }
}
