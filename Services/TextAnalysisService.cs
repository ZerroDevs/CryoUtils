using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace CryoUtils.Services
{
    public sealed record TextColorCode(string Text, Color Color);

    public static class TextAnalysisService
    {
        private static readonly Regex HexPattern = new(
            @"(?<![\w#])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})\b",
            RegexOptions.Compiled);

        private static readonly Regex RgbPattern = new(
            @"\b(?:rgb|rgba)\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*,\s*(\d{1,3}|0?\.\d+))?\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HslPattern = new(
            @"\b(?:hsl|hsla)\(\s*(-?\d{1,3}(?:\.\d+)?)\s*,\s*(\d{1,3}(?:\.\d+)?)%\s*,\s*(\d{1,3}(?:\.\d+)?)%(?:\s*,\s*(\d{1,3}|0?\.\d+))?\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex CodeLinePattern = new(
            @"(?m)^\s*(?:public|private|protected|internal|class|namespace|using|import|from|def|function|const|let|var|#include|SELECT|INSERT|UPDATE|DELETE|CREATE)\b",
            RegexOptions.Compiled);

        private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".c", ".cc", ".cpp", ".cs", ".css", ".dart", ".go", ".h", ".hpp",
            ".html", ".htm", ".ini", ".java", ".js", ".jsx", ".json", ".kt",
            ".lua", ".php", ".ps1", ".py", ".r", ".rb", ".rs", ".sh", ".sql",
            ".swift", ".toml", ".ts", ".tsx", ".vue", ".xml", ".yaml", ".yml"
        };

        public static bool LooksLikeCode(string? text, string? filePath)
        {
            string source = text ?? "";
            string extension = string.IsNullOrWhiteSpace(filePath)
                ? ""
                : Path.GetExtension(filePath);
            if (CodeExtensions.Contains(extension))
            {
                return true;
            }
            if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int score = 0;
            if (CodeLinePattern.IsMatch(source))
            {
                score += 2;
            }
            if (source.Contains("=>", StringComparison.Ordinal) ||
                source.Contains("</", StringComparison.Ordinal))
            {
                score++;
            }
            if (source.Contains('{') &&
                source.Contains('}') &&
                source.Contains(';'))
            {
                score++;
            }
            return score >= 2;
        }

        public static IReadOnlyList<TextColorCode> FindColorCodes(string? text)
        {
            string source = text ?? "";
            var results = new List<TextColorCode>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in HexPattern.Matches(source))
            {
                if (TryParseHex(match.Value, out Color color))
                {
                    Add(match.Value, color);
                }
            }

            foreach (Match match in RgbPattern.Matches(source))
            {
                if (TryParseRgb(match, out Color color))
                {
                    Add(match.Value, color);
                }
            }

            foreach (Match match in HslPattern.Matches(source))
            {
                if (TryParseHsl(match, out Color color))
                {
                    Add(match.Value, color);
                }
            }

            return results;

            void Add(string token, Color color)
            {
                string normalized = token.Trim();
                if (normalized.Length == 0 || !seen.Add(normalized))
                {
                    return;
                }
                results.Add(new TextColorCode(normalized, color));
            }
        }

        private static bool TryParseHex(string token, out Color color)
        {
            color = Colors.Transparent;
            string value = token.TrimStart('#');
            if (value.Length is not (3 or 4 or 6 or 8) ||
                !value.All(Uri.IsHexDigit))
            {
                return false;
            }

            if (value.Length is 3 or 4)
            {
                value = string.Concat(value.Select(character => new string(character, 2)));
            }

            byte alpha = 255;
            int offset = 0;
            if (value.Length == 8)
            {
                alpha = byte.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                offset = 2;
            }

            byte red = byte.Parse(value.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte green = byte.Parse(value.Substring(offset + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte blue = byte.Parse(value.Substring(offset + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            color = Color.FromArgb(alpha, red, green, blue);
            return true;
        }

        private static bool TryParseRgb(Match match, out Color color)
        {
            color = Colors.Transparent;
            if (!TryByte(match.Groups[1].Value, out byte red) ||
                !TryByte(match.Groups[2].Value, out byte green) ||
                !TryByte(match.Groups[3].Value, out byte blue))
            {
                return false;
            }

            byte alpha = 255;
            if (match.Groups[4].Success && !TryAlpha(match.Groups[4].Value, out alpha))
            {
                return false;
            }

            color = Color.FromArgb(alpha, red, green, blue);
            return true;
        }

        private static bool TryParseHsl(Match match, out Color color)
        {
            color = Colors.Transparent;
            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hue) ||
                !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double saturation) ||
                !double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lightness))
            {
                return false;
            }

            if (saturation is < 0 or > 100 || lightness is < 0 or > 100)
            {
                return false;
            }

            byte alpha = 255;
            if (match.Groups[4].Success && !TryAlpha(match.Groups[4].Value, out alpha))
            {
                return false;
            }

            hue = ((hue % 360) + 360) % 360;
            double s = saturation / 100;
            double l = lightness / 100;
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
            double m = l - c / 2;
            (double r, double g, double b) = hue switch
            {
                < 60 => (c, x, 0d),
                < 120 => (x, c, 0d),
                < 180 => (0d, c, x),
                < 240 => (0d, x, c),
                < 300 => (x, 0d, c),
                _ => (c, 0d, x)
            };
            color = Color.FromArgb(
                alpha,
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
            return true;
        }

        private static bool TryByte(string value, out byte result)
        {
            return byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) &&
                   result <= 255;
        }

        private static bool TryAlpha(string value, out byte result)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                result = 255;
                return false;
            }

            // CSS alpha is a 0..1 fraction, including a bare 1. Larger values are 0..255 bytes.
            if (number is >= 0 and <= 1)
            {
                result = (byte)Math.Round(number * 255);
                return true;
            }

            if (number is > 1 and <= 255 && value.IndexOf('.') < 0)
            {
                result = (byte)number;
                return true;
            }

            result = 255;
            return false;
        }
    }
}
