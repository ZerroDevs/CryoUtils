using System;
using System.Collections.Generic;

namespace CryoUtils.Models
{
    public class AppSettings
    {
        public const string DefaultAnnotationColor = "#E5534B";

        // General
        public string Language { get; set; } = "English"; // "English" or "العربية"
        public string Theme { get; set; } = "Dark"; // "Dark", "Light" or "System"
        public bool StartWithWindows { get; set; } = false;
        public bool LaunchInBackground { get; set; } = true;
        public string TrayLeftClickAction { get; set; } = "Dashboard"; // "Dashboard" or "Capture"
        public bool NotificationsEnabled { get; set; } = true;
        public bool ShowPinnedBorder { get; set; } = true;
        public bool PinnedBorderSound { get; set; } = false;
        public int PinnedOpacity { get; set; } = 100;
        public bool PinnedClickThrough { get; set; } = false;

        // Module switches
        public bool CaptureEnabled { get; set; } = true;
        public bool ColorPickerEnabled { get; set; } = true;
        public bool WindowPinnerEnabled { get; set; } = true;
        public bool CompressorEnabled { get; set; } = true;
        public bool ClipboardHistoryEnabled { get; set; } = false;

        // Hotkeys
        public string CaptureHotkeyModifiers { get; set; } = "";
        public string CaptureHotkeyKey { get; set; } = "PrintScreen";
        public string ColorPickerHotkeyModifiers { get; set; } = "Ctrl+Shift";
        public string ColorPickerHotkeyKey { get; set; } = "C";
        public string WindowPinnerHotkeyModifiers { get; set; } = "Ctrl+Win";
        public string WindowPinnerHotkeyKey { get; set; } = "T";
        public string PlainPasteHotkeyModifiers { get; set; } = "Ctrl+Shift";
        public string PlainPasteHotkeyKey { get; set; } = "V";
        public string ClipboardHotkeyModifiers { get; set; } = "Ctrl+Shift";
        public string ClipboardHotkeyKey { get; set; } = "H";
        public bool PrintScreenFallbackSuggested { get; set; } = false;

        // Capture
        public string SaveFolder { get; set; } = "";
        public bool QuickSave { get; set; } = false;
        public string ImageFormat { get; set; } = "PNG"; // PNG, JPG, WEBP
        public bool AutoCopyCapture { get; set; } = true;
        public bool CaptureSound { get; set; } = false;
        public bool ShowCaptureLoupe { get; set; } = false;
        public int CaptureDelaySeconds { get; set; } = 0;
        public string AnnotationColor { get; set; } = DefaultAnnotationColor;
        public string ColorFormat { get; set; } = "HEX";
        public int AnnotationThickness { get; set; } = 3;
        public int StepStartNumber { get; set; } = 1;

        // GIF / recorder
        public int GifFps { get; set; } = 15;
        public string RecorderFormat { get; set; } = "GIF"; // GIF or MP4
        public int MaxRecordingSeconds { get; set; } = 30;
        public bool RecorderCopyOnStop { get; set; } = true;

        // Tools
        public string CompressorPreset { get; set; } = "Discord";
        public int CompressorQuality { get; set; } = 82;
        public int CompressorScale { get; set; } = 100;
        public List<string> RecentColors { get; set; } = new();

        // Clipboard
        public bool ClipboardAutoClearSensitive { get; set; } = true;
        public int ClipboardAutoClearSeconds { get; set; } = 60;
        public int ClipboardHistoryLimit { get; set; } = 40;

        // Updates
        public bool AutomaticUpdateChecks { get; set; } = true;
        public bool SaveRollbackOnUpdate { get; set; } = true;
        public DateTime? LastUpdateCheckUtc { get; set; }
        public string LastUpdateOutcome { get; set; } = "";
        public string LastUpdateVersion { get; set; } = "";
        public string LastUpdateDetail { get; set; } = "";
        public string LastNotifiedUpdateVersion { get; set; } = "";

        // Window
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
    }
}
