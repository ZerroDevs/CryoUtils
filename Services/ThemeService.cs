using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace CryoUtils.Services
{
    public static class ThemeService
    {
        private const string PersonalizeKey =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private static readonly Dictionary<string, string> DarkPalette = new()
        {
            ["WindowBorderBrush"] = "#2BB8A3",
            ["WindowBackgroundBrush"] = "#141414",
            ["TitleBarBackgroundBrush"] = "#141414",
            ["HeaderBackgroundBrush"] = "#1B1B1B",
            ["PanelBackgroundBrush"] = "#1E1E1E",
            ["PanelAltBackgroundBrush"] = "#1A1A1A",
            ["CardBackgroundBrush"] = "#242424",
            ["CardAltBackgroundBrush"] = "#262626",
            ["InsetBackgroundBrush"] = "#121212",
            ["ChartBackgroundBrush"] = "#0D0D0D",
            ["DividerBrush"] = "#262626",
            ["BorderBrush"] = "#333333",
            ["BorderStrongBrush"] = "#383838",
            ["BorderSubtleBrush"] = "#303030",
            ["InputBackgroundBrush"] = "#1E1E1E",
            ["InputBorderBrush"] = "#4A4A4A",
            ["HoverBackgroundBrush"] = "#2A2A2A",
            ["ActiveBackgroundBrush"] = "#2D2D2D",
            ["TextPrimaryBrush"] = "#FFFFFF",
            ["TextTitleBrush"] = "#D5D5D5",
            ["TextSecondaryBrush"] = "#B8B8B8",
            ["TextTertiaryBrush"] = "#9A9A9A",
            ["TextMutedBrush"] = "#8F8F8F",
            ["TextDisabledBrush"] = "#787878",
            ["TextDimBrush"] = "#A6A6A6",
            ["IconBrush"] = "#C4C4C4",
            ["TooltipBackgroundBrush"] = "#222222",
            ["TooltipBorderBrush"] = "#444444",
            ["MenuBackgroundBrush"] = "#1E1E1E",
            ["MenuBorderBrush"] = "#525252",
            ["MenuHoverBrush"] = "#2D2D2D",
            ["MenuHoverBorderBrush"] = "#6E6E6E",
            ["CheckboxBackgroundBrush"] = "#1A1A1A",
            ["CheckboxBorderBrush"] = "#4A4A4A",
            ["AccentBrush"] = "#2FD3BD",
            ["AccentHoverBrush"] = "#46E2CD",
            ["AccentPressedBrush"] = "#22A995",
            ["AccentSoftBrush"] = "#143B36",
            ["AccentTextBrush"] = "#4FE0CE",
            ["AccentButtonBrush"] = "#0F7A6C",
            ["AccentButtonTextBrush"] = "#FFFFFF",
            ["SearchMatchBrush"] = "#7FE8DA",
            ["SuccessTextBrush"] = "#ABD068",
            ["WarningTextBrush"] = "#F3C765",
            ["DangerTextBrush"] = "#FAAA9A",
            ["DangerSoftBackgroundBrush"] = "#3A2424",
            ["DangerSoftHoverBrush"] = "#4A2929",
            ["ToggleOnTrackBrush"] = "#174F47",
            ["ToggleOnThumbBrush"] = "#2FD3BD",
            ["ToggleOffTrackBrush"] = "#333333",
            ["ToggleOffThumbBrush"] = "#8A8A8A",
            ["ToggleOffTextBrush"] = "#B8B8B8",
            ["ToggleOnTextBrush"] = "#4FE0CE",
            ["OverlayBackgroundBrush"] = "#E6161616",
            ["OverlayLabelBrush"] = "#AAAAAA",
            ["OverlaySecondaryBrush"] = "#A8A8A8",
            ["OverlayMutedBrush"] = "#9A9A9A",
            ["OverlayIconBrush"] = "#888888",
            ["OverlayDimBrush"] = "#A6141414",
            ["OverlaySelectionBorderBrush"] = "#2FD3BD",
            ["RecordingBorderBrush"] = "#E5534B",
            ["RecordingFillBrush"] = "#33E5534B",
            ["PinnedBorderBrush"] = "#2FD3BD",
            ["AlternateRowBrush"] = "#272727",
            ["HighlightRowBrush"] = "#172F2C",
            ["HighlightRowBorderBrush"] = "#2FD3BD",
            ["SelectionBrush"] = "#1B4B44",
            ["SelectionTextBrush"] = "#FFFFFF",
            ["FocusBrush"] = "#2FD3BD",
            ["ScrollThumbBrush"] = "#4A4A4A",
            ["ScrollThumbHoverBrush"] = "#5E5E5E",
            ["ScrollTrackBrush"] = "#1A1A1A",
            ["ScanFrameBrush"] = "#2FD3BD",
            ["ModuleMutedBrush"] = "#8C8C8C",
            ["HeroBackgroundBrush"] = "#1B1B1B",
            ["HeroInsetBrush"] = "#101615"
        };

        private static readonly Dictionary<string, string> LightPalette = new()
        {
            ["WindowBorderBrush"] = "#0B6B5E",
            ["WindowBackgroundBrush"] = "#F5F3EF",
            ["TitleBarBackgroundBrush"] = "#F5F3EF",
            ["HeaderBackgroundBrush"] = "#EDEAE4",
            ["PanelBackgroundBrush"] = "#FFFFFF",
            ["PanelAltBackgroundBrush"] = "#FAF8F4",
            ["CardBackgroundBrush"] = "#F7F4EF",
            ["CardAltBackgroundBrush"] = "#F1EDE6",
            ["InsetBackgroundBrush"] = "#FBFAF7",
            ["ChartBackgroundBrush"] = "#FBF9F5",
            ["DividerBrush"] = "#E7E1D8",
            ["BorderBrush"] = "#DDD7CD",
            ["BorderStrongBrush"] = "#CFC7BA",
            ["BorderSubtleBrush"] = "#E6E0D7",
            ["InputBackgroundBrush"] = "#FFFFFF",
            ["InputBorderBrush"] = "#B9B1A5",
            ["HoverBackgroundBrush"] = "#F0EDE7",
            ["ActiveBackgroundBrush"] = "#E8E4DC",
            ["TextPrimaryBrush"] = "#1C1A17",
            ["TextTitleBrush"] = "#2A2723",
            ["TextSecondaryBrush"] = "#5C564D",
            ["TextTertiaryBrush"] = "#746E64",
            ["TextMutedBrush"] = "#706A60",
            ["TextDisabledBrush"] = "#A39B90",
            ["TextDimBrush"] = "#6B655C",
            ["IconBrush"] = "#4E4A43",
            ["TooltipBackgroundBrush"] = "#FFFFFF",
            ["TooltipBorderBrush"] = "#CFC7BA",
            ["MenuBackgroundBrush"] = "#FFFFFF",
            ["MenuBorderBrush"] = "#C9C1B5",
            ["MenuHoverBrush"] = "#F0EDE7",
            ["MenuHoverBorderBrush"] = "#B9B1A5",
            ["CheckboxBackgroundBrush"] = "#FFFFFF",
            ["CheckboxBorderBrush"] = "#A9A196",
            ["AccentBrush"] = "#0E7C6C",
            ["AccentHoverBrush"] = "#128C79",
            ["AccentPressedBrush"] = "#075247",
            ["AccentSoftBrush"] = "#D9F1EC",
            ["AccentTextBrush"] = "#0A6B5D",
            ["AccentButtonBrush"] = "#0B6455",
            ["AccentButtonTextBrush"] = "#FFFFFF",
            ["SearchMatchBrush"] = "#155A94",
            ["SuccessTextBrush"] = "#4F7A16",
            ["WarningTextBrush"] = "#8A6500",
            ["DangerTextBrush"] = "#A8321B",
            ["DangerSoftBackgroundBrush"] = "#FBE7E3",
            ["DangerSoftHoverBrush"] = "#F5D6D0",
            ["ToggleOnTrackBrush"] = "#BEE9E1",
            ["ToggleOnThumbBrush"] = "#0E7C6C",
            ["ToggleOffTrackBrush"] = "#E6E0D7",
            ["ToggleOffThumbBrush"] = "#A9A196",
            ["ToggleOffTextBrush"] = "#746E64",
            ["ToggleOnTextBrush"] = "#0A6B5D",
            ["OverlayBackgroundBrush"] = "#F2F4F2EE",
            ["OverlayLabelBrush"] = "#5F5A53",
            ["OverlaySecondaryBrush"] = "#6F6962",
            ["OverlayMutedBrush"] = "#6B655C",
            ["OverlayIconBrush"] = "#7D776D",
            ["OverlayDimBrush"] = "#A6000000",
            ["OverlaySelectionBorderBrush"] = "#0E7C6C",
            ["RecordingBorderBrush"] = "#C62828",
            ["RecordingFillBrush"] = "#33C62828",
            ["PinnedBorderBrush"] = "#0E7C6C",
            ["AlternateRowBrush"] = "#F6F2EC",
            ["HighlightRowBrush"] = "#E1F3EF",
            ["HighlightRowBorderBrush"] = "#0E7C6C",
            ["SelectionBrush"] = "#D9F1EC",
            ["SelectionTextBrush"] = "#1C1A17",
            ["FocusBrush"] = "#0E7C6C",
            ["ScrollThumbBrush"] = "#C9C1B5",
            ["ScrollThumbHoverBrush"] = "#B9B1A5",
            ["ScrollTrackBrush"] = "#EFEAE2",
            ["ScanFrameBrush"] = "#0E7C6C",
            ["ModuleMutedBrush"] = "#746E64",
            ["HeroBackgroundBrush"] = "#EDEAE4",
            ["HeroInsetBrush"] = "#E4EFEC"
        };

        private static bool _systemEventsHooked;
        private static bool _systemParametersHooked;
        private static bool _isHighContrast;
        private static string _configuredMode = "Dark";
        private static string _effectiveMode = "Dark";

        public static event EventHandler? ThemeChanged;

        public static bool IsLight => _effectiveMode == "Light";

        public static bool IsHighContrast => _isHighContrast;

        public static string ConfiguredMode => _configuredMode;

        public static void Initialize(string? mode)
        {
            if (!_systemEventsHooked)
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _systemEventsHooked = true;
            }

            if (!_systemParametersHooked)
            {
                SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
                _systemParametersHooked = true;
            }

            Apply(mode, force: true);
        }

        public static void Apply(string? mode, bool force = false)
        {
            string normalized = Normalize(mode);
            string effective = normalized == "System"
                ? (IsSystemLight() ? "Light" : "Dark")
                : normalized;

            bool highContrast = SystemParameters.HighContrast;
            _configuredMode = normalized;
            if (!force &&
                string.Equals(effective, _effectiveMode, StringComparison.Ordinal) &&
                highContrast == _isHighContrast)
            {
                return;
            }

            _effectiveMode = effective;
            _isHighContrast = highContrast;
            var resources = Application.Current?.Resources;
            if (resources != null)
            {
                if (highContrast)
                {
                    ApplyHighContrastPalette(resources);
                }
                else
                {
                    ApplyPalette(
                        resources,
                        effective == "Light" ? LightPalette : DarkPalette);
                }
            }

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string Normalize(string? mode)
        {
            if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase))
            {
                return "Light";
            }
            if (string.Equals(mode, "System", StringComparison.OrdinalIgnoreCase))
            {
                return "System";
            }
            return "Dark";
        }

        public static Color GetColor(string key)
        {
            if (Application.Current?.Resources[key] is SolidColorBrush brush)
            {
                return brush.Color;
            }

            return Colors.Transparent;
        }

        public static Brush GetBrush(string key)
        {
            if (Application.Current?.Resources[key] is Brush brush)
            {
                return brush;
            }

            return Brushes.Transparent;
        }

        private static void ApplyPalette(
            ResourceDictionary resources,
            IReadOnlyDictionary<string, string> palette)
        {
            foreach (var entry in palette)
            {
                var brush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(entry.Value));
                brush.Freeze();
                resources[entry.Key] = brush;
            }
        }

        private static void ApplyHighContrastPalette(ResourceDictionary resources)
        {
            var window = FrozenBrush(SystemColors.WindowColor);
            var windowText = FrozenBrush(SystemColors.WindowTextColor);
            var grayText = FrozenBrush(SystemColors.GrayTextColor);
            var control = FrozenBrush(SystemColors.ControlColor);
            var controlText = FrozenBrush(SystemColors.ControlTextColor);
            var controlDark = FrozenBrush(SystemColors.ControlDarkColor);
            var highlight = FrozenBrush(SystemColors.HighlightColor);
            var highlightText = FrozenBrush(SystemColors.HighlightTextColor);
            var hotTrack = FrozenBrush(SystemColors.HotTrackColor);

            foreach (string key in DarkPalette.Keys)
            {
                resources[key] = window;
            }

            string[] surfaceKeys =
            {
                "WindowBackgroundBrush", "TitleBarBackgroundBrush", "HeaderBackgroundBrush",
                "PanelBackgroundBrush", "PanelAltBackgroundBrush", "CardBackgroundBrush",
                "CardAltBackgroundBrush", "InsetBackgroundBrush", "ChartBackgroundBrush",
                "MenuBackgroundBrush", "TooltipBackgroundBrush", "CheckboxBackgroundBrush",
                "InputBackgroundBrush", "DangerSoftBackgroundBrush", "DangerSoftHoverBrush",
                "ToggleOffTrackBrush", "OverlayBackgroundBrush", "AlternateRowBrush",
                "HighlightRowBrush", "HeroBackgroundBrush", "HeroInsetBrush",
                "AccentSoftBrush", "ScrollTrackBrush", "ToggleOnTrackBrush"
            };
            foreach (string key in surfaceKeys)
            {
                resources[key] = key.Contains("Hover", StringComparison.Ordinal) ||
                    key.Contains("Pressed", StringComparison.Ordinal)
                    ? control
                    : window;
            }

            string[] textKeys =
            {
                "TextPrimaryBrush", "TextTitleBrush", "TextSecondaryBrush", "TextTertiaryBrush",
                "TextMutedBrush", "TextDimBrush", "IconBrush", "OverlayLabelBrush",
                "OverlaySecondaryBrush", "OverlayMutedBrush", "OverlayIconBrush",
                "SuccessTextBrush", "WarningTextBrush", "DangerTextBrush",
                "ToggleOffTextBrush", "ToggleOnTextBrush", "ModuleMutedBrush"
            };
            foreach (string key in textKeys)
            {
                resources[key] = windowText;
            }

            resources["TextDisabledBrush"] = grayText;
            resources["BorderBrush"] = controlDark;
            resources["BorderStrongBrush"] = windowText;
            resources["BorderSubtleBrush"] = controlDark;
            resources["DividerBrush"] = controlDark;
            resources["InputBorderBrush"] = windowText;
            resources["MenuBorderBrush"] = controlDark;
            resources["MenuHoverBrush"] = control;
            resources["MenuHoverBorderBrush"] = windowText;
            resources["TooltipBorderBrush"] = windowText;
            resources["CheckboxBorderBrush"] = windowText;
            resources["WindowBorderBrush"] = windowText;
            resources["HoverBackgroundBrush"] = control;
            resources["ActiveBackgroundBrush"] = control;
            resources["AccentBrush"] = highlight;
            resources["AccentHoverBrush"] = highlight;
            resources["AccentPressedBrush"] = highlight;
            resources["AccentSoftBrush"] = highlight;
            resources["AccentTextBrush"] = hotTrack;
            resources["AccentButtonBrush"] = highlight;
            resources["AccentButtonTextBrush"] = highlightText;
            resources["SearchMatchBrush"] = hotTrack;
            resources["SelectionBrush"] = highlight;
            resources["SelectionTextBrush"] = highlightText;
            resources["HighlightRowBorderBrush"] = windowText;
            resources["FocusBrush"] = highlight;
            resources["ScanFrameBrush"] = highlight;
            resources["PinnedBorderBrush"] = highlight;
            resources["OverlaySelectionBorderBrush"] = highlight;
            resources["RecordingBorderBrush"] = highlight;
            resources["ToggleOnThumbBrush"] = highlight;
            resources["ToggleOffThumbBrush"] = controlText;
            resources["ScrollThumbBrush"] = controlDark;
            resources["ScrollThumbHoverBrush"] = controlText;
        }

        private static SolidColorBrush FrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public static void Shutdown()
        {
            if (_systemEventsHooked)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _systemEventsHooked = false;
            }

            if (_systemParametersHooked)
            {
                SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
                _systemParametersHooked = false;
            }
        }

        private static void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.PropertyName) &&
                !string.Equals(e.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
            {
                return;
            }

            Application.Current?.Dispatcher.BeginInvoke(
                new Action(() => Apply(_configuredMode, force: true)));
        }

        private static bool IsSystemLight()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                object? value = key?.GetValue("AppsUseLightTheme");
                return value != null && Convert.ToInt32(value) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static void OnUserPreferenceChanged(
            object sender,
            UserPreferenceChangedEventArgs e)
        {
            bool accessibility = e.Category == UserPreferenceCategory.Accessibility;
            if (!accessibility && _configuredMode != "System")
            {
                return;
            }

            if (!accessibility &&
                e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.VisualStyle)
            {
                return;
            }

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                Apply(_configuredMode, force: accessibility)));
        }
    }
}
