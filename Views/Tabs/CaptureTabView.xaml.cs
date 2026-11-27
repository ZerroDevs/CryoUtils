using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CryoUtils.Services;
using Microsoft.Win32;

namespace CryoUtils.Views.Tabs
{
    public partial class CaptureTabView : UserControl
    {
        private bool _loading;

        public event Action? SaveFolderChanged;
        public event Action? HotkeyStatusChanged;
        public event Action? RecorderSettingsClosed;

        public CaptureTabView()
        {
            _loading = true;
            try
            {
                InitializeComponent();
            }
            finally
            {
                _loading = false;
            }
        }

        public void LoadFromSettings()
        {
            _loading = true;
            try
            {
                var settings = SettingsService.Instance.Current;
                ChkQuickSave.IsChecked = settings.QuickSave;
                TxtSaveFolder.Text = SettingsService.Instance.EffectiveSaveFolder;
                TxtSaveFolder.FlowDirection = FlowDirection.LeftToRight;
                ChkAutoCopy.IsChecked = settings.AutoCopyCapture;
                ChkHoverHighlight.IsChecked = settings.HoverWindowHighlight;
                ChkInstantCopy.IsChecked = settings.InstantCaptureCopy;
                ChkCursorCoords.IsChecked = settings.ShowCursorCoordinates;
                ChkCaptureSound.IsChecked = settings.CaptureSound;
                ChkShowLoupe.IsChecked = settings.ShowCaptureLoupe;
                ChkPrettyFrame.IsChecked = settings.PrettyFrameExport;
                ChkArchiveCopied.IsChecked = settings.ArchiveCopiedCaptures;
                ChkCloudUpload.IsChecked = settings.CloudUploadEnabled;
                ChkOcrLibraryIndex.IsChecked = settings.OcrLibraryIndexingEnabled;
                PopulateOcrLanguages(settings.OcrLanguageTag);
                MainTabHelpers.SelectTag(CmbImageFormat, settings.ImageFormat);
                MainTabHelpers.SelectTag(CmbCaptureDelay, settings.CaptureDelaySeconds.ToString(CultureInfo.InvariantCulture));
                MainTabHelpers.SelectTag(CmbFps, settings.GifFps.ToString(CultureInfo.InvariantCulture));
                MainTabHelpers.SelectTag(CmbRecFormat, settings.RecorderFormat);
                MainTabHelpers.SelectTag(CmbMaxSeconds, settings.MaxRecordingSeconds.ToString(CultureInfo.InvariantCulture));
                ChkCopyOnStop.IsChecked = settings.RecorderCopyOnStop;

                SldThickness.Value = Math.Clamp(settings.AnnotationThickness, 1, 20);
                TxtThickness.Text = ((int)SldThickness.Value).ToString(CultureInfo.InvariantCulture);
                TxtStepStart.Text = Math.Clamp(settings.StepStartNumber, 1, 99).ToString(CultureInfo.InvariantCulture);
                TxtDefaultColor.Text = settings.AnnotationColor;
                DefaultColorSwatch.Background = new SolidColorBrush(
                    AnnotationRenderer.ParseColor(settings.AnnotationColor));
            }
            finally
            {
                _loading = false;
            }

            RefreshRecorderSummary();
            UpdateContestedBanner();
            ApplyScreenshotOnlyUi();
        }

        public void ApplyScreenshotOnlyUi()
        {
            bool lite = SettingsService.Instance.Current.ScreenshotOnlyMode;
            BtnCaptureTabRecord.Visibility = lite ? Visibility.Collapsed : Visibility.Visible;
            CardRecorder.Opacity = lite ? 0.45 : 1.0;
            CardRecorder.IsEnabled = !lite;
            ChkCloudUpload.IsEnabled = !lite;
            ChkCloudUpload.Opacity = lite ? 0.45 : 1.0;
            ChkOcrLibraryIndex.IsEnabled = !lite;
            ChkOcrLibraryIndex.Opacity = lite ? 0.45 : 1.0;
        }

        private void PopulateOcrLanguages(string? selectedTag)
        {
            if (CmbOcrLanguage == null)
            {
                return;
            }

            CmbOcrLanguage.Items.Clear();
            CmbOcrLanguage.Items.Add(new ComboBoxItem
            {
                Content = LocalizationService.Get("Capture_OcrLanguageAuto"),
                Tag = ""
            });
            try
            {
                foreach (var lang in OcrService.GetAvailableLanguages())
                {
                    CmbOcrLanguage.Items.Add(new ComboBoxItem
                    {
                        Content = string.IsNullOrWhiteSpace(lang.DisplayName)
                            ? lang.Tag
                            : $"{lang.DisplayName} ({lang.Tag})",
                        Tag = lang.Tag
                    });
                }
            }
            catch
            {
            }

            MainTabHelpers.SelectTag(CmbOcrLanguage, selectedTag ?? "");
            if (CmbOcrLanguage.SelectedIndex < 0 && CmbOcrLanguage.Items.Count > 0)
            {
                CmbOcrLanguage.SelectedIndex = 0;
            }
        }

        public void UpdateContestedBanner()
        {
            var settings = SettingsService.Instance.Current;
            bool printScreen = string.Equals(settings.CaptureHotkeyKey, "PrintScreen", StringComparison.OrdinalIgnoreCase);
            bool contested = settings.CaptureEnabled && printScreen &&
                !GlobalHotkeyService.Instance.IsRegistered(GlobalHotkeyService.CaptureHotkeyId);
            PrintScreenBanner.Visibility = contested ? Visibility.Visible : Visibility.Collapsed;
        }

