using System;
using System.Windows;
using System.Windows.Controls;
using CryoUtils.Models;
using CryoUtils.Services;

namespace CryoUtils.Views.Tabs
{
    public partial class HotkeysTabView : UserControl
    {
        public event Action? HotkeysChanged;

        public HotkeysTabView()
        {
            InitializeComponent();
        }

        public void RefreshRows()
        {
            HotkeyRowsPanel.Children.Clear();
            var settings = SettingsService.Instance.Current;
            bool lite = settings.ScreenshotOnlyMode;

            AddRow(
                LocalizationService.Get("Hotkeys_Capture"),
                LocalizationService.Get("Module_CaptureDesc"),
                settings.CaptureHotkeyModifiers,
                settings.CaptureHotkeyKey,
                enabled: true,
                (modifiers, key) =>
                {
                    settings.CaptureHotkeyModifiers = modifiers;
                    settings.CaptureHotkeyKey = key;
                });

            AddRow(
                LocalizationService.Get("Hotkeys_ColorPicker"),
                LocalizationService.Get("Module_ColorDesc"),
                settings.ColorPickerHotkeyModifiers,
                settings.ColorPickerHotkeyKey,
                enabled: !lite,
                (modifiers, key) =>
                {
                    settings.ColorPickerHotkeyModifiers = modifiers;
                    settings.ColorPickerHotkeyKey = key;
                });

            AddRow(
                LocalizationService.Get("Hotkeys_Pinner"),
                LocalizationService.Get("Module_PinnerDesc"),
                settings.WindowPinnerHotkeyModifiers,
                settings.WindowPinnerHotkeyKey,
                enabled: !lite,
                (modifiers, key) =>
                {
                    settings.WindowPinnerHotkeyModifiers = modifiers;
                    settings.WindowPinnerHotkeyKey = key;
                });

            AddRow(
                LocalizationService.Get("Hotkeys_PlainPaste"),
                LocalizationService.Get("Module_ClipboardDesc"),
                settings.PlainPasteHotkeyModifiers,
                settings.PlainPasteHotkeyKey,
                enabled: !lite,
                (modifiers, key) =>
                {
                    settings.PlainPasteHotkeyModifiers = modifiers;
                    settings.PlainPasteHotkeyKey = key;
                });

            AddRow(
                LocalizationService.Get("Hotkeys_History"),
                LocalizationService.Get("Module_ClipboardDesc"),
                settings.ClipboardHotkeyModifiers,
                settings.ClipboardHotkeyKey,
                enabled: !lite,
                (modifiers, key) =>
                {
                    settings.ClipboardHotkeyModifiers = modifiers;
                    settings.ClipboardHotkeyKey = key;
                });

            AddRow(
                LocalizationService.Get("Hotkeys_Workbench"),
                LocalizationService.Get("PowerTools_Subtitle"),
                settings.WorkbenchHotkeyModifiers,
                settings.WorkbenchHotkeyKey,
                enabled: !lite,
                (modifiers, key) =>
                {
                    settings.WorkbenchHotkeyModifiers = modifiers;
                    settings.WorkbenchHotkeyKey = key;
                });

            AddRow(
                LocalizationService.Get("Hotkeys_LiveDraw"),
                LocalizationService.Get("Tools_LiveDrawDesc"),
                settings.LiveDrawHotkeyModifiers,
                settings.LiveDrawHotkeyKey,
                enabled: !lite,
                (modifiers, key) =>
                {
                    settings.LiveDrawHotkeyModifiers = modifiers;
                    settings.LiveDrawHotkeyKey = key;
                });

            AddRow(
                LocalizationService.Get("Hotkeys_Replay"),
                LocalizationService.Get("Rec_ReplayHotkeyHint"),
                settings.ReplayHotkeyModifiers,
                settings.ReplayHotkeyKey,
                enabled: !lite,
                (modifiers, key) =>
                {
                    settings.ReplayHotkeyModifiers = modifiers;
                    settings.ReplayHotkeyKey = key;
                });
        }

