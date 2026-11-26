using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public sealed class UploadRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Url { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Provider { get; set; } = "Catbox";
        public DateTime UploadedAt { get; set; } = DateTime.Now;
        public DateTime? ExpiresAt { get; set; }
        public string LocalPath { get; set; } = "";

        public string ProviderDisplay => Provider == UploadProviderService.LocalProvider
            ? LocalizationService.Get("Privacy_ProviderLocal")
            : "catbox.moe";

        public string HistoryLine => $"{FileName}\n{Url}\n{MetaDisplay}";

        public string MetaDisplay
        {
            get
            {
                string expiry = ExpiresAt.HasValue
                    ? $" · {LocalizationService.Format("Privacy_ExpiresFmt", ExpiresAt.Value.ToString("yyyy-MM-dd HH:mm"))}"
                    : "";
                return $"{ProviderDisplay} · {UploadedAt:yyyy-MM-dd HH:mm}{expiry}";
            }
        }
    }

    public sealed class UploadHistoryService
    {
        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoUtils");
        private static readonly string HistoryFile = Path.Combine(DataFolder, "upload-history.json");
        private static readonly string LocalUploadsFolder = Path.Combine(DataFolder, "uploads");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static UploadHistoryService? _instance;
        public static UploadHistoryService Instance => _instance ??= new UploadHistoryService();

        private readonly object _lock = new();
        private List<UploadRecord> _records;

        public string LocalFolder => LocalUploadsFolder;
        public event Action? Changed;

        private UploadHistoryService()
        {
            _records = Load();
            ExpireLocalUploads();
        }

        public IReadOnlyList<UploadRecord> Records
        {
            get
            {
                lock (_lock)
                {
                    return _records.OrderByDescending(record => record.UploadedAt).ToList();
                }
            }
        }

        public void Add(UploadRecord record)
        {
            lock (_lock)
            {
                _records.Insert(0, record);
                if (_records.Count > 200)
                {
                    _records.RemoveRange(200, _records.Count - 200);
                }
                Save();
            }
            Changed?.Invoke();
        }

        public bool Remove(string id)
        {
            bool removed;
            lock (_lock)
            {
                var record = _records.FirstOrDefault(item => item.Id == id);
                if (record == null)
                {
                    return false;
                }

                _records.Remove(record);
                if (!string.IsNullOrEmpty(record.LocalPath))
                {
                    TryDelete(record.LocalPath);
                }
                Save();
                removed = true;
            }
            Changed?.Invoke();
            return removed;
        }

        public void Clear()
        {
            lock (_lock)
            {
                foreach (var record in _records.Where(record => !string.IsNullOrEmpty(record.LocalPath)))
                {
                    TryDelete(record.LocalPath);
                }
                _records.Clear();
                Save();
            }
            Changed?.Invoke();
        }

        public void ExpireLocalUploads()
        {
            bool changed = false;
            lock (_lock)
            {
                var expired = _records
                    .Where(record => record.ExpiresAt.HasValue && record.ExpiresAt.Value <= DateTime.Now)
                    .ToList();
                foreach (var record in expired)
                {
                    _records.Remove(record);
                    if (!string.IsNullOrEmpty(record.LocalPath))
                    {
                        TryDelete(record.LocalPath);
                    }
                    changed = true;
                }
                if (changed)
                {
                    Save();
                }
            }
            if (changed)
            {
                Changed?.Invoke();
            }
        }

        public bool OpenLocalFolder()
        {
            try
            {
                Directory.CreateDirectory(LocalUploadsFolder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = LocalUploadsFolder,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    NativeMethods.MoveToRecycleBin(path);
                }
            }
            catch
            {
            }
        }

        private static List<UploadRecord> Load()
        {
            try
            {
                if (File.Exists(HistoryFile))
                {
                    string json = File.ReadAllText(HistoryFile);
                    var loaded = JsonSerializer.Deserialize<List<UploadRecord>>(json);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch
            {
            }
            return new List<UploadRecord>();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                string json = JsonSerializer.Serialize(_records, JsonOptions);
                string tempPath = HistoryFile + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, HistoryFile, overwrite: true);
            }
            catch
            {
            }
        }
    }

    public static class UploadProviderService
    {
        public const string CatboxProvider = "Catbox";
        public const string LocalProvider = "LocalFolder";

        public static bool IsLocalProvider =>
            string.Equals(
                SettingsService.Instance.Current.UploadProvider,
                LocalProvider,
                StringComparison.OrdinalIgnoreCase);

        public static async Task<string> UploadImageAsync(
            BitmapSource bitmap,
            IProgress<UploadProgressInfo>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsLocalProvider)
            {
                string url = await CloudUploadService.UploadImageAsync(bitmap, progress, cancellationToken)
                    .ConfigureAwait(false);
                AddHistory(url, $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png", CatboxProvider, null, "");
                return url;
            }

            byte[] bytes = CloudUploadService.EncodeBitmapToPng(bitmap);
            string fileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            return await SaveLocalAsync(bytes, fileName, progress, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<string> UploadFileAsync(
            string filePath,
            IProgress<UploadProgressInfo>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsLocalProvider)
            {
                string url = await CloudUploadService.UploadFileAsync(filePath, progress, cancellationToken)
                    .ConfigureAwait(false);
                AddHistory(url, Path.GetFileName(filePath), CatboxProvider, null, "");
                return url;
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("File not found for upload.", filePath);
            }

            string destination = BuildLocalPath(Path.GetFileName(filePath));
            long total = new FileInfo(filePath).Length;
            progress?.Report(new UploadProgressInfo(0, total));
            try
            {
                await Task.Run(() =>
                {
                    using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    byte[] buffer = new byte[65536];
                    long copied = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        copied += read;
                        progress?.Report(new UploadProgressInfo(copied, total));
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                TryDeletePartial(destination);
                throw;
            }

            return AddLocalHistory(destination, Path.GetFileName(filePath), total);
        }

        private static async Task<string> SaveLocalAsync(
            byte[] bytes,
            string fileName,
            IProgress<UploadProgressInfo>? progress,
            CancellationToken cancellationToken)
        {
            string destination = BuildLocalPath(fileName);
            progress?.Report(new UploadProgressInfo(0, bytes.Length));
            try
            {
                await Task.Run(() =>
                {
                    using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    output.Write(bytes, 0, bytes.Length);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                TryDeletePartial(destination);
                throw;
            }
            progress?.Report(new UploadProgressInfo(bytes.Length, bytes.Length));
            return AddLocalHistory(destination, fileName, bytes.Length);
        }

        private static string BuildLocalPath(string fileName)
        {
            Directory.CreateDirectory(UploadHistoryService.Instance.LocalFolder);
            string safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
            string candidate = Path.Combine(
                UploadHistoryService.Instance.LocalFolder,
                $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeName}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            for (int index = 2; index < 1000; index++)
            {
                string numbered = Path.Combine(
                    UploadHistoryService.Instance.LocalFolder,
                    $"{DateTime.Now:yyyyMMdd_HHmmss}_{index}_{safeName}");
                if (!File.Exists(numbered))
                {
                    return numbered;
                }
            }

            return Path.Combine(
                UploadHistoryService.Instance.LocalFolder,
                $"{Guid.NewGuid():N}_{safeName}");
        }

        private static string AddLocalHistory(string path, string fileName, long size)
        {
            int hours = Math.Clamp(SettingsService.Instance.Current.LocalUploadExpiryHours, 1, 720);
            var record = new UploadRecord
            {
                Url = new Uri(path).AbsoluteUri,
                FileName = fileName,
                Provider = LocalProvider,
                UploadedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddHours(hours),
                LocalPath = path
            };
            UploadHistoryService.Instance.Add(record);
            return record.Url;
        }

        private static void AddHistory(
            string url,
            string fileName,
            string provider,
            DateTime? expiresAt,
            string localPath)
        {
            UploadHistoryService.Instance.Add(new UploadRecord
            {
                Url = url,
                FileName = fileName,
                Provider = provider,
                UploadedAt = DateTime.Now,
                ExpiresAt = expiresAt,
                LocalPath = localPath
            });
        }

        private static void TryDeletePartial(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
