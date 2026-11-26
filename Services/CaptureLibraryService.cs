using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public sealed class CaptureLibraryItem
    {
        public string Path { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime Created { get; set; }
        public long SizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string SourceApp { get; set; } = "";
        public string SourceWindow { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public bool IsFavorite { get; set; }

        [JsonIgnore]
        public string TagDisplay => Tags.Count == 0 ? "" : string.Join(" · ", Tags);

        [JsonIgnore]
        public BitmapSource? Thumbnail { get; set; }

        [JsonIgnore]
        public string SizeDisplay => ImageIO.FormatBytes(SizeBytes);

        [JsonIgnore]
        public bool IsVideo =>
            Path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
            Path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public string KindLabel => Path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            ? "MP4"
            : Path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                ? "GIF"
                : "IMAGE";

        [JsonIgnore]
        public string CreatedDisplay => Created.ToString("MMM d · HH:mm");

        [JsonIgnore]
        public string FavoriteMark => IsFavorite ? "★" : "";

        [JsonIgnore]
        public string Dimensions => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "";

        [JsonIgnore]
        public string SourceDisplay => string.IsNullOrWhiteSpace(SourceApp)
            ? SourceWindow
            : string.IsNullOrWhiteSpace(SourceWindow)
                ? SourceApp
                : $"{SourceApp} — {SourceWindow}";

        [JsonIgnore]
        public string DisplayLine =>
            (IsFavorite ? "★  " : "   ") + FileName + "\n" + MetaDisplay +
            (string.IsNullOrWhiteSpace(TagDisplay) ? "" : "\n" + TagDisplay);

        [JsonIgnore]
        public string MetaDisplay
        {
            get
            {
                var parts = new List<string> { Created.ToString("yyyy-MM-dd HH:mm"), SizeDisplay };
                if (!string.IsNullOrEmpty(Dimensions))
                {
                    parts.Add(Dimensions);
                }
                if (!string.IsNullOrWhiteSpace(SourceDisplay))
                {
                    parts.Add(SourceDisplay);
                }
                return string.Join("   ", parts);
            }
        }
    }

    public sealed class CaptureLibraryMetadata
    {
        public string SourceApp { get; set; } = "";
        public string SourceWindow { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public bool IsFavorite { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime RecordedAt { get; set; }
    }

    public sealed class CaptureLibraryService
    {
        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoUtils");

        private static readonly string MetadataFile = Path.Combine(DataFolder, "capture-library.json");

        private static readonly HashSet<string> LibraryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".jfif", ".mp4"
        };

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static CaptureLibraryService? _instance;
        public static CaptureLibraryService Instance => _instance ??= new CaptureLibraryService();

        private readonly object _lock = new();
        private readonly Dictionary<string, CaptureLibraryMetadata> _metadata;

        public event Action? Changed;

        private CaptureLibraryService()
        {
            _metadata = LoadMetadata();
        }

        public string LibraryFolder => SettingsService.Instance.EffectiveSaveFolder;

        public IReadOnlyList<CaptureLibraryItem> Load(
            string? search = null,
            string? tag = null,
            bool favoritesOnly = false)
        {
            var items = new List<CaptureLibraryItem>();
            string folder = LibraryFolder;
            if (!Directory.Exists(folder))
            {
                return items;
            }

            string query = search?.Trim() ?? "";
            string tagQuery = tag?.Trim() ?? "";

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return items;
            }

            lock (_lock)
            {
                foreach (string path in files)
                {
                    try
                    {
                        if (!LibraryExtensions.Contains(Path.GetExtension(path)))
                        {
                            continue;
                        }
                        var info = new FileInfo(path);
                        if ((info.Attributes & FileAttributes.Hidden) != 0)
                        {
                            continue;
                        }

                        _metadata.TryGetValue(Normalize(path), out var metadata);
                        var item = new CaptureLibraryItem
                        {
                            Path = path,
                            FileName = info.Name,
                            Created = info.LastWriteTime,
                            SizeBytes = info.Length,
                            Width = metadata?.Width ?? 0,
                            Height = metadata?.Height ?? 0,
                            SourceApp = metadata?.SourceApp ?? "",
                            SourceWindow = metadata?.SourceWindow ?? "",
                            Tags = metadata?.Tags.ToList() ?? new List<string>(),
                            IsFavorite = metadata?.IsFavorite ?? false
                        };

                        if (favoritesOnly && !item.IsFavorite)
                        {
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(tagQuery) &&
                            !item.Tags.Any(itemTag => itemTag.Contains(tagQuery, StringComparison.CurrentCultureIgnoreCase)))
                        {
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(query) &&
                            !MatchesQuery(item, query))
                        {
                            continue;
                        }

                        items.Add(item);
                    }
                    catch
                    {
                    }
                }
            }

            return items
                .OrderByDescending(item => item.IsFavorite)
                .ThenByDescending(item => item.Created)
                .ToList();
        }

        private static bool MatchesQuery(CaptureLibraryItem item, string query)
        {
            return item.FileName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   item.SourceApp.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   item.SourceWindow.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                   item.Tags.Any(tag => tag.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        public IReadOnlyList<string> GetTags()
        {
            lock (_lock)
            {
                return _metadata.Values
                    .SelectMany(value => value.Tags)
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
        }

        public void Record(string path, string? sourceApp, string? sourceWindow, int width = 0, int height = 0)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            string key = Normalize(path);
            lock (_lock)
            {
                if (!_metadata.TryGetValue(key, out var metadata))
                {
                    metadata = new CaptureLibraryMetadata();
                    _metadata[key] = metadata;
                }

                if (!string.IsNullOrWhiteSpace(sourceApp))
                {
                    metadata.SourceApp = sourceApp.Trim();
                }
                if (!string.IsNullOrWhiteSpace(sourceWindow))
                {
                    metadata.SourceWindow = sourceWindow.Trim();
                }
                if (width > 0 && height > 0)
                {
                    metadata.Width = width;
                    metadata.Height = height;
                }
                metadata.RecordedAt = DateTime.Now;
                SaveMetadata();
            }

            Changed?.Invoke();
        }

        public void SetFavorite(string path, bool favorite)
        {
            Update(path, metadata => metadata.IsFavorite = favorite);
        }

        public void SetTags(string path, IEnumerable<string> tags)
        {
            Update(path, metadata =>
            {
                metadata.Tags = tags
                    .Select(tag => tag.Trim())
                    .Where(tag => tag.Length > 0)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .Take(12)
                    .ToList();
            });
        }

        private void Update(string path, Action<CaptureLibraryMetadata> update)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string key = Normalize(path);
            lock (_lock)
            {
                if (!_metadata.TryGetValue(key, out var metadata))
                {
                    metadata = new CaptureLibraryMetadata();
                    _metadata[key] = metadata;
                }
                update(metadata);
                SaveMetadata();
            }
            Changed?.Invoke();
        }

        public bool OpenFile(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RevealFile(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public IReadOnlyList<string> DeleteToRecycleBin(IEnumerable<string> paths)
        {
            var failed = new List<string>();
            lock (_lock)
            {
                foreach (string path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    if (!File.Exists(path))
                    {
                        _metadata.Remove(Normalize(path));
                        continue;
                    }

                    if (NativeMethods.MoveToRecycleBin(path))
                    {
                        _metadata.Remove(Normalize(path));
                    }
                    else
                    {
                        failed.Add(path);
                    }
                }
                SaveMetadata();
            }

            Changed?.Invoke();
            return failed;
        }

        private static string Normalize(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }

        private static Dictionary<string, CaptureLibraryMetadata> LoadMetadata()
        {
            try
            {
                if (File.Exists(MetadataFile))
                {
                    string json = File.ReadAllText(MetadataFile);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, CaptureLibraryMetadata>>(json);
                    if (loaded != null)
                    {
                        return new Dictionary<string, CaptureLibraryMetadata>(
                            loaded,
                            StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
            }

            return new Dictionary<string, CaptureLibraryMetadata>(StringComparer.OrdinalIgnoreCase);
        }

        private void SaveMetadata()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                string json = JsonSerializer.Serialize(_metadata, JsonOptions);
                string tempPath = MetadataFile + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, MetadataFile, overwrite: true);
            }
            catch
            {
            }
        }
    }
}