        public void RefreshRecorderSummary()
        {
            var settings = SettingsService.Instance.Current;
            if (string.Equals(settings.RecorderFormat, "GIF", StringComparison.OrdinalIgnoreCase))
            {
                TxtRecorderSummary.Text = LocalizationService.Format(
                    "Rec_SummaryGif",
                    Math.Clamp(settings.GifFps, 5, 30),
                    settings.MaxRecordingSeconds);
                return;
            }

            TxtRecorderSummary.Text = LocalizationService.Format(
                "Rec_SummaryMp4",
                settings.RecorderResolution,
                settings.RecorderFps,
                settings.RecorderQuality,
                settings.RecorderBitrateMbps > 0
                    ? $"{settings.RecorderBitrateMbps} Mbps"
                    : LocalizationService.Get("Rec_BitrateAuto"));
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || !IsInitialized || TxtSaveFolder == null) return;

            MemoryTrimService.PauseForInteraction();
            var settings = SettingsService.Instance.Current;
            settings.QuickSave = ChkQuickSave.IsChecked == true;
            settings.SaveFolder = TxtSaveFolder.Text == SettingsService.Instance.DefaultSaveFolder
                ? ""
                : TxtSaveFolder.Text;
            settings.AutoCopyCapture = ChkAutoCopy.IsChecked == true;
            settings.HoverWindowHighlight = ChkHoverHighlight.IsChecked == true;
            settings.InstantCaptureCopy = ChkInstantCopy.IsChecked == true;
            settings.ShowCursorCoordinates = ChkCursorCoords.IsChecked == true;
            settings.CaptureSound = ChkCaptureSound.IsChecked == true;
            settings.ShowCaptureLoupe = ChkShowLoupe.IsChecked == true;
            settings.PrettyFrameExport = ChkPrettyFrame.IsChecked == true;
            settings.ArchiveCopiedCaptures = ChkArchiveCopied.IsChecked == true;
            settings.CloudUploadEnabled = ChkCloudUpload.IsChecked == true;
            settings.OcrLanguageTag = MainTabHelpers.SelectedTag(CmbOcrLanguage) ?? "";
            settings.OcrLibraryIndexingEnabled = ChkOcrLibraryIndex.IsChecked == true;
            settings.ImageFormat = MainTabHelpers.SelectedTag(CmbImageFormat) ?? "PNG";
            settings.CaptureDelaySeconds = int.TryParse(MainTabHelpers.SelectedTag(CmbCaptureDelay), out int delay) ? delay : 0;
            settings.GifFps = Math.Clamp(int.TryParse(MainTabHelpers.SelectedTag(CmbFps), out int fps) ? fps : 15, 5, 30);
            settings.RecorderFormat = MainTabHelpers.SelectedTag(CmbRecFormat) ?? "GIF";
            settings.MaxRecordingSeconds = int.TryParse(MainTabHelpers.SelectedTag(CmbMaxSeconds), out int seconds) ? seconds : 300;
            settings.RecorderCopyOnStop = ChkCopyOnStop.IsChecked == true;

            SettingsService.Instance.Save();
            RefreshRecorderSummary();
        }

        private void Thickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtThickness == null) return;
            TxtThickness.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture);
            if (_loading) return;

            SettingsService.Instance.Current.AnnotationThickness = (int)Math.Round(e.NewValue);
            SettingsService.Instance.SaveWithoutNotification();
        }

        private void TxtStepStart_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtStepStart.Text, out int step)) step = 1;
            step = Math.Clamp(step, 1, 99);
            TxtStepStart.Text = step.ToString(CultureInfo.InvariantCulture);
            SettingsService.Instance.Current.StepStartNumber = step;
            SettingsService.Instance.SaveWithoutNotification();
        }

        private void BtnDefaultColor_Click(object sender, RoutedEventArgs e)
        {
            string? hex = ColorDialogHelper.PickColorHex(Window.GetWindow(this), SettingsService.Instance.Current.AnnotationColor);
            if (string.IsNullOrEmpty(hex)) return;

            SettingsService.Instance.Current.AnnotationColor = hex;
            SettingsService.Instance.Save();
            TxtDefaultColor.Text = hex;
            DefaultColorSwatch.Background = new SolidColorBrush(AnnotationRenderer.ParseColor(hex));
        }

        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var dialog = new OpenFolderDialog
            {
                Title = LocalizationService.Get("Capture_Folder"),
                InitialDirectory = SettingsService.Instance.EffectiveSaveFolder
            };
            bool? ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (ok != true) return;

            TxtSaveFolder.Text = dialog.FolderName;
            SettingsService.Instance.Current.SaveFolder = dialog.FolderName;
            SettingsService.Instance.Save();
            SaveFolderChanged?.Invoke();
        }

        private void BtnUseFallback_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.Instance.Current;
            settings.CaptureHotkeyModifiers = "Ctrl+Shift";
            settings.CaptureHotkeyKey = "S";
            settings.PrintScreenFallbackSuggested = false;
            SettingsService.Instance.Save();
            PrintScreenBanner.Visibility = Visibility.Collapsed;
            HotkeyStatusChanged?.Invoke();
        }

        private void BtnHeroCapture_Click(object sender, RoutedEventArgs e) =>
            App.CurrentApp?.StartCapture();

        private void BtnRecordNow_Click(object sender, RoutedEventArgs e) =>
            App.CurrentApp?.StartRecording();

        private void BtnRecorderSettings_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.OpenRecorderSettings();
            RefreshRecorderSummary();
            RecorderSettingsClosed?.Invoke();
        }
    }
}
