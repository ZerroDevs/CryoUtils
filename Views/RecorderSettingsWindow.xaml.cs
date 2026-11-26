using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CryoUtils.Services;
using CryoUtils.Services.Recording;

namespace CryoUtils.Views
{
    public partial class RecorderSettingsWindow : Window
    {
        private bool _loading;
        private string _replayModifiers = "Ctrl+Shift";
        private string _replayKey = "R";

        public RecorderSettingsWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            LoadSettings();
        }

        private void LoadSettings()
        {
            _loading = true;
            var settings = SettingsService.Instance.Current;
            SelectTag(CmbFormat, settings.RecorderFormat);
            SelectTag(CmbFps, settings.RecorderFps >= 60 ? "60" : "30");
            SelectTag(CmbResolution, settings.RecorderResolution);
            SelectTag(CmbQuality, settings.RecorderQuality);
            SelectTag(CmbBitrateMode, settings.RecorderBitrateMbps > 0 ? "Custom" : "Auto");
            SldBitrate.Value = Math.Clamp(settings.RecorderBitrateMbps, 2, 60);
            SelectTag(CmbMaxLength, settings.MaxRecordingSeconds.ToString(CultureInfo.InvariantCulture));
            ChkTrim.IsChecked = settings.RecorderTrimBeforeSave;
            ChkSystemAudio.IsChecked = settings.RecorderSystemAudio;
            ChkMicrophone.IsChecked = settings.RecorderMicrophone;
            LoadAudioDevices();
            ChkCursor.IsChecked = settings.RecorderCursorHighlight;
            ChkClicks.IsChecked = settings.RecorderClickEffects;
            ChkWebcam.IsChecked = settings.RecorderWebcam;
            ChkReplay.IsChecked = settings.ReplayEnabled;
            SldReplayLength.Value = Math.Clamp(
                settings.ReplaySeconds,
                RecordingOptions.MinimumReplaySeconds,
                RecordingOptions.MaximumReplaySeconds);
            SelectTag(CmbReplayMonitor, settings.ReplayMonitor);
            TxtDiskBudget.Text = Math.Clamp(settings.ReplayDiskBudgetGb, 1, 64)
                .ToString(CultureInfo.InvariantCulture);
            _replayModifiers = settings.ReplayHotkeyModifiers;
            _replayKey = settings.ReplayHotkeyKey;
            UpdateHotkeyLabel();
            UpdateBitrateLabel();
            UpdateReplayLabel();
            _loading = false;
        }

        private void LoadAudioDevices()
        {
            var settings = SettingsService.Instance.Current;
            PopulateAudioDeviceCombo(
                CmbSystemAudioDevice,
                WasapiCaptureService.GetDevices(input: false),
                settings.RecorderSystemAudioDeviceId);
            PopulateAudioDeviceCombo(
                CmbMicrophoneDevice,
                WasapiCaptureService.GetDevices(input: true),
                settings.RecorderMicrophoneDeviceId);
        }

