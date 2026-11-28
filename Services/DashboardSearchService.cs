using System;
using System.Collections.Generic;
using System.Linq;

namespace CryoUtils.Services
{
    public sealed class DashboardSearchEntry
    {
        public string Id { get; init; } = "";
        public string TitleKey { get; init; } = "";
        public string DescriptionKey { get; init; } = "";
        public string Tab { get; init; } = "Overview";
        public string? SubTab { get; init; }
        public string? Keywords { get; init; }
        public string? ActionId { get; init; }
        public bool AvailableInLite { get; init; } = true;
    }

    public sealed class DashboardSearchHit
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Category { get; init; } = "";
        public string Tab { get; init; } = "Overview";
        public string? SubTab { get; init; }
        public string? ActionId { get; init; }
    }

    /// <summary>Indexed command and setting catalog for the CryoTray-style Ctrl+K header search.</summary>
    public static class DashboardSearchService
    {
        private static readonly IReadOnlyList<DashboardSearchEntry> Entries = new[]
        {
            new DashboardSearchEntry { Id = "act-capture", TitleKey = "Main_CaptureNow", DescriptionKey = "Module_CaptureDesc", Tab = "Overview", Keywords = "screenshot printscreen crop annotate f10", ActionId = "capture" },
            new DashboardSearchEntry { Id = "act-snap", TitleKey = "Hotkeys_SnapWindow", DescriptionKey = "Hotkeys_SnapWindowDesc", Tab = "Overview", Keywords = "window active 0ms snap alt", ActionId = "snap" },
            new DashboardSearchEntry { Id = "act-record", TitleKey = "Main_RecordNow", DescriptionKey = "Capture_GifTitle", Tab = "Overview", Keywords = "gif mp4 video record screen", ActionId = "record", AvailableInLite = false },
            new DashboardSearchEntry { Id = "act-color", TitleKey = "Module_ColorName", DescriptionKey = "Module_ColorDesc", Tab = "Overview", Keywords = "eyedropper picker hex rgb hsl", ActionId = "color", AvailableInLite = false },
            new DashboardSearchEntry { Id = "act-pin", TitleKey = "Module_PinnerName", DescriptionKey = "Module_PinnerDesc", Tab = "Overview", Keywords = "topmost pin ghost clickthrough", ActionId = "pin", AvailableInLite = false },
            new DashboardSearchEntry { Id = "act-clip", TitleKey = "Module_ClipboardName", DescriptionKey = "Module_ClipboardDesc", Tab = "Overview", Keywords = "clipboard paste plain snippets", ActionId = "clipboard", AvailableInLite = false },
            new DashboardSearchEntry { Id = "act-qr", TitleKey = "Tools_QrTitle", DescriptionKey = "Tools_QrDesc", Tab = "Tools", Keywords = "qr barcode scan generate", AvailableInLite = false },

            new DashboardSearchEntry { Id = "cap-quicksave", TitleKey = "Capture_QuickSave", DescriptionKey = "Capture_QuickSaveDesc", Tab = "Capture", Keywords = "auto save folder png jpg webp" },
            new DashboardSearchEntry { Id = "cap-hover", TitleKey = "Capture_HoverHighlight", DescriptionKey = "Capture_HoverHighlightDesc", Tab = "Capture", Keywords = "window hover snap highlight border space" },
            new DashboardSearchEntry { Id = "cap-instant", TitleKey = "Capture_InstantCopy", DescriptionKey = "Capture_InstantCopyDesc", Tab = "Capture", Keywords = "1 motion instant copy release drag" },
            new DashboardSearchEntry { Id = "cap-coords", TitleKey = "Capture_CursorCoords", DescriptionKey = "Capture_SettingsTitle", Tab = "Capture", Keywords = "crosshair coordinates xy pixel ruler hover info badge window size" },
            new DashboardSearchEntry { Id = "cap-cursor", TitleKey = "Capture_MouseCursor", DescriptionKey = "Capture_MouseCursorDesc", Tab = "Capture", Keywords = "mouse cursor pointer screenshot arrow capture include" },
            new DashboardSearchEntry { Id = "cap-rightclick", TitleKey = "Capture_RightClickAction", DescriptionKey = "Capture_RightClickActionDesc", Tab = "Capture", Keywords = "right click back close context menu copy undo mouse" },
            new DashboardSearchEntry { Id = "cap-ocrlang", TitleKey = "Capture_OcrLanguage", DescriptionKey = "Capture_OcrNote", Tab = "Capture", Keywords = "ocr text recognition language arabic english" },
            new DashboardSearchEntry { Id = "cap-prettyframe", TitleKey = "Capture_PrettyFrame", DescriptionKey = "Capture_PrettyFrameDesc", Tab = "Capture", Keywords = "frame rounded shadow padding export" },
            new DashboardSearchEntry { Id = "cap-loupe", TitleKey = "Capture_Loupe", DescriptionKey = "Capture_LoupeDesc", Tab = "Capture", Keywords = "magnifier zoom pixel" },
            new DashboardSearchEntry { Id = "cap-format", TitleKey = "Capture_ImageFormat", DescriptionKey = "Capture_SettingsTitle", Tab = "Capture", Keywords = "png jpg webp format quality" },
            new DashboardSearchEntry { Id = "cap-delay", TitleKey = "Capture_Delay", DescriptionKey = "Capture_SettingsTitle", Tab = "Capture", Keywords = "countdown timer delay 3s 5s 10s" },
            new DashboardSearchEntry { Id = "cap-profile", TitleKey = "Profile_MenuTitle", DescriptionKey = "Profile_CycleHint", Tab = "Overview", Keywords = "profile preset discord bugreport ocr compression" },
            new DashboardSearchEntry { Id = "cap-recorder", TitleKey = "Capture_GifTitle", DescriptionKey = "Rec_SettingsTitle", Tab = "Capture", Keywords = "fps mp4 gif bitrate webcam audio", AvailableInLite = false },

            new DashboardSearchEntry { Id = "tool-compress", TitleKey = "Tools_CompressorTitle", DescriptionKey = "Tools_CompressorDesc", Tab = "Tools", Keywords = "compress shrink discord whatsapp image gif", AvailableInLite = false },
            new DashboardSearchEntry { Id = "tool-contrast", TitleKey = "Tools_ColorStudio", DescriptionKey = "Tools_ContrastDesc", Tab = "Tools", Keywords = "wcag contrast color ratio", AvailableInLite = false },
            new DashboardSearchEntry { Id = "tool-livedraw", TitleKey = "Tools_LiveDraw", DescriptionKey = "Tools_WorkspaceDesc", Tab = "Tools", Keywords = "draw ink annotate desktop", ActionId = "livedraw", AvailableInLite = false },
            new DashboardSearchEntry { Id = "tool-ruler", TitleKey = "Tools_ScreenRuler", DescriptionKey = "Tools_WorkspaceDesc", Tab = "Tools", Keywords = "ruler measure pixel align", ActionId = "ruler", AvailableInLite = false },
            new DashboardSearchEntry { Id = "tool-shelf", TitleKey = "Tools_FileShelf", DescriptionKey = "Tools_WorkspaceDesc", Tab = "Tools", Keywords = "shelf drag drop pdf merge", ActionId = "shelf", AvailableInLite = false },
            new DashboardSearchEntry { Id = "tool-editor", TitleKey = "Tools_ImageEditor", DescriptionKey = "Tools_WorkspaceDesc", Tab = "Tools", Keywords = "edit crop rotate resize", ActionId = "editor", AvailableInLite = false },
            new DashboardSearchEntry { Id = "tool-dev", TitleKey = "Tools_Developer", DescriptionKey = "Tools_WorkspaceDesc", Tab = "Tools", Keywords = "json xml base64 sha256 guid regex", ActionId = "dev", AvailableInLite = false },

            new DashboardSearchEntry { Id = "ws-library", TitleKey = "PowerTools_Library", DescriptionKey = "PowerTools_Subtitle", Tab = "Workspace", SubTab = "Library", Keywords = "library history tags favorites search", AvailableInLite = false },
            new DashboardSearchEntry { Id = "ws-workflows", TitleKey = "PowerTools_Workflows", DescriptionKey = "PowerTools_Subtitle", Tab = "Workspace", SubTab = "Workflows", Keywords = "recipe automation template app rule", AvailableInLite = false },
            new DashboardSearchEntry { Id = "ws-privacy", TitleKey = "PowerTools_Privacy", DescriptionKey = "Capture_CloudUploadDesc", Tab = "Workspace", SubTab = "Privacy", Keywords = "privacy upload catbox expiry consent", AvailableInLite = false },
            new DashboardSearchEntry { Id = "ws-files", TitleKey = "PowerTools_FileActions", DescriptionKey = "Tools_WorkspaceDesc", Tab = "Workspace", SubTab = "FileActions", Keywords = "queue batch convert recycle", AvailableInLite = false },
            new DashboardSearchEntry { Id = "ws-color", TitleKey = "PowerTools_Color", DescriptionKey = "Tools_ContrastDesc", Tab = "Workspace", SubTab = "Color", Keywords = "palette css tailwind colorblind", AvailableInLite = false },
            new DashboardSearchEntry { Id = "ws-media", TitleKey = "PowerTools_Media", DescriptionKey = "PowerTools_Subtitle", Tab = "Workspace", SubTab = "Media", Keywords = "pdf split merge ocr video mp3", AvailableInLite = false },
            new DashboardSearchEntry { Id = "ws-pinner", TitleKey = "PowerTools_Pinner", DescriptionKey = "Module_PinnerDesc", Tab = "Workspace", SubTab = "Pinner", Keywords = "pinned opacity restore clickthrough ghost", AvailableInLite = false },
            new DashboardSearchEntry { Id = "ws-diag", TitleKey = "PowerTools_Diagnostics", DescriptionKey = "PowerTools_Subtitle", Tab = "Workspace", SubTab = "Diagnostics", Keywords = "logs diagnostics safemode crash", AvailableInLite = false },

            new DashboardSearchEntry { Id = "hotkeys-all", TitleKey = "Hotkeys_Title", DescriptionKey = "Hotkeys_Desc", Tab = "Hotkeys", Keywords = "shortcut hotkey keybind printscreen" },

            new DashboardSearchEntry { Id = "set-lite", TitleKey = "General_ScreenshotOnly", DescriptionKey = "General_ScreenshotOnlyDesc", Tab = "Settings", Keywords = "lite screenshot only memory ram" },
            new DashboardSearchEntry { Id = "set-theme", TitleKey = "General_Theme", DescriptionKey = "General_Appearance", Tab = "Settings", Keywords = "dark light system appearance" },
            new DashboardSearchEntry { Id = "set-accent", TitleKey = "General_Accent", DescriptionKey = "General_Appearance", Tab = "Settings", Keywords = "accent color blue cyan emerald violet amber rose" },
            new DashboardSearchEntry { Id = "set-lang", TitleKey = "General_Language", DescriptionKey = "General_Appearance", Tab = "Settings", Keywords = "english arabic rtl language" },
            new DashboardSearchEntry { Id = "set-tray", TitleKey = "General_TrayClick", DescriptionKey = "General_PrefsTitle", Tab = "Settings", Keywords = "tray click" },
            new DashboardSearchEntry { Id = "set-startup", TitleKey = "General_Startup", DescriptionKey = "General_StartupDesc", Tab = "Settings", Keywords = "run startup windows boot autostart" },
            new DashboardSearchEntry { Id = "set-background", TitleKey = "General_Background", DescriptionKey = "General_BackgroundDesc", Tab = "Settings", Keywords = "run background tray minimize idle" },
            new DashboardSearchEntry { Id = "set-notify", TitleKey = "General_DisableNotifications", DescriptionKey = "General_DisableNotificationsDesc", Tab = "Settings", Keywords = "disable all notifications toast mute silence alerts" },
            new DashboardSearchEntry { Id = "set-anchor", TitleKey = "General_AnchorToTray", DescriptionKey = "General_PrefsTitle", Tab = "Settings", Keywords = "anchor dock tray taskbar position corner" },
            new DashboardSearchEntry { Id = "set-ctx", TitleKey = "General_ContextMenuTitle", DescriptionKey = "General_ContextMenuDesc", Tab = "Settings", Keywords = "explorer right click context menu" },
            new DashboardSearchEntry { Id = "set-limit", TitleKey = "General_SavedLimit", DescriptionKey = "General_SavedLimitDesc", Tab = "Settings", Keywords = "retention limit recycle prune" },
            new DashboardSearchEntry { Id = "set-update", TitleKey = "About_Updates", DescriptionKey = "About_UpdatesDesc", Tab = "Settings", Keywords = "update sha256 check rollback backup" },
            new DashboardSearchEntry { Id = "set-backup", TitleKey = "General_DataTitle", DescriptionKey = "General_DataDesc", Tab = "Settings", Keywords = "export import reset settings json" }
        };

        public static IReadOnlyList<DashboardSearchHit> Search(string? query, bool screenshotOnly, int limit = 12)
        {
            string trimmed = (query ?? "").Trim();
            var candidates = Entries.Where(e => !screenshotOnly || e.AvailableInLite);

            if (string.IsNullOrEmpty(trimmed))
            {
                return candidates
                    .Take(Math.Min(limit, 8))
                    .Select(ToHit)
                    .ToList();
            }

            string[] terms = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(int Score, DashboardSearchHit Hit)>();

            foreach (var entry in candidates)
            {
                string title = LocalizationService.Get(entry.TitleKey);
                string desc = LocalizationService.Get(entry.DescriptionKey);
                string haystack = $"{title} {desc} {entry.Keywords ?? ""} {entry.Tab} {entry.SubTab ?? ""}";

                int score = 0;
                bool matchedAll = true;
                foreach (string term in terms)
                {
                    if (title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 120;
                    }
                    else if (title.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 80;
                    }
                    else if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 40;
                    }
                    else
                    {
                        matchedAll = false;
                        break;
                    }
                }

                if (matchedAll)
                {
                    scored.Add((score, ToHit(entry)));
                }
            }

            return scored
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Hit.Title, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(x => x.Hit)
                .ToList();
        }

        private static DashboardSearchHit ToHit(DashboardSearchEntry entry)
        {
            string tabLabelKey = entry.Tab switch
            {
                "Overview" => "Nav_Overview",
                "Capture" => "Nav_Capture",
                "Tools" => "Nav_Tools",
                "Workspace" => "Nav_Workspace",
                "Hotkeys" => "Nav_Hotkeys",
                "Settings" => "Nav_General",
                _ => "Nav_Overview"
            };
            return new DashboardSearchHit
            {
                Id = entry.Id,
                Title = LocalizationService.Get(entry.TitleKey),
                Category = LocalizationService.Get(tabLabelKey),
                Tab = entry.Tab,
                SubTab = entry.SubTab,
                ActionId = entry.ActionId
            };
        }
    }
}
