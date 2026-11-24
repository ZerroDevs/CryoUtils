using System;
using System.IO;
using System.Text.Json;
using CryoUtils.Models;

namespace CryoUtils.Services
{
    public class SettingsService
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoUtils");
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        private static SettingsService? _instance;
        public static SettingsService Instance => _instance ??= new SettingsService();

        private readonly object _ioLock = new();

        public AppSettings Current { get; private set; }

        public event Action? SettingsChanged;

        public string DefaultSaveFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CryoUtils");

        public string EffectiveSaveFolder =>
            string.IsNullOrWhiteSpace(Current.SaveFolder)
                ? DefaultSaveFolder
                : Current.SaveFolder;

        public SettingsService()
        {
            Current = Load();
        }

        public AppSettings Load()
        {
            lock (_ioLock)
            {
                try
                {
                    if (File.Exists(SettingsFile))
                    {
                        string json = File.ReadAllText(SettingsFile);
                        var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                        if (loaded != null)
                        {
                            return loaded;
                        }
                    }
                }
                catch
                {
                    // Fall back to defaults.
                }

                return new AppSettings();
            }
        }

        public void Save()
        {
            bool saved = false;
            lock (_ioLock)
            {
                try
                {
                    WriteAtomicJson(SettingsFile, Current);
                    saved = true;
                }
                catch
                {
                    // Ignore write failures; the running session keeps working.
                }
            }

            if (saved)
            {
                SettingsChanged?.Invoke();
            }
        }

        internal void SaveWithoutNotification()
        {
            lock (_ioLock)
            {
                try
                {
                    WriteAtomicJson(SettingsFile, Current);
                }
                catch
                {
                }
            }
        }

        internal static void WriteAtomicJson(string targetPath, AppSettings settings)
        {
            string? directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(settings, WriteOptions);
            string tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, targetPath, overwrite: true);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
                throw;
            }
        }

        public void Export(string filePath)
        {
            lock (_ioLock)
            {
                WriteAtomicJson(filePath, Current);
            }
        }

        public void Import(string filePath)
        {
            string json = File.ReadAllText(filePath);
            var imported = JsonSerializer.Deserialize<AppSettings>(json)
                ?? throw new InvalidDataException("Settings file did not contain valid configuration data.");
            lock (_ioLock)
            {
                Current = imported;
            }
            Save();
        }

        public void Reset()
        {
            lock (_ioLock)
            {
                Current = new AppSettings();
            }
            Save();
        }

        public void RegisterRecentColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return;
            }

            string normalized = hex.Trim().ToUpperInvariant();
            Current.RecentColors.RemoveAll(
                item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
            Current.RecentColors.Insert(0, normalized);
            while (Current.RecentColors.Count > 10)
            {
                Current.RecentColors.RemoveAt(Current.RecentColors.Count - 1);
            }
            Save();
        }
    }
}
