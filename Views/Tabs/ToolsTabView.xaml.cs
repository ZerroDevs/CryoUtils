using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Services;
using CryoUtils.Services.Recording;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace CryoUtils.Views.Tabs
{
    public partial class ToolsTabView : UserControl
    {
        private bool _loading;
        private string? _compressSourcePath;
        private CompressionResult? _compressResult;

        public ToolsTabView()
        {
            InitializeComponent();
        }

        public void LoadFromSettings()
        {
            _loading = true;
            try
            {
                var settings = SettingsService.Instance.Current;
                MainTabHelpers.SelectTag(CmbPreset, settings.CompressorPreset);
                SldQuality.Value = Math.Clamp(settings.CompressorQuality, 20, 100);
                SldScale.Value = Math.Clamp(settings.CompressorScale, 20, 100);
                TxtQualityValue.Text = $"{(int)SldQuality.Value}%";
                TxtScaleValue.Text = $"{(int)SldScale.Value}%";
                SldPinnedOpacity.Value = Math.Clamp(settings.PinnedOpacity, 20, 100);
                TxtPinnedOpacity.Text = $"{(int)SldPinnedOpacity.Value}%";
                ChkPinnedClickThrough.IsChecked = settings.PinnedClickThrough;
                UpdatePresetSliderState();
                UpdateDashboardContrast();
            }
            finally
            {
                _loading = false;
            }

            RefreshRecentColors();
            RefreshPinnedState();
            RefreshClipboardState();
            RefreshReplayCard();
            UpdateCompressionAvailability();
        }

        public void ReleaseResources()
        {
            ImgCompressedPreview.Source = null;
            ImgQrPreview.Source = null;
            _compressResult = null;
        }

        public void LoadCompressionSource(string path)
        {
            string extension = IOPath.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif"))
            {
                App.CurrentApp?.ShowToast(LocalizationService.Get("Tools_CompressFailed"));
                return;
            }

            _compressSourcePath = path;
            _compressResult = null;
            BtnSaveCompressed.IsEnabled = false;
            BtnCopyCompressed.IsEnabled = false;
            TxtCompressFileName.Text = IOPath.GetFileName(path);
            TxtCompressStatus.Text = LocalizationService.Get("Tools_NoResult");
            TxtBeforeSize.Text = ImageIO.FormatBytes(new FileInfo(path).Length);
            TxtAfterSize.Text = "--";
            TxtSaved.Text = "--";
            try
            {
                ImgCompressedPreview.Source = ImageIO.LoadFileBitmap(path);
            }
            catch
            {
                ImgCompressedPreview.Source = null;
            }
            UpdateCompressionAvailability();
        }

        public void UpdateCompressionAvailability()
        {
            bool enabled = SettingsService.Instance.Current.CompressorEnabled;
            bool custom = MainTabHelpers.SelectedTag(CmbPreset) == "Custom";
            BtnCompress.IsEnabled = enabled && !string.IsNullOrEmpty(_compressSourcePath);
            CmbPreset.IsEnabled = enabled;
            SldQuality.IsEnabled = enabled && custom;
            SldScale.IsEnabled = enabled && custom;
        }

        public void RefreshRecentColors()
        {
            RecentColorsPanel.Children.Clear();
            var colors = SettingsService.Instance.Current.RecentColors;
            TxtRecentColorsEmpty.Visibility = colors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (string hex in colors)
            {
                var button = new Button
                {
                    Width = 26,
                    Height = 22,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(0),
                    Style = (Style)FindResource("SecondaryButton"),
                    Background = new SolidColorBrush(AnnotationRenderer.ParseColor(hex)),
                    BorderBrush = ThemeService.GetBrush("BorderStrongBrush"),
                    ToolTip = hex
                };
                System.Windows.Automation.AutomationProperties.SetName(button, hex);
                button.Click += (_, _) =>
                {
                    try
                    {
                        Clipboard.SetText(hex);
                        App.CurrentApp?.ShowToast(LocalizationService.Get("Common_Copied"));
                    }
                    catch { }
                };
                RecentColorsPanel.Children.Add(button);
            }
        }

        public void RefreshPinnedState()
        {
            var topmost = TopmostService.Instance;
            TxtPinnedWindow.Text = topmost.IsPinned
                ? LocalizationService.Format("Tools_PinnedActive", topmost.PinnedTitle)
                : LocalizationService.Get("Tools_PinnedNone");
            BtnUnpin.IsEnabled = topmost.IsPinned;
        }

        public void RefreshClipboardState()
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.ClipboardHistoryEnabled)
            {
                TxtClipboardCount.Text = LocalizationService.Get("Tools_ClipboardDisabled");
                TxtClipboardNote.Text = LocalizationService.Get("Tools_ClipboardDesc");
                return;
            }

            int count = ClipboardService.Instance.Items.Count;
            TxtClipboardCount.Text = count == 0
                ? LocalizationService.Get("Tools_ClipboardEmpty")
                : LocalizationService.Format("Tools_ClipboardCount", count);
            TxtClipboardNote.Text = settings.ClipboardAutoClearSensitive
                ? LocalizationService.Format("Tools_ClipboardSensitive", settings.ClipboardAutoClearSeconds)
                : LocalizationService.Get("Tools_ClipboardDesc");
        }

        public void RefreshReplayCard()
        {
            var replay = ReplayService.Instance;
            BtnReplayToggle.Content = LocalizationService.Get(
                replay.IsRunning ? "Rec_ReplayStop" : "Rec_ReplayStart");
            BtnReplaySave.IsEnabled = replay.IsRunning;
            TxtReplayStatus.Text = replay.IsRunning
                ? LocalizationService.Format(
                    "Rec_ReplayRunning",
                    $"{replay.BufferedSeconds / 60:00}:{replay.BufferedSeconds % 60:00}",
                    ImageIO.FormatBytes(replay.BufferedBytes))
                : LocalizationService.Get("Rec_ReplayOff");
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = ThemeService.GetBrush("BorderStrongBrush");
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                LoadCompressionSource(files[0]);
            }
        }

        private void DropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => BrowseCompressionFile();
        private void BtnBrowseCompress_Click(object sender, RoutedEventArgs e) => BrowseCompressionFile();

        private void BrowseCompressionFile()
        {
            var owner = Window.GetWindow(this);
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Get("Tools_Dropzone"),
                Filter = LocalizationService.Get("Common_ImageFilter"),
                CheckFileExists = true
            };
            bool? ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (ok == true)
            {
                LoadCompressionSource(dialog.FileName);
            }
        }

        private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || TxtQualityValue == null) return;
            string preset = MainTabHelpers.SelectedTag(CmbPreset) ?? "Discord";
            _loading = true;
            try
            {
                if (preset == "Discord") { SldQuality.Value = 88; SldScale.Value = 100; }
                else if (preset == "WhatsApp") { SldQuality.Value = 70; SldScale.Value = 80; }
                TxtQualityValue.Text = $"{(int)SldQuality.Value}%";
                TxtScaleValue.Text = $"{(int)SldScale.Value}%";
            }
            finally { _loading = false; }

            SettingsService.Instance.Current.CompressorPreset = preset;
            SettingsService.Instance.SaveWithoutNotification();
            UpdatePresetSliderState();
        }

        private void UpdatePresetSliderState()
        {
            bool custom = MainTabHelpers.SelectedTag(CmbPreset) == "Custom";
            SldQuality.IsEnabled = custom;
            SldScale.IsEnabled = custom;
        }

        private void Compression_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtQualityValue == null || TxtScaleValue == null) return;
            TxtQualityValue.Text = $"{(int)Math.Round(SldQuality.Value)}%";
            TxtScaleValue.Text = $"{(int)Math.Round(SldScale.Value)}%";
            if (_loading) return;

            var settings = SettingsService.Instance.Current;
            settings.CompressorQuality = (int)Math.Round(SldQuality.Value);
            settings.CompressorScale = (int)Math.Round(SldScale.Value);
            SettingsService.Instance.SaveWithoutNotification();
        }

        private async void BtnCompress_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_compressSourcePath) || !File.Exists(_compressSourcePath))
            {
                App.CurrentApp?.ShowToast(LocalizationService.Get("Tools_NoResult"));
                return;
            }
            if (!SettingsService.Instance.Current.CompressorEnabled)
            {
                App.CurrentApp?.ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }

            string preset = MainTabHelpers.SelectedTag(CmbPreset) ?? "Discord";
            int quality = (int)Math.Round(SldQuality.Value);
            int scale = (int)Math.Round(SldScale.Value);
            var settings = SettingsService.Instance.Current;
            settings.CompressorPreset = preset;
            settings.CompressorQuality = quality;
            settings.CompressorScale = scale;
            SettingsService.Instance.SaveWithoutNotification();

            SetCompressionBusy(true);
            TxtCompressStatus.Text = LocalizationService.Get("Tools_CompressBusy");
            try
            {
                var result = await CompressionService.CompressAsync(_compressSourcePath, preset, quality, scale);
                _compressResult = result;
                ImgCompressedPreview.Source = result.Preview;
                TxtBeforeSize.Text = ImageIO.FormatBytes(result.OriginalBytes);
                TxtAfterSize.Text = ImageIO.FormatBytes(result.CompressedBytes);
                TxtSaved.Text = result.SavedPercent > 0 ? $"{(int)Math.Round(result.SavedPercent)}%" : "0%";
                TxtCompressStatus.Text = LocalizationService.Format(
                    "Tools_Result",
                    ImageIO.FormatBytes(result.OriginalBytes),
                    ImageIO.FormatBytes(result.CompressedBytes),
                    TxtSaved.Text);
                BtnSaveCompressed.IsEnabled = true;
                BtnCopyCompressed.IsEnabled = true;
            }
            catch (Exception ex)
            {
                TxtCompressStatus.Text = LocalizationService.Get("Tools_CompressFailed");
                App.CurrentApp?.ShowToast(LocalizationService.Format("Error_Body", ex.Message));
            }
            finally { SetCompressionBusy(false); }
        }

        private void SetCompressionBusy(bool busy)
        {
            BtnCompress.IsEnabled = !busy && SettingsService.Instance.Current.CompressorEnabled && !string.IsNullOrEmpty(_compressSourcePath);
            BtnSaveCompressed.IsEnabled = !busy && _compressResult != null;
            BtnCopyCompressed.IsEnabled = !busy && _compressResult != null;
        }

        private void BtnSaveCompressed_Click(object sender, RoutedEventArgs e)
        {
            if (_compressResult == null) return;
            var owner = Window.GetWindow(this);
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Tools_SaveAs"),
                Filter = LocalizationService.Get("Common_ImageFilter"),
                DefaultExt = _compressResult.Extension,
                AddExtension = true,
                FileName = ScreenCaptureService.CreateTimestampedFileName(_compressResult.Extension),
                InitialDirectory = SettingsService.Instance.EffectiveSaveFolder
            };
            bool? ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (ok != true) return;

            try
            {
                File.WriteAllBytes(dialog.FileName, _compressResult.Data);
                App.CurrentApp?.ShowToast(LocalizationService.Get("General_ExportDone"));
            }
            catch (Exception ex) { App.CurrentApp?.ShowToast(LocalizationService.Format("Error_Body", ex.Message)); }
        }

        private void BtnCopyCompressed_Click(object sender, RoutedEventArgs e)
        {
            if (_compressResult == null) return;
            try
            {
                string folder = IOPath.Combine(IOPath.GetTempPath(), "CryoUtils");
                Directory.CreateDirectory(folder);
                string path = IOPath.Combine(folder, ScreenCaptureService.CreateTimestampedFileName(_compressResult.Extension));
                File.WriteAllBytes(path, _compressResult.Data);
                Clipboard.SetFileDropList(new StringCollection { path });
                App.CurrentApp?.ShowToast(LocalizationService.Get("Common_Copied"));
            }
            catch (Exception ex) { App.CurrentApp?.ShowToast(LocalizationService.Format("Error_Body", ex.Message)); }
        }

        private void ContrastColor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loading) UpdateDashboardContrast();
        }

        private void BtnSwapContrastColors_Click(object sender, RoutedEventArgs e)
        {
            string temp = TxtContrastA.Text;
            TxtContrastA.Text = TxtContrastB.Text;
            TxtContrastB.Text = temp;
        }

        public void UpdateDashboardContrast()
        {
            if (TxtContrastA == null || TxtContrastB == null) return;
            var colorA = AnnotationRenderer.ParseColor(TxtContrastA.Text.Trim());
            var colorB = AnnotationRenderer.ParseColor(TxtContrastB.Text.Trim());
            SwatchContrastA.Background = new SolidColorBrush(colorA);
            SwatchContrastB.Background = new SolidColorBrush(colorB);

            double ratio = ColorContrastService.CalculateRatio(colorA, colorB);
            TxtDashboardContrastRatio.Text = ColorContrastService.FormatRatio(ratio);
            var eval = ColorContrastService.Evaluate(ratio);

            string passText = LocalizationService.Get("Tools_WcagPass");
            string failText = LocalizationService.Get("Tools_WcagFail");
            string normalLabel = LocalizationService.Get("Tools_WcagNormalText");
            string largeLabel = LocalizationService.Get("Tools_WcagLargeText");

            SetWcagBadge(BadgeWcagAaNormal, TxtWcagAaNormal, normalLabel, eval.AaNormal, passText, failText);
            SetWcagBadge(BadgeWcagAaLarge, TxtWcagAaLarge, largeLabel, eval.AaLarge, passText, failText);
            SetWcagBadge(BadgeWcagAaaNormal, TxtWcagAaaNormal, normalLabel, eval.AaaNormal, passText, failText);
            SetWcagBadge(BadgeWcagAaaLarge, TxtWcagAaaLarge, largeLabel, eval.AaaLarge, passText, failText);
        }

        private static void SetWcagBadge(Border badge, TextBlock text, string label, bool pass, string passText, string failText)
        {
            text.Text = $"{label}: {(pass ? passText : failText)}";
            badge.Background = pass ? ThemeService.GetBrush("SuccessMutedBrush") : ThemeService.GetBrush("InsetBackgroundBrush");
            text.Foreground = pass ? ThemeService.GetBrush("SuccessBrush") : ThemeService.GetBrush("TextMutedBrush");
        }

        private void TxtQrInput_TextChanged(object sender, TextChangedEventArgs e) => RegenerateDashboardQr();
        private void CmbQrCorrection_SelectionChanged(object sender, SelectionChangedEventArgs e) => RegenerateDashboardQr();

        private void RegenerateDashboardQr()
        {
            if (TxtQrInput == null || ImgQrPreview == null) return;
            string text = TxtQrInput.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                ImgQrPreview.Source = null;
                BtnCopyQr.IsEnabled = false;
                BtnSaveQr.IsEnabled = false;
                return;
            }

            string levelStr = MainTabHelpers.SelectedTag(CmbQrCorrection) ?? "M";
            var level = levelStr switch
            {
                "L" => ZXing.QrCode.Internal.ErrorCorrectionLevel.L,
                "Q" => ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
                "H" => ZXing.QrCode.Internal.ErrorCorrectionLevel.H,
                _ => ZXing.QrCode.Internal.ErrorCorrectionLevel.M
            };

            try
            {
                ImgQrPreview.Source = QrCodeService.Encode(text, 256, level);
                BtnCopyQr.IsEnabled = true;
                BtnSaveQr.IsEnabled = true;
            }
            catch
            {
                ImgQrPreview.Source = null;
                BtnCopyQr.IsEnabled = false;
                BtnSaveQr.IsEnabled = false;
            }
        }

        private void BtnCopyQr_Click(object sender, RoutedEventArgs e)
        {
            if (ImgQrPreview.Source is BitmapSource bitmap)
            {
                try
                {
                    Clipboard.SetImage(bitmap);
                    RecentCaptureService.Instance.Add(bitmap, null, LocalizationService.Get("Tools_QrTitle"));
                    App.CurrentApp?.ShowToast(LocalizationService.Get("Common_Copied"));
                }
                catch { }
            }
        }

        private void BtnSaveQr_Click(object sender, RoutedEventArgs e)
        {
            if (ImgQrPreview.Source is not BitmapSource bitmap) return;
            var owner = Window.GetWindow(this);
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Tools_QrSave"),
                Filter = LocalizationService.Get("Common_PngFilter"),
                DefaultExt = ".png",
                FileName = $"CryoUtils_QRCode_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };
            bool? ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (ok != true) return;
            try
            {
                File.WriteAllBytes(dialog.FileName, ImageIO.EncodePng(bitmap));
                RecentCaptureService.Instance.Add(bitmap, dialog.FileName, LocalizationService.Get("Tools_QrTitle"));
                App.CurrentApp?.ShowToast(LocalizationService.Get("General_ExportDone"));
            }
            catch (Exception ex) { App.CurrentApp?.ShowToast(LocalizationService.Format("Error_Body", ex.Message)); }
        }

        private void BtnQrScanScreen_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.StartCapture();

        private void BtnQrScanFile_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Get("Tools_QrScanFile"),
                Filter = LocalizationService.Get("Common_ImageFilter")
            };
            bool? ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (ok != true) return;
            try
            {
                var bitmap = new BitmapImage(new Uri(dialog.FileName, UriKind.Absolute));
                var decoded = QrCodeService.Decode(bitmap);
                if (decoded == null || string.IsNullOrWhiteSpace(decoded.Text))
                {
                    App.CurrentApp?.ShowToast(LocalizationService.Get("Qr_NotFound"));
                    return;
                }
                RecentCaptureService.Instance.Add(bitmap, dialog.FileName, LocalizationService.Get("Tools_QrTitle"));
                var window = new QrResultWindow(decoded.Text, decoded.FormatName, bitmap);
                window.Show();
                window.Activate();
            }
            catch (Exception ex) { App.CurrentApp?.ShowToast(LocalizationService.Format("Error_Body", ex.Message)); }
        }

        private void BtnPowerTools_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenWorkbench();
        private void BtnLiveDraw_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.ToggleLiveDraw();
        private void BtnScreenRuler_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenScreenRuler();
        private void BtnFileShelf_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenFileShelf();
        private void BtnImageEditor_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenImageEditor();
        private void BtnDeveloperToolbox_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenDeveloperToolbox();
        private void BtnPrivacy_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenPrivacy();
        private void BtnQuickColor_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenColorPicker();
        private void BtnClipboardOpen_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenClipboardHistory();

        private void BtnClipboardClear_Click(object sender, RoutedEventArgs e)
        {
            ClipboardService.Instance.Clear();
            RefreshClipboardState();
        }

        private void BtnUnpin_Click(object sender, RoutedEventArgs e) => TopmostService.Instance.Unpin();
        private void BtnResetPinner_Click(object sender, RoutedEventArgs e) => TopmostService.Instance.ForceClearAll();

        private void PinnedOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtPinnedOpacity == null) return;
            TxtPinnedOpacity.Text = $"{(int)Math.Round(SldPinnedOpacity.Value)}%";
            if (_loading) return;
            SettingsService.Instance.Current.PinnedOpacity = (int)Math.Round(SldPinnedOpacity.Value);
            SettingsService.Instance.SaveWithoutNotification();
            TopmostService.Instance.ApplySettings();
        }

        private void PinnedClickThrough_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.Instance.Current.PinnedClickThrough = ChkPinnedClickThrough.IsChecked == true;
            SettingsService.Instance.SaveWithoutNotification();
            TopmostService.Instance.ApplySettings();
        }

        private void BtnReplayToggle_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.ToggleReplayBuffer();
            RefreshReplayCard();
        }

        private void BtnReplaySave_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.SaveReplay();
        private void BtnRecorderSettings_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.OpenRecorderSettings();
            RefreshReplayCard();
        }
    }
}
