using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public sealed class PaletteColor
    {
        public string Hex { get; set; } = "#000000";
        public int Count { get; set; }
        public string Rgb => Hex;
        public Brush Swatch
        {
            get
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(Hex);
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    return brush;
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }
        }
    }

    public sealed class SavedContrastPair
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Foreground { get; set; } = "#FFFFFF";
        public string Background { get; set; } = "#1E6FAE";
        public double Ratio { get; set; }
        public DateTime SavedAt { get; set; } = DateTime.Now;

        public string Summary => $"{Name} · {Foreground} / {Background} · {Ratio:0.00}:1";
    }

    public enum ColorBlindnessType
    {
        None,
        Protanopia,
        Deuteranopia,
        Tritanopia,
        Achromatopsia
    }

    public sealed class ColorStudioService
    {
        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoUtils");
        private static readonly string PairsFile = Path.Combine(DataFolder, "contrast-pairs.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static ColorStudioService? _instance;
        public static ColorStudioService Instance => _instance ??= new ColorStudioService();

        private readonly object _lock = new();
        private List<SavedContrastPair> _pairs;

        public event Action? Changed;

        private ColorStudioService()
        {
            _pairs = LoadPairs();
        }

        public IReadOnlyList<SavedContrastPair> Pairs
        {
            get
            {
                lock (_lock)
                {
                    return _pairs.OrderByDescending(pair => pair.SavedAt).ToList();
                }
            }
        }

        public static IReadOnlyList<PaletteColor> ExtractPalette(BitmapSource source, int maxColors = 8)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return Array.Empty<PaletteColor>();
            }

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            var buckets = new Dictionary<int, (long R, long G, long B, int Count)>();
            int step = Math.Max(1, Math.Min(width, height) / 160);
            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    int offset = y * stride + x * 4;
                    byte b = pixels[offset];
                    byte g = pixels[offset + 1];
                    byte r = pixels[offset + 2];
                    byte a = pixels[offset + 3];
                    if (a < 32)
                    {
                        continue;
                    }

                    int key = ((r / 32) << 16) | ((g / 32) << 8) | (b / 32);
                    if (buckets.TryGetValue(key, out var bucket))
                    {
                        buckets[key] = (bucket.R + r, bucket.G + g, bucket.B + b, bucket.Count + 1);
                    }
                    else
                    {
                        buckets[key] = (r, g, b, 1);
                    }
                }
            }

            return buckets.Values
                .OrderByDescending(bucket => bucket.Count)
                .Take(Math.Clamp(maxColors, 3, 16))
                .Select(bucket =>
                {
                    byte r = (byte)(bucket.R / bucket.Count);
                    byte g = (byte)(bucket.G / bucket.Count);
                    byte b = (byte)(bucket.B / bucket.Count);
                    return new PaletteColor
                    {
                        Hex = $"#{r:X2}{g:X2}{b:X2}",
                        Count = bucket.Count
                    };
                })
                .ToList();
        }

        public void AddPair(string name, string foreground, string background)
        {
            var fg = ParseColor(foreground);
            var bg = ParseColor(background);
            if (fg == null || bg == null)
            {
                throw new ArgumentException("Colors must be hex values.");
            }

            var pair = new SavedContrastPair
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"{foreground} / {background}" : name.Trim(),
                Foreground = foreground.ToUpperInvariant(),
                Background = background.ToUpperInvariant(),
                Ratio = Math.Round(ColorContrastService.CalculateRatio(fg.Value, bg.Value), 2)
            };
            lock (_lock)
            {
                _pairs.Insert(0, pair);
                if (_pairs.Count > 60)
                {
                    _pairs.RemoveRange(60, _pairs.Count - 60);
                }
                SavePairs();
            }
            Changed?.Invoke();
        }

        public void RemovePair(string id)
        {
            lock (_lock)
            {
                _pairs.RemoveAll(pair => pair.Id == id);
                SavePairs();
            }
            Changed?.Invoke();
        }

        public static string ExportCss(IEnumerable<PaletteColor> palette)
        {
            var builder = new StringBuilder(":root {\n");
            int index = 1;
            foreach (var color in palette)
            {
                builder.Append("  --color-");
                builder.Append(index++);
                builder.Append(": ");
                builder.Append(color.Hex);
                builder.Append(";\n");
            }
            builder.Append("}\n");
            return builder.ToString();
        }

        public static string ExportJson(IEnumerable<PaletteColor> palette)
        {
            return JsonSerializer.Serialize(
                palette.Select((color, index) => new { name = $"color-{index + 1}", hex = color.Hex }),
                JsonOptions);
        }

        public static string ExportTailwind(IEnumerable<PaletteColor> palette)
        {
            var builder = new StringBuilder("module.exports = {\n  theme: {\n    extend: {\n      colors: {\n");
            int index = 1;
            foreach (var color in palette)
            {
                builder.Append("        'cryo-");
                builder.Append(index++);
                builder.Append("': '");
                builder.Append(color.Hex);
                builder.Append("',\n");
            }
            builder.Append("      },\n    },\n  },\n};\n");
            return builder.ToString();
        }

        public static string Simulate(string hex, ColorBlindnessType type)
        {
            var color = ParseColor(hex);
            if (color == null || type == ColorBlindnessType.None)
            {
                return hex.ToUpperInvariant();
            }

            double r = color.Value.R / 255.0;
            double g = color.Value.G / 255.0;
            double b = color.Value.B / 255.0;
            double nr, ng, nb;
            switch (type)
            {
                case ColorBlindnessType.Protanopia:
                    nr = 0.567 * r + 0.433 * g;
                    ng = 0.558 * r + 0.442 * g;
                    nb = 0.242 * g + 0.758 * b;
                    break;
                case ColorBlindnessType.Deuteranopia:
                    nr = 0.625 * r + 0.375 * g;
                    ng = 0.700 * r + 0.300 * g;
                    nb = 0.300 * g + 0.700 * b;
                    break;
                case ColorBlindnessType.Tritanopia:
                    nr = 0.950 * r + 0.050 * g;
                    ng = 0.433 * g + 0.567 * b;
                    nb = 0.475 * g + 0.525 * b;
                    break;
                default:
                    double gray = 0.299 * r + 0.587 * g + 0.114 * b;
                    nr = ng = nb = gray;
                    break;
            }

            return $"#{ToByte(nr):X2}{ToByte(ng):X2}{ToByte(nb):X2}";
        }

        public static Color? ParseColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return null;
            }

            try
            {
                string value = hex.Trim();
                if (!value.StartsWith("#", StringComparison.Ordinal))
                {
                    value = "#" + value;
                }
                if (value.Length == 4)
                {
                    value = $"#{value[1]}{value[1]}{value[2]}{value[2]}{value[3]}{value[3]}";
                }
                if (value.Length != 7)
                {
                    return null;
                }
                return (Color)ColorConverter.ConvertFromString(value);
            }
            catch
            {
                return null;
            }
        }

        private static byte ToByte(double value) =>
            (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

        private static List<SavedContrastPair> LoadPairs()
        {
            try
            {
                if (File.Exists(PairsFile))
                {
                    string json = File.ReadAllText(PairsFile);
                    var loaded = JsonSerializer.Deserialize<List<SavedContrastPair>>(json);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch
            {
            }
            return new List<SavedContrastPair>();
        }

        private void SavePairs()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                string json = JsonSerializer.Serialize(_pairs, JsonOptions);
                string tempPath = PairsFile + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, PairsFile, overwrite: true);
            }
            catch
            {
            }
        }
    }
}
