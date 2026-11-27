using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace CryoUtils.Services
{
    public sealed class UpdateInfo
    {
        public Version Version { get; init; } = new(0, 0);
        public string Tag { get; init; } = "";
        public string Name { get; init; } = "";
        public string Notes { get; init; } = "";
        public string ReleaseUrl { get; init; } = "";
        public string AssetName { get; init; } = "";
        public string AssetUrl { get; init; } = "";
        public string ChecksumName { get; init; } = "";
        public string ChecksumUrl { get; init; } = "";
        public DateTime PublishedAt { get; init; }
        public bool HasAsset => !string.IsNullOrEmpty(AssetUrl);
    }

    public static class UpdaterService
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/ZerroDevs/CryoUtils/releases/latest";

        private static readonly HttpClient Http = CreateClient();

        public static string CurrentVersion
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                string? informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;
                if (!string.IsNullOrEmpty(informational))
                {
                    int plus = informational.IndexOf('+');
                    return plus >= 0 ? informational[..plus] : informational;
                }

                return assembly.GetName().Version?.ToString(3) ?? "0.0.102";
            }
        }

        public static string BackupFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CryoUtils",
            "Rollback");

        public static string BackupExePath => Path.Combine(BackupFolder, "CryoUtils.previous.exe");
        public static string BackupVersionPath => Path.Combine(BackupFolder, "version.txt");
        public static bool IsBackupReady => File.Exists(BackupExePath);

        public static string? BackupVersion
        {
            get
            {
                try
                {
                    return File.Exists(BackupVersionPath)
                        ? File.ReadAllText(BackupVersionPath).Trim()
                        : null;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CryoUtils-Updater");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public static async Task<UpdateInfo?> CheckAsync()
        {
            using var response = await Http.GetAsync(LatestReleaseUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"GitHub returned {(int)response.StatusCode}.");
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            string tag = root.TryGetProperty("tag_name", out var tagProperty)
                ? tagProperty.GetString() ?? ""
                : "";
            string normalized = tag.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(normalized, out var version))
            {
                throw new InvalidOperationException("Release tag is not a version.");
            }

            var current = Version.TryParse(CurrentVersion, out var parsedCurrent)
                ? parsedCurrent
                : new Version(0, 0);
            if (version <= current)
            {
                return null;
            }

            string assetName = "";
            string assetUrl = "";
            string checksumName = "";
            string checksumUrl = "";
            if (root.TryGetProperty("assets", out var assets) &&
                assets.ValueKind == JsonValueKind.Array)
            {
                var candidates = new List<(string Name, string Url)>();
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var nameProperty)
                        ? nameProperty.GetString() ?? ""
                        : "";
                    string url = asset.TryGetProperty("browser_download_url", out var urlProperty)
                        ? urlProperty.GetString() ?? ""
                        : "";
                    if (!string.IsNullOrEmpty(url))
                    {
                        candidates.Add((name, url));
                    }
                }

                (assetName, assetUrl) = SelectPreferredAsset(
                    candidates,
                    IsSelfContainedInstallation(AppContext.BaseDirectory));
                if (!string.IsNullOrEmpty(assetName))
                {
                    (checksumName, checksumUrl) = SelectChecksumAsset(candidates, assetName);
                }
            }

            DateTime published = DateTime.MinValue;
            if (root.TryGetProperty("published_at", out var publishedProperty) &&
                DateTime.TryParse(publishedProperty.GetString(), out var parsedPublished))
            {
                published = parsedPublished.ToLocalTime();
            }

            return new UpdateInfo
            {
                Version = version,
                Tag = tag,
                Name = root.TryGetProperty("name", out var nameElem)
                    ? nameElem.GetString() ?? tag
                    : tag,
                Notes = root.TryGetProperty("body", out var bodyElem)
                    ? bodyElem.GetString() ?? ""
                    : "",
                ReleaseUrl = root.TryGetProperty("html_url", out var htmlElem)
                    ? htmlElem.GetString() ?? ""
                    : "",
                AssetName = assetName,
                AssetUrl = assetUrl,
                ChecksumName = checksumName,
                ChecksumUrl = checksumUrl,
                PublishedAt = published
            };
        }

        public static async Task<string> DownloadAsync(
            UpdateInfo info,
            IProgress<double>? progress = null)
        {
            if (!info.HasAsset)
            {
                throw new InvalidOperationException("This release has no installer asset.");
            }
            if (string.IsNullOrEmpty(info.ChecksumUrl))
            {
                throw new InvalidOperationException(
                    LocalizationService.Get("Update_ChecksumMissing"));
            }

            string folder = Path.Combine(Path.GetTempPath(), "CryoUtils", "update");
            Directory.CreateDirectory(folder);
            string extension = Path.GetExtension(info.AssetName);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".exe";
            }
            string path = Path.Combine(folder, $"CryoUtils_{info.Version}{extension}");

            using var response = await Http.GetAsync(
                info.AssetUrl,
                HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? -1;
            await using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var output = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    received += read;
                    if (total > 0)
                    {
                        progress?.Report(received / (double)total);
                    }
                }
            }

            try
            {
                string checksumText = await Http.GetStringAsync(info.ChecksumUrl).ConfigureAwait(false);
                if (checksumText.Length > 64 * 1024)
                {
                    throw new InvalidOperationException(
                        LocalizationService.Get("Update_ChecksumMalformed"));
                }

                string? expectedHex = ParseExpectedSha256(checksumText, info.AssetName);
                if (string.IsNullOrEmpty(expectedHex))
                {
                    throw new InvalidOperationException(
                        LocalizationService.Get("Update_ChecksumMalformed"));
                }

                if (!await VerifyFileSha256Async(path, expectedHex).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        LocalizationService.Get("Update_ChecksumMismatch"));
                }
            }
            catch
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
                throw;
            }

            return path;
        }

        internal static (string Name, string Url) SelectChecksumAsset(
            IEnumerable<(string Name, string Url)> assets,
            string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName))
            {
                return ("", "");
            }

            string expected = assetName + ".sha256";
            foreach (var (name, url) in assets)
            {
                if (string.Equals(name, expected, StringComparison.OrdinalIgnoreCase) &&
                    Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps)
                {
                    return (name, url);
                }
            }

            return ("", "");
        }

        internal static string? ParseExpectedSha256(string checksumContent, string? assetName)
        {
            if (string.IsNullOrWhiteSpace(checksumContent))
            {
                return null;
            }

            foreach (string rawLine in checksumContent.Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || !IsSha256Hex(parts[0]))
                {
                    continue;
                }

                if (parts.Length == 1 ||
                    string.IsNullOrWhiteSpace(assetName) ||
                    string.Equals(Path.GetFileName(parts[^1].TrimStart('*')), assetName, StringComparison.OrdinalIgnoreCase))
                {
                    return parts[0].ToLowerInvariant();
                }
            }

            return null;
        }

        internal static async Task<bool> VerifyFileSha256Async(string filePath, string expectedHex)
        {
            if (!IsSha256Hex(expectedHex) || !File.Exists(filePath))
            {
                return false;
            }

            byte[] expected = Convert.FromHexString(expectedHex);
            byte[] actual;
            await using var stream = File.OpenRead(filePath);
            actual = await System.Security.Cryptography.SHA256.HashDataAsync(stream).ConfigureAwait(false);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        private static bool IsSha256Hex(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }

            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') ||
                           (c >= 'a' && c <= 'f') ||
                           (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        public static void CreateBackup()
        {
            string exe = CurrentExePath;
            if (!File.Exists(exe))
            {
                throw new InvalidOperationException("Current executable was not found.");
            }

            Directory.CreateDirectory(BackupFolder);
            File.Copy(exe, BackupExePath, true);
            File.WriteAllText(BackupVersionPath, CurrentVersion);
        }

        public static void DeleteBackup()
        {
            if (Directory.Exists(BackupFolder))
            {
                Directory.Delete(BackupFolder, true);
            }
        }

        public static void ApplyUpdate(string installerPath)
        {
            string exe = CurrentExePath;
            string scriptPath = Path.Combine(
                Path.GetTempPath(),
                "CryoUtils",
                "apply_update.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);

            string fileName = Path.GetFileName(installerPath);
            bool setupPackage = installerPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("install", StringComparison.OrdinalIgnoreCase);
            string applyLine = setupPackage
                ? (installerPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                    ? $"start \"\" /wait msiexec /i \"{installerPath}\" /qn /norestart"
                    : $"start \"\" /wait \"{installerPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART")
                : $"copy /Y \"{installerPath}\" \"{exe}\" >nul";

            string script =
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {Environment.ProcessId}\" 2>nul | find \"{Environment.ProcessId}\" >nul\r\n" +
                "if not errorlevel 1 (\r\n" +
                "  timeout /t 1 /nobreak >nul\r\n" +
                "  goto wait\r\n" +
                ")\r\n" +
                applyLine + "\r\n" +
                $"start \"\" \"{exe}\"\r\n" +
                "del \"%~f0\"\r\n";

            File.WriteAllText(scriptPath, script);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Application.Current.Shutdown();
        }

        public static void RestoreBackup()
        {
            if (!File.Exists(BackupExePath))
            {
                throw new InvalidOperationException("No rollback backup was found.");
            }

            string exe = CurrentExePath;
            string scriptPath = Path.Combine(
                Path.GetTempPath(),
                "CryoUtils",
                "restore_backup.cmd");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);

            string script =
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {Environment.ProcessId}\" 2>nul | find \"{Environment.ProcessId}\" >nul\r\n" +
                "if not errorlevel 1 (\r\n" +
                "  timeout /t 1 /nobreak >nul\r\n" +
                "  goto wait\r\n" +
                ")\r\n" +
                $"copy /Y \"{BackupExePath}\" \"{exe}\" >nul\r\n" +
                $"start \"\" \"{exe}\"\r\n" +
                "del \"%~f0\"\r\n";

            File.WriteAllText(scriptPath, script);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Application.Current.Shutdown();
        }

        public static void OpenReleasePage(UpdateInfo? info)
        {
            string url = string.IsNullOrEmpty(info?.ReleaseUrl)
                ? "https://github.com/ZerroDevs/CryoUtils/releases"
                : info!.ReleaseUrl;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        internal static bool IsSelfContainedInstallation(string baseDirectory) =>
            !string.IsNullOrWhiteSpace(baseDirectory) &&
            File.Exists(Path.Combine(baseDirectory, "coreclr.dll"));

        internal static (string Name, string Url) SelectPreferredAsset(
            IEnumerable<(string Name, string Url)> assets,
            bool isSelfContained)
        {
            string fallbackName = "";
            string fallbackUrl = "";
            string anyExeName = "";
            string anyExeUrl = "";

            foreach (var (name, url) in assets)
            {
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                bool isExe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                bool isMsi = name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
                if (!isExe && !isMsi)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(fallbackUrl))
                {
                    fallbackName = name;
                    fallbackUrl = url;
                }

                if (isExe)
                {
                    if (string.IsNullOrEmpty(anyExeUrl))
                    {
                        anyExeName = name;
                        anyExeUrl = url;
                    }

                    bool isLite = name.Contains("-lite", StringComparison.OrdinalIgnoreCase);
                    if (isSelfContained != isLite)
                    {
                        return (name, url);
                    }
                }
            }

            return !string.IsNullOrEmpty(anyExeUrl)
                ? (anyExeName, anyExeUrl)
                : (fallbackName, fallbackUrl);
        }

        private static string CurrentExePath
        {
            get
            {
                string? path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path) &&
                    path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                return Process.GetCurrentProcess().MainModule?.FileName ?? "";
            }
        }
    }
}
