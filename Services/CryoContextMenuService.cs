using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CryoUtils.Services
{
    public static class CryoContextMenuService
    {
        private const string RootKey = @"Software\Classes\SystemFileAssociations";
        private const string CustomMenuRoot = @"Software\Classes";
        private const string MenuName = "CryoUtils";

        private static readonly string[] PdfExts = { ".pdf" };
        private static readonly string[] ImageExts = { "image", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".jfif" };
        private static readonly string[] VideoExts = { "video", ".mp4", ".mkv", ".avi", ".mov", ".webm" };

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"{RootKey}\.png\shell\{MenuName}");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enable)
        {
            if (enable)
            {
                RegisterAll();
            }
            else
            {
                UnregisterAll();
            }
        }

        public static void RegisterAll()
        {
            UnregisterAll();

            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryoUtils.exe");

            // 1. PDF
            var pdfActions = new (string id, string action, string label)[]
            {
                ("01_pdf_png", "pdf-to-png", LocalizationService.Get("Context_PdfToPng")),
                ("02_pdf_jpg", "pdf-to-jpg", LocalizationService.Get("Context_PdfToJpg")),
                ("03_pdf_compress", "compress-pdf", LocalizationService.Get("Context_CompressPdf")),
            };
            foreach (var ext in PdfExts)
            {
                RegisterMenuForTarget(ext, exePath, pdfActions);
            }

            // 2. Images
            var imgActions = new (string id, string action, string label)[]
            {
                ("01_convert_png", "convert-to-png", LocalizationService.Get("Context_ConvertToPng")),
                ("02_convert_jpg", "convert-to-jpg", LocalizationService.Get("Context_ConvertToJpg")),
                ("03_convert_webp", "convert-to-webp", LocalizationService.Get("Context_ConvertToWebp")),
                ("04_convert_ico", "convert-to-ico", LocalizationService.Get("Context_ConvertToIco")),
                ("05_remove_bg", "remove-bg", LocalizationService.Get("Context_RemoveBg")),
                ("06_strip_exif", "strip-exif", LocalizationService.Get("Context_StripExif")),
                ("07_img_to_pdf", "img-to-pdf", LocalizationService.Get("Context_ImgToPdf")),
                ("08_quick_compress", "quick-compress", LocalizationService.Get("Context_QuickCompress")),
            };
            foreach (var ext in ImageExts)
            {
                RegisterMenuForTarget(ext, exePath, imgActions);
            }

            // 3. Videos
            var vidActions = new (string id, string action, string label)[]
            {
                ("01_video_mp3", "video-to-mp3", LocalizationService.Get("Context_VideoToMp3")),
                ("02_video_gif", "video-to-gif", LocalizationService.Get("Context_VideoToGif")),
            };
            foreach (var ext in VideoExts)
            {
                RegisterMenuForTarget(ext, exePath, vidActions);
            }

            NativeMethods.NotifyShellAssociationChanged();
        }

        public static void UnregisterAll()
        {
            foreach (var ext in PdfExts)
            {
                UnregisterAnchor(ext);
            }
            foreach (var ext in ImageExts)
            {
                UnregisterAnchor(ext);
            }
            foreach (var ext in VideoExts)
            {
                UnregisterAnchor(ext);
            }

            // Clean up legacy CryoUtils.Menu.* keys if present
            DeleteSubTree($@"{CustomMenuRoot}\CryoUtils.Menu.Pdf");
            DeleteSubTree($@"{CustomMenuRoot}\CryoUtils.Menu.Image");
            DeleteSubTree($@"{CustomMenuRoot}\CryoUtils.Menu.Video");

            NativeMethods.NotifyShellAssociationChanged();
        }

        private static void RegisterMenuForTarget(string target, string exePath, (string id, string action, string label)[] actions)
        {
            try
            {
                string menuKeyPath = $@"{RootKey}\{target}\shell\{MenuName}";
                using var menuKey = Registry.CurrentUser.CreateSubKey(menuKeyPath);
                if (menuKey == null) return;

                menuKey.SetValue("MUIVerb", "CryoUtils");
                menuKey.SetValue("", "CryoUtils");
                menuKey.SetValue("Icon", $"\"{exePath}\",0");
                menuKey.SetValue("subcommands", "");
                menuKey.SetValue("SubCommands", "");

                try { menuKey.DeleteValue("ExtendedSubCommandsKey"); } catch { }

                using var shellKey = menuKey.CreateSubKey("shell");
                if (shellKey == null) return;

                foreach (var action in actions)
                {
                    using var actionKey = shellKey.CreateSubKey(action.id);
                    if (actionKey == null) continue;

                    actionKey.SetValue("", action.label);
                    actionKey.SetValue("MUIVerb", action.label);
                    actionKey.SetValue("Icon", $"\"{exePath}\",0");

                    using var cmdKey = actionKey.CreateSubKey("command");
                    cmdKey?.SetValue("", $"\"{exePath}\" --action {action.action} \"%1\"");
                }
            }
            catch
            {
            }
        }

        private static void RegisterAnchor(string target, string exePath, string subCommandsKeyName)
        {
            // Legacy stub kept for backward compatibility
        }

        private static void RegisterSubCommands(string keyName, string exePath, (string id, string action, string label)[] actions)
        {
            // Legacy stub kept for backward compatibility
        }

        private static void UnregisterAnchor(string target)
        {
            DeleteSubTree($@"{RootKey}\{target}\shell\{MenuName}");
        }

        private static void DeleteSubTree(string subKeyPath)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
            }
            catch
            {
            }
        }
    }
}
