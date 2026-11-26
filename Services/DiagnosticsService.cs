using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CryoUtils.Services
{
    public static class DiagnosticsService
    {
        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoUtils");
        private static readonly string LogFolderPath = Path.Combine(DataFolder, "logs");
        private static readonly string LogFile = Path.Combine(LogFolderPath, "cryoutils.log");
        private static readonly object Sync = new();

        public static string LogPath => LogFile;
        public static string LogFolder => LogFolderPath;

        public static void Log(string message, Exception? exception = null)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogFolderPath);
                    if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 2 * 1024 * 1024)
                    {
                        string oldPath = Path.Combine(LogFolderPath, "cryoutils.old.log");
                        File.Copy(LogFile, oldPath, overwrite: true);
                        File.Delete(LogFile);
                    }

                    var builder = new StringBuilder();
                    builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    builder.Append("  ");
                    builder.Append(message);
                    if (exception != null)
                    {
                        builder.AppendLine();
                        builder.Append(exception);
                    }
                    builder.AppendLine();
                    File.AppendAllText(LogFile, builder.ToString());
                }
            }
            catch
            {
            }
        }

        public static string ReadLog()
        {
            try
            {
                if (!File.Exists(LogFile))
                {
                    return "";
                }

                lock (Sync)
                {
                    using var stream = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static void ClearLog()
        {
            try
            {
                lock (Sync)
                {
                    if (File.Exists(LogFile))
                    {
                        File.Delete(LogFile);
                    }
                }
            }
            catch
            {
            }
        }

        public static bool OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(LogFolderPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = LogFolderPath,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