        private static void PopulateAudioDeviceCombo(
            ComboBox comboBox,
            System.Collections.Generic.IReadOnlyList<AudioDeviceInfo> devices,
            string? selectedId)
        {
            comboBox.Items.Clear();
            var defaultDevice = devices.FirstOrDefault(device => device.IsDefault);
            string defaultName = defaultDevice?.Name?.Trim() ?? "";
            string defaultLabel = string.IsNullOrEmpty(defaultName)
                ? LocalizationService.Get("Rec_AudioDeviceDefault")
                : string.Format(
                    LocalizationService.Get("Rec_AudioDeviceDefaultName"),
                    defaultName);
            comboBox.Items.Add(new ComboBoxItem
            {
                Content = $"★  {defaultLabel}",
                Tag = ""
            });

            foreach (var device in devices.Where(device => !device.IsDefault))
            {
                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = device.Name,
                    Tag = device.Id
                });
            }

            ComboBoxItem? selected = null;
            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                selected = comboBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(
                        item.Tag as string,
                        selectedId,
                        StringComparison.OrdinalIgnoreCase));
            }

            comboBox.SelectedItem = selected ?? comboBox.Items[0];
        }

        private static string SelectedDeviceId(ComboBox comboBox) =>
            (comboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        private void UpdateHotkeyLabel()
        {
            BtnReplayHotkey.Content = GlobalHotkeyService.FormatShortcutDisplay(
                _replayModifiers,
                _replayKey);
        }

        private void UpdateBitrateLabel()
        {
            bool custom = (CmbBitrateMode.SelectedItem as ComboBoxItem)?.Tag as string == "Custom";
            SldBitrate.IsEnabled = custom;
            TxtBitrateValue.Text = custom
                ? $"{SldBitrate.Value:0} Mbps"
                : LocalizationService.Get("Rec_BitrateAuto");
        }

        private void UpdateReplayLabel()
        {
            TxtReplayLength.Text = $"{SldReplayLength.Value:0} s";
        }

        private void SldBitrate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loading)
            {
                UpdateBitrateLabel();
            }
        }

        private void CmbBitrateMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading)
            {
                UpdateBitrateLabel();
            }
        }

        private void SldReplayLength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loading)
            {
                UpdateReplayLabel();
            }
        }

        private void ChkReplay_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loading)
            {
                TxtStatus.Text = ChkReplay.IsChecked == true
                    ? LocalizationService.Get("Rec_ReplayStartHint")
                    : LocalizationService.Get("Rec_ReplayStopHint");
            }
        }

        private void Options_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loading && (ChkSystemAudio.IsChecked == true || ChkMicrophone.IsChecked == true))
            {
                TxtStatus.Text = LocalizationService.Get("Rec_AudioHint");
            }
        }

        private void BtnReplayHotkey_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new HotkeyCaptureWindow(_replayModifiers, _replayKey)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.KeyToken))
            {
                return;
            }

            _replayModifiers = dialog.ModifiersToken;
            _replayKey = dialog.KeyToken;
            UpdateHotkeyLabel();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.Instance.Current;
            bool customBitrate = (CmbBitrateMode.SelectedItem as ComboBoxItem)?.Tag as string == "Custom";
            int bitrate = customBitrate ? (int)Math.Round(SldBitrate.Value) : 0;
            string quality = (CmbQuality.SelectedItem as ComboBoxItem)?.Tag as string ?? "Medium";
            if (customBitrate)
            {
                quality = "Custom";
            }

            int maxSeconds = int.TryParse(
                (CmbMaxLength.SelectedItem as ComboBoxItem)?.Tag as string,
                out int parsedMax)
                ? parsedMax
                : 300;
            int diskBudget = int.TryParse(TxtDiskBudget.Text, out int parsedDisk)
                ? Math.Clamp(parsedDisk, 1, 64)
                : 4;

            settings.RecorderFormat = (CmbFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "MP4";
            settings.RecorderFps = (CmbFps.SelectedItem as ComboBoxItem)?.Tag as string == "60" ? 60 : 30;
            settings.RecorderResolution = (CmbResolution.SelectedItem as ComboBoxItem)?.Tag as string ?? "Source";
            settings.RecorderQuality = quality;
            settings.RecorderBitrateMbps = bitrate;
            settings.MaxRecordingSeconds = maxSeconds;
            settings.RecorderTrimBeforeSave = ChkTrim.IsChecked == true;
            settings.RecorderSystemAudio = ChkSystemAudio.IsChecked == true;
            settings.RecorderMicrophone = ChkMicrophone.IsChecked == true;
            settings.RecorderSystemAudioDeviceId = SelectedDeviceId(CmbSystemAudioDevice);
            settings.RecorderMicrophoneDeviceId = SelectedDeviceId(CmbMicrophoneDevice);
            settings.RecorderCursorHighlight = ChkCursor.IsChecked == true;
            settings.RecorderClickEffects = ChkClicks.IsChecked == true;
            settings.RecorderWebcam = ChkWebcam.IsChecked == true;
            settings.ReplayEnabled = ChkReplay.IsChecked == true;
            settings.ReplaySeconds = (int)Math.Round(SldReplayLength.Value);
            settings.ReplayMonitor = (CmbReplayMonitor.SelectedItem as ComboBoxItem)?.Tag as string ?? "Primary";
            settings.ReplayHotkeyModifiers = _replayModifiers;
            settings.ReplayHotkeyKey = _replayKey;
            settings.ReplayDiskBudgetGb = diskBudget;
            SettingsService.Instance.Save();
            if (ReplayService.Instance.IsRunning)
            {
                ReplayService.Instance.Stop();
            }
            (Application.Current as App)?.ApplyReplayPreference();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private static void SelectTag(ComboBox comboBox, string? tag)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(comboItem.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }
    }
}
