using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CryoUtils.Services
{
    public static class CryoContextMenuService
    {
        private const string SystemAssocRoot = @"Software\Classes\SystemFileAssociations";
        private const string ClassesRoot = @"Software\Classes";
        private const string MenuName = "CryoUtils";
        private const string ConvertMenuName = "CryoUtilsConvert";
        private const int EcfSeparatorBefore = 0x20;

        private sealed record ContextActionNode(
            string Id,
            string? Action,
            string Label,
            bool SeparatorBefore = false,
            ContextActionNode[]? Children = null);

        private static readonly string[] PdfExts = { ".pdf" };
        private static readonly string[] ImageExts = { "image", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".ico", ".jfif" };
        private static readonly string[] GifExts = { ".gif" };
        private static readonly string[] VideoExts = { "video", ".mp4", ".mkv", ".avi", ".mov", ".webm" };
        private static readonly string[] AudioExts = { "audio", ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".aac" };
        private static readonly string[] JsonExts = { ".json" };
        private static readonly string[] XmlExts = { ".xml" };
        private static readonly string[] TextQrExts = { ".txt", ".md", ".csv" };

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"{SystemAssocRoot}\.png\shell\{MenuName}");
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

        public static void SyncIfNeeded(bool enable)
        {
            if (!enable)
            {
                if (IsEnabled())
                {
                    UnregisterAll();
                }
                return;
            }

            if (IsRegistrationCurrent())
            {
                return;
            }

            RegisterAll();
        }

        private static bool IsRegistrationCurrent()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryoUtils.exe");
                using var cmdKey = Registry.CurrentUser.OpenSubKey($@"{SystemAssocRoot}\.png\shell\{MenuName}\shell\02_quick_compress\command");
                string? registeredCmd = cmdKey?.GetValue("") as string;
                string expectedCmd = $"\"{exePath}\" --action quick-compress \"%1\"";
                if (!string.Equals(registeredCmd, expectedCmd, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                using var convertKey = Registry.CurrentUser.OpenSubKey($@"{SystemAssocRoot}\.png\shell\{ConvertMenuName}");
                string? convertLabel = convertKey?.GetValue("MUIVerb") as string;
                return string.Equals(convertLabel, LocalizationService.Get("Context_TopConvert"), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static void RegisterAll()
        {
            UnregisterAll();

            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryoUtils.exe");

            // 1. PDF (<= 10 top-level rows)
            var pdfActions = new ContextActionNode[]
            {
                new("01_pdf_png", "pdf-to-png", LocalizationService.Get("Context_PdfToPng")),
                new("02_pdf_jpg", "pdf-to-jpg", LocalizationService.Get("Context_PdfToJpg")),
                new("03_pdf_compress", "compress-pdf", LocalizationService.Get("Context_CompressPdf")),
                new("04_pdf_grayscale", "pdf-grayscale", LocalizationService.Get("Context_PdfGrayscale"), SeparatorBefore: true),
                new("05_pdf_rot90", "pdf-rotate-90", LocalizationService.Get("Context_PdfRotate90")),
                new("06_pdf_rot180", "pdf-rotate-180", LocalizationService.Get("Context_PdfRotate180")),
                new("90_upload", "upload-file", LocalizationService.Get("Context_UploadLink"), SeparatorBefore: true),
            };
            foreach (var ext in PdfExts)
            {
                RegisterMenuAtPath($@"{SystemAssocRoot}\{ext}\shell\{MenuName}", exePath, pdfActions);
            }

            // 2. Images + GIF
            // Windows Explorer enforces a hard cap of 16 total static subcommands per verb tree.
            // Splitting conversions into a sibling "CryoUtils Convert" verb keeps every menu under 16 and <= 10 top rows.
            foreach (var ext in ImageExts)
            {
                RegisterMenuAtPath($@"{SystemAssocRoot}\{ext}\shell\{MenuName}", exePath, BuildImageToolsActions(), "CryoUtils");
                RegisterMenuAtPath($@"{SystemAssocRoot}\{ext}\shell\{ConvertMenuName}", exePath, BuildImageConvertActions(includeGifFrames: false), LocalizationService.Get("Context_TopConvert"));
            }
            foreach (var ext in GifExts)
            {
                RegisterMenuAtPath($@"{SystemAssocRoot}\{ext}\shell\{MenuName}", exePath, BuildImageToolsActions(), "CryoUtils");
                RegisterMenuAtPath($@"{SystemAssocRoot}\{ext}\shell\{ConvertMenuName}", exePath, BuildImageConvertActions(includeGifFrames: true), LocalizationService.Get("Context_TopConvert"));
            }

            // 3. Video (<= 10 top-level rows)
            var videoActions = new ContextActionNode[]
            {
                new("01_video_compress", "video-compress", LocalizationService.Get("Context_VideoCompress")),
                new("02_video_gif", "video-to-gif", LocalizationService.Get("Context_VideoToGif")),
                new("03_video_mp3", "video-to-mp3", LocalizationService.Get("Context_VideoToMp3")),
                new("04_video_mute", "video-mute", LocalizationService.Get("Context_VideoMute"), SeparatorBefore: true),
                new("05_video_thumb", "video-thumbnail", LocalizationService.Get("Context_VideoThumbnail")),
                new("90_upload", "upload-file", LocalizationService.Get("Context_UploadLink"), SeparatorBefore: true),
            };
            foreach (var ext in VideoExts)
            {
                RegisterMenuAtPath($@"{SystemAssocRoot}\{ext}\shell\{MenuName}", exePath, videoActions);
            }

            // 4. Audio (<= 10 top-level rows)
            var audioActions = new ContextActionNode[]
            {
                new("01_audio_mp3", "audio-to-mp3", LocalizationService.Get("Context_AudioToMp3")),
                new("02_audio_wav", "audio-to-wav", LocalizationService.Get("Context_AudioToWav")),
                new("03_audio_m4a", "audio-to-m4a", LocalizationService.Get("Context_AudioToM4a")),
                new("90_upload", "upload-file", LocalizationService.Get("Context_UploadLink"), SeparatorBefore: true),
            };
            foreach (var ext in AudioExts)
            {
                RegisterMenuAtPath($@"{SystemAssocRoot}\{ext}\shell\{MenuName}", exePath, audioActions);
            }

            // 5. Data / Text files
            var jsonActions = new ContextActionNode[]
            {
                new("01_json_pretty", "json-pretty", LocalizationService.Get("Context_JsonPretty")),
                new("02_json_minify", "json-minify", LocalizationService.Get("Context_JsonMinify")),
                new("03_text_qr", "text-to-qr", LocalizationService.Get("Context_TextToQr"), SeparatorBefore: true),
                new("90_upload", "upload-file", LocalizationService.Get("Context_UploadLink"), SeparatorBefore: true),
                new("91_sha256", "hash-sha256", LocalizationService.Get("Context_CopySha256")),
            };
            foreach (var ext in JsonExts)
            {
                RegisterMenuAtPath($@"{SystemAssocRoot}\{ext}\shell\{MenuName}", exePath, jsonActions);
            }

            var xmlActions = new ContextActionNode[]
            {
                new("01_xml_pretty", "xml-pretty", LocalizationService.Get("Context_XmlPretty")),
                new("02_text_qr", "text-to-qr", LocalizationService.Get("Context_TextToQr"), SeparatorBefore: true),
                new("90_upload", "upload-file", LocalizationService.Get("Context_UploadLink"), SeparatorBefore: true),
                new("91_sha256", "hash-sha256", LocalizationService.Get("Context_CopySha256")),
            };
            foreach (var ext in XmlExts)
            {
                RegisterMenuAtPath($@"{SystemAssocRoot}\{ext}\shell\{MenuName}", exePath, xmlActions);
            }

            var textActions = new ContextActionNode[]
            {
                new("01_text_qr", "text-to-qr", LocalizationService.Get("Context_TextToQr")),
                new("90_upload", "upload-file", LocalizationService.Get("Context_UploadLink"), SeparatorBefore: true),
                new("91_sha256", "hash-sha256", LocalizationService.Get("Context_CopySha256")),
            };
            foreach (var ext in TextQrExts)
            {
                RegisterMenuAtPath($@"{SystemAssocRoot}\{ext}\shell\{MenuName}", exePath, textActions);
            }

            // 6. All Files (*)
            var allFileActions = new ContextActionNode[]
            {
                new("90_upload", "upload-file", LocalizationService.Get("Context_UploadLink")),
                new("91_sha256", "hash-sha256", LocalizationService.Get("Context_CopySha256"), SeparatorBefore: true),
                new("92_md5", "hash-md5", LocalizationService.Get("Context_CopyMd5")),
            };
            RegisterMenuAtPath($@"{ClassesRoot}\*\shell\{MenuName}", exePath, allFileActions);

            // 7. Folders (Directory)
            var directoryActions = new ContextActionNode[]
            {
                new("01_dir_pdf", "dir-imgs-pdf", LocalizationService.Get("Context_DirImgsToPdf")),
                new("02_dir_gif", "dir-imgs-gif", LocalizationService.Get("Context_DirImgsToGif")),
                new("03_dir_compress", "dir-compress-imgs", LocalizationService.Get("Context_DirCompressImgs"), SeparatorBefore: true),
            };
            RegisterMenuAtPath($@"{ClassesRoot}\Directory\shell\{MenuName}", exePath, directoryActions);

            NativeMethods.NotifyShellAssociationChanged();
        }

        private static ContextActionNode[] BuildImageConvertActions(bool includeGifFrames)
        {
            return includeGifFrames
                ? new ContextActionNode[]
                {
                    new("01_convert_png", "convert-to-png", LocalizationService.Get("Context_ConvertToPng")),
                    new("02_convert_jpg", "convert-to-jpg", LocalizationService.Get("Context_ConvertToJpg")),
                    new("03_convert_webp", "convert-to-webp", LocalizationService.Get("Context_ConvertToWebp")),
                    new("04_convert_ico", "convert-to-ico", LocalizationService.Get("Context_ConvertToIco")),
                    new("05_img_to_pdf", "img-to-pdf", LocalizationService.Get("Context_ImgToPdf")),
                    new("06_flatten_white", "flatten-white", LocalizationService.Get("Context_FlattenWhite")),
                    new("07_gif_frames", "gif-frames", LocalizationService.Get("Context_ExtractGifFrames"), SeparatorBefore: true),
                    new("08_copy_base64", "copy-base64", LocalizationService.Get("Context_CopyBase64"), SeparatorBefore: true),
                }
                : new ContextActionNode[]
                {
                    new("01_convert_png", "convert-to-png", LocalizationService.Get("Context_ConvertToPng")),
                    new("02_convert_jpg", "convert-to-jpg", LocalizationService.Get("Context_ConvertToJpg")),
                    new("03_convert_webp", "convert-to-webp", LocalizationService.Get("Context_ConvertToWebp")),
                    new("04_convert_ico", "convert-to-ico", LocalizationService.Get("Context_ConvertToIco")),
                    new("05_img_to_pdf", "img-to-pdf", LocalizationService.Get("Context_ImgToPdf")),
                    new("06_flatten_white", "flatten-white", LocalizationService.Get("Context_FlattenWhite")),
                    new("08_copy_base64", "copy-base64", LocalizationService.Get("Context_CopyBase64"), SeparatorBefore: true),
                };
        }

        private static ContextActionNode[] BuildImageToolsActions()
        {
            var resizeChildren = new ContextActionNode[]
            {
                new("01_resize_50", "resize-50", LocalizationService.Get("Context_Resize50")),
                new("02_resize_1920", "resize-1920", LocalizationService.Get("Context_Resize1920")),
                new("03_resize_512", "resize-512", LocalizationService.Get("Context_Resize512")),
            };

            return new ContextActionNode[]
            {
                new("01_group_resize", null, LocalizationService.Get("Context_GroupResize"), Children: resizeChildren),
                new("02_quick_compress", "quick-compress", LocalizationService.Get("Context_QuickCompress")),
                new("03_remove_bg", "remove-bg", LocalizationService.Get("Context_RemoveBg"), SeparatorBefore: true),
                new("04_trim_alpha", "trim-alpha", LocalizationService.Get("Context_TrimAlpha")),
                new("05_strip_exif", "strip-exif", LocalizationService.Get("Context_StripExif")),
                new("06_ocr", "ocr-text", LocalizationService.Get("Context_ExtractOcr"), SeparatorBefore: true),
                new("07_scan_qr", "scan-qr", LocalizationService.Get("Context_ScanQr")),
                new("08_copy_pixels", "copy-pixels", LocalizationService.Get("Context_CopyPixels"), SeparatorBefore: true),
                new("09_pin", "pin-image", LocalizationService.Get("Context_PinImage")),
                new("90_upload", "upload-file", LocalizationService.Get("Context_UploadLink"), SeparatorBefore: true),
            };
        }

        public static void UnregisterAll()
        {
            foreach (var ext in PdfExts) UnregisterSystemAssoc(ext);
            foreach (var ext in ImageExts) UnregisterSystemAssoc(ext);
            foreach (var ext in GifExts) UnregisterSystemAssoc(ext);
            foreach (var ext in VideoExts) UnregisterSystemAssoc(ext);
            foreach (var ext in AudioExts) UnregisterSystemAssoc(ext);
            foreach (var ext in JsonExts) UnregisterSystemAssoc(ext);
            foreach (var ext in XmlExts) UnregisterSystemAssoc(ext);
            foreach (var ext in TextQrExts) UnregisterSystemAssoc(ext);

            DeleteSubTree($@"{ClassesRoot}\*\shell\{MenuName}");
            DeleteSubTree($@"{ClassesRoot}\Directory\shell\{MenuName}");

            // Clean up legacy keys if present
            DeleteSubTree($@"{ClassesRoot}\CryoUtils.Menu.Pdf");
            DeleteSubTree($@"{ClassesRoot}\CryoUtils.Menu.Image");
            DeleteSubTree($@"{ClassesRoot}\CryoUtils.Menu.Video");

            NativeMethods.NotifyShellAssociationChanged();
        }

        private static void RegisterMenuAtPath(
            string menuKeyPath,
            string exePath,
            ContextActionNode[] actions,
            string rootTitle = MenuName)
        {
            try
            {
                using var menuKey = Registry.CurrentUser.CreateSubKey(menuKeyPath);
                if (menuKey == null) return;

                menuKey.SetValue("MUIVerb", rootTitle);
                menuKey.SetValue("Icon", $"\"{exePath}\",0");
                menuKey.SetValue("SubCommands", "");

                try { menuKey.DeleteValue("", throwOnMissingValue: false); } catch { }
                try { menuKey.DeleteValue("ExtendedSubCommandsKey", throwOnMissingValue: false); } catch { }

                using var shellKey = menuKey.CreateSubKey("shell");
                if (shellKey == null) return;

                WriteActionNodes(shellKey, exePath, actions);
            }
            catch
            {
            }
        }

        private static void WriteActionNodes(RegistryKey shellKey, string exePath, ContextActionNode[] nodes)
        {
            foreach (var node in nodes)
            {
                using var actionKey = shellKey.CreateSubKey(node.Id);
                if (actionKey == null) continue;

                actionKey.SetValue("MUIVerb", node.Label);
                actionKey.SetValue("Icon", $"\"{exePath}\",0");
                try { actionKey.DeleteValue("", throwOnMissingValue: false); } catch { }

                if (node.SeparatorBefore)
                {
                    actionKey.SetValue("CommandFlags", EcfSeparatorBefore, RegistryValueKind.DWord);
                }

                if (node.Children is { Length: > 0 })
                {
                    actionKey.SetValue("SubCommands", "");
                    using var childShell = actionKey.CreateSubKey("shell");
                    if (childShell != null)
                    {
                        WriteActionNodes(childShell, exePath, node.Children);
                    }
                }
                else if (!string.IsNullOrEmpty(node.Action))
                {
                    using var cmdKey = actionKey.CreateSubKey("command");
                    cmdKey?.SetValue("", $"\"{exePath}\" --action {node.Action} \"%1\"");
                }
            }
        }

        private static void UnregisterSystemAssoc(string target)
        {
            DeleteSubTree($@"{SystemAssocRoot}\{target}\shell\{MenuName}");
            DeleteSubTree($@"{SystemAssocRoot}\{target}\shell\{ConvertMenuName}");
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
