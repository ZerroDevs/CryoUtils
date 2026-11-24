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
            ["WindowBorderBrush"] = "#2B88D8",
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
            ["TextMutedBrush"] = "#929292",
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
            ["AccentBrush"] = "#2B88D8",
            ["AccentHoverBrush"] = "#3A9AE8",
            ["AccentPressedBrush"] = "#1E6FAE",
            ["AccentSoftBrush"] = "#1E3A4A",
            ["AccentTextBrush"] = "#6FB8F2",
            ["AccentButtonBrush"] = "#1E6FAE",
            ["AccentButtonTextBrush"] = "#FFFFFF",
            ["SearchMatchBrush"] = "#6FB8F2",
            ["CaptureAccentBrush"] = "#4FC3F7",
            ["MediaAccentBrush"] = "#7CB342",
            ["ColorAccentBrush"] = "#F3C765",
            ["UtilityAccentBrush"] = "#62B0E8",
            ["SuccessTextBrush"] = "#ABD068",
            ["SuccessBrush"] = "#ABD068",
            ["SuccessMutedBrush"] = "#223519",
            ["WarningTextBrush"] = "#F3C765",
            ["DangerTextBrush"] = "#FAAA9A",
            ["DangerSoftBackgroundBrush"] = "#3A2424",
            ["DangerSoftHoverBrush"] = "#4A2929",
            ["ToggleOnTrackBrush"] = "#1E5C91",
            ["ToggleOnThumbBrush"] = "#FFFFFF",
            ["ToggleOffTrackBrush"] = "#333333",
            ["ToggleOffThumbBrush"] = "#8A8A8A",
            ["ToggleOffTextBrush"] = "#B8B8B8",
            ["ToggleOnTextBrush"] = "#6FB8F2",
            ["OverlayBackgroundBrush"] = "#E6161616",
            ["OverlayLabelBrush"] = "#AAAAAA",
            ["OverlaySecondaryBrush"] = "#A8A8A8",
            ["OverlayMutedBrush"] = "#9A9A9A",
            ["OverlayIconBrush"] = "#888888",
            ["OverlayDimBrush"] = "#A6141414",
            ["OverlaySelectionBorderBrush"] = "#2B88D8",
            ["RecordingBorderBrush"] = "#E5534B",
            ["RecordingFillBrush"] = "#33E5534B",
            ["PinnedBorderBrush"] = "#2B88D8",
            ["AlternateRowBrush"] = "#272727",
            ["HighlightRowBrush"] = "#1E3A4A",
            ["HighlightRowBorderBrush"] = "#2B88D8",
            ["SelectionBrush"] = "#1E5C91",
            ["SelectionTextBrush"] = "#FFFFFF",
            ["FocusBrush"] = "#2B88D8",
            ["ScrollThumbBrush"] = "#4A4A4A",
            ["ScrollThumbHoverBrush"] = "#5E5E5E",
            ["ScrollTrackBrush"] = "#1A1A1A",
            ["ScanFrameBrush"] = "#2B88D8",
            ["ModuleMutedBrush"] = "#929292",
            ["HeroBackgroundBrush"] = "#1B1B1B",
            ["HeroInsetBrush"] = "#162B3D",
            ["HeroGradientStartBrush"] = "#1E4F7A",
            ["HeroGradientEndBrush"] = "#18649C"
        };

        private static readonly Dictionary<string, string> LightPalette = new()
        {
            ["WindowBorderBrush"] = "#1B6CB3",
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
            ["AccentBrush"] = "#1B6CB3",
            ["AccentHoverBrush"] = "#2B88D8",
            ["AccentPressedBrush"] = "#0F5C95",
            ["AccentSoftBrush"] = "#DCEAF6",
            ["AccentTextBrush"] = "#1B6CB3",
            ["AccentButtonBrush"] = "#1B6CB3",
            ["AccentButtonTextBrush"] = "#FFFFFF",
            ["SearchMatchBrush"] = "#155A94",
            ["CaptureAccentBrush"] = "#0B6E99",
            ["MediaAccentBrush"] = "#4C7A22",
            ["ColorAccentBrush"] = "#8A6500",
            ["UtilityAccentBrush"] = "#1D6FA5",
            ["SuccessTextBrush"] = "#4F7A16",
            ["SuccessBrush"] = "#4F7A16",
            ["SuccessMutedBrush"] = "#E4F1D5",
            ["WarningTextBrush"] = "#8A6500",
            ["DangerTextBrush"] = "#A8321B",
            ["DangerSoftBackgroundBrush"] = "#FBE7E3",
            ["DangerSoftHoverBrush"] = "#F5D6D0",
            ["ToggleOnTrackBrush"] = "#1B6CB3",
            ["ToggleOnThumbBrush"] = "#FFFFFF",
            ["ToggleOffTrackBrush"] = "#E6E0D7",
            ["ToggleOffThumbBrush"] = "#8A847A",
            ["ToggleOffTextBrush"] = "#746E64",
            ["ToggleOnTextBrush"] = "#1B6CB3",
            ["OverlayBackgroundBrush"] = "#F2F4F2EE",
            ["OverlayLabelBrush"] = "#5F5A53",
            ["OverlaySecondaryBrush"] = "#6F6962",
            ["OverlayMutedBrush"] = "#6B655C",
            ["OverlayIconBrush"] = "#7D776D",
            ["OverlayDimBrush"] = "#A6000000",
            ["OverlaySelectionBorderBrush"] = "#1B6CB3",
            ["RecordingBorderBrush"] = "#C62828",
            ["RecordingFillBrush"] = "#33C62828",
            ["PinnedBorderBrush"] = "#1B6CB3",
            ["AlternateRowBrush"] = "#F6F2EC",
            ["HighlightRowBrush"] = "#E3F1F9",
            ["HighlightRowBorderBrush"] = "#1B6CB3",
            ["SelectionBrush"] = "#DCEAF6",
            ["SelectionTextBrush"] = "#1C1A17",
            ["FocusBrush"] = "#1B6CB3",
            ["ScrollThumbBrush"] = "#C9C1B5",
            ["ScrollThumbHoverBrush"] = "#B9B1A5",
            ["ScrollTrackBrush"] = "#EFEAE2",
            ["ScanFrameBrush"] = "#1B6CB3",
            ["ModuleMutedBrush"] = "#746E64",
            ["HeroBackgroundBrush"] = "#EDEAE4",
            ["HeroInsetBrush"] = "#DCEAF6",
            ["HeroGradientStartBrush"] = "#1B5C96",
            ["HeroGradientEndBrush"] = "#1E73BA"
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
                "HeroGradientStartBrush", "HeroGradientEndBrush",
                "AccentSoftBrush", "ScrollTrackBrush", "ToggleOnTrackBrush", "SuccessMutedBrush"
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
                "SuccessTextBrush", "SuccessBrush", "WarningTextBrush", "DangerTextBrush",
                "CaptureAccentBrush", "MediaAccentBrush", "ColorAccentBrush", "UtilityAccentBrush",
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