        private void AddRow(
            string title,
            string description,
            string modifiers,
            string key,
            bool enabled,
            Action<string, string> apply)
        {
            var row = new Border
            {
                BorderBrush = ThemeService.GetBrush("DividerBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 10, 12, 10),
                Opacity = enabled ? 1.0 : 0.45,
                IsEnabled = enabled
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = title,
                Style = (Style)FindResource("CardTitle")
            });
            textStack.Children.Add(new TextBlock
            {
                Text = enabled ? description : LocalizationService.Get("Module_PausedInLite"),
                Style = (Style)FindResource("MutedText"),
                Margin = new Thickness(0, 3, 0, 0),
                MaxWidth = 500,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            grid.Children.Add(textStack);

            var pill = new Border
            {
                Style = (Style)FindResource("HotkeyPill"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            };
            pill.Child = new TextBlock
            {
                Text = GlobalHotkeyService.FormatShortcutDisplay(modifiers, key),
                Foreground = ThemeService.GetBrush("TextSecondaryBrush"),
                FontFamily = (System.Windows.Media.FontFamily)FindResource("CryoMono"),
                FontSize = 10.5,
                FlowDirection = FlowDirection.LeftToRight
            };
            Grid.SetColumn(pill, 1);
            grid.Children.Add(pill);

            var change = new Button
            {
                Content = LocalizationService.Get("Common_Change"),
                Style = (Style)FindResource("SecondaryButton"),
                MinWidth = 78
            };
            change.Click += (_, _) =>
            {
                var dialog = new HotkeyCaptureWindow(modifiers, key)
                {
                    Owner = Window.GetWindow(this)
                };
                if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.KeyToken))
                {
                    return;
                }

                apply(dialog.ModifiersToken, dialog.KeyToken);
                SettingsService.Instance.Save();
                (Application.Current as App)?.ApplyHotkeys();
                RefreshRows();
                HotkeysChanged?.Invoke();
            };
            Grid.SetColumn(change, 2);
            grid.Children.Add(change);

            row.Child = grid;
            HotkeyRowsPanel.Children.Add(row);
        }

        private void BtnResetHotkeys_Click(object sender, RoutedEventArgs e)
        {
            var defaults = new AppSettings();
            var settings = SettingsService.Instance.Current;
            settings.CaptureHotkeyModifiers = defaults.CaptureHotkeyModifiers;
            settings.CaptureHotkeyKey = defaults.CaptureHotkeyKey;
            settings.ColorPickerHotkeyModifiers = defaults.ColorPickerHotkeyModifiers;
            settings.ColorPickerHotkeyKey = defaults.ColorPickerHotkeyKey;
            settings.WindowPinnerHotkeyModifiers = defaults.WindowPinnerHotkeyModifiers;
            settings.WindowPinnerHotkeyKey = defaults.WindowPinnerHotkeyKey;
            settings.PlainPasteHotkeyModifiers = defaults.PlainPasteHotkeyModifiers;
            settings.PlainPasteHotkeyKey = defaults.PlainPasteHotkeyKey;
            settings.ClipboardHotkeyModifiers = defaults.ClipboardHotkeyModifiers;
            settings.ClipboardHotkeyKey = defaults.ClipboardHotkeyKey;
            settings.WorkbenchHotkeyModifiers = defaults.WorkbenchHotkeyModifiers;
            settings.WorkbenchHotkeyKey = defaults.WorkbenchHotkeyKey;
            settings.LiveDrawHotkeyModifiers = defaults.LiveDrawHotkeyModifiers;
            settings.LiveDrawHotkeyKey = defaults.LiveDrawHotkeyKey;
            settings.ReplayHotkeyModifiers = defaults.ReplayHotkeyModifiers;
            settings.ReplayHotkeyKey = defaults.ReplayHotkeyKey;
            settings.PrintScreenFallbackSuggested = false;
            SettingsService.Instance.Save();
            RefreshRows();
            HotkeysChanged?.Invoke();
        }
    }
}
