using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CryoUtils.Models;
using CryoUtils.Services;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
using IOPath = System.IO.Path;

namespace CryoUtils.Views
{
    public partial class MainWindow : Window
    {
        private bool _initializing = true;
        private bool _loading;
        private bool _allowClose;
        private bool _searchingUpdates;
        private bool _downloadingUpdate;
        private string _currentTab = "Overview";
        private string? _compressSourcePath;
        private CompressionResult? _compressResult;
        private UpdateInfo? _availableUpdate;

        public MainWindow()
        {
            _initializing = true;
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FlowDirection = LocalizationService.Instance.FlowDirection;

            RecentItems.ItemsSource = RecentCaptureService.Instance.Items;
            RecentCaptureService.Instance.Items.CollectionChanged += RecentItems_CollectionChanged;
            RecentCaptureService.Instance.AllTimeItems.CollectionChanged += RecentItems_CollectionChanged;
            AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(MainContentScroll_PreviewMouseWheel), true);
            TopmostService.Instance.StateChanged += RefreshPinnedState;
            ClipboardService.Instance.Changed += RefreshClipboardState;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            ThemeService.ThemeChanged += OnThemeChanged;

            ReloadUi();
            _initializing = false;
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.Instance.ApplyWindow(this);
            RestoreWindowPosition();
            RefreshPinnedState();
            RefreshClipboardState();
            UpdateHotkeyStatus();
            MaybeAutoCheckUpdates();
        }

        private void RestoreWindowPosition()
        {
            var settings = SettingsService.Instance.Current;
            if (settings.WindowLeft is not double left || settings.WindowTop is not double top)
            {
                return;
            }

            double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 160;
            double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;
            if (left < SystemParameters.VirtualScreenLeft - 20 || left > maxLeft ||
                top < SystemParameters.VirtualScreenTop - 20 || top > maxTop)
            {
                return;
            }

            Left = left;
            Top = top;
        }

        private void SaveWindowPosition()
        {
            var settings = SettingsService.Instance.Current;
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            SettingsService.Instance.SaveWithoutNotification();
        }

        // ============================ Loading ============================
        private void ReloadUi()
        {
            _loading = true;
            try
            {
                var settings = SettingsService.Instance.Current;
                ChkQuickSave.IsChecked = settings.QuickSave;
                TxtSaveFolder.Text = SettingsService.Instance.EffectiveSaveFolder;
                TxtSaveFolder.FlowDirection = FlowDirection.LeftToRight;
                ChkAutoCopy.IsChecked = settings.AutoCopyCapture;
                ChkCaptureSound.IsChecked = settings.CaptureSound;
                ChkShowLoupe.IsChecked = settings.ShowCaptureLoupe;
                SelectTag(CmbImageFormat, settings.ImageFormat);
                SelectTag(CmbCaptureDelay, settings.CaptureDelaySeconds.ToString());
                SelectTag(CmbFps, settings.GifFps.ToString());
                SelectTag(CmbRecFormat, settings.RecorderFormat);
                SelectTag(CmbMaxSeconds, settings.MaxRecordingSeconds.ToString());
                ChkCopyOnStop.IsChecked = settings.RecorderCopyOnStop;

                SldThickness.Value = Math.Clamp(settings.AnnotationThickness, 1, 20);
                TxtThickness.Text = ((int)SldThickness.Value).ToString(CultureInfo.InvariantCulture);
                TxtStepStart.Text = Math.Clamp(settings.StepStartNumber, 1, 99).ToString(CultureInfo.InvariantCulture);
                TxtDefaultColor.Text = settings.AnnotationColor;
                DefaultColorSwatch.Background = new SolidColorBrush(
                    AnnotationRenderer.ParseColor(settings.AnnotationColor));

                ChkModuleCapture.IsChecked = settings.CaptureEnabled;
                ChkModuleColor.IsChecked = settings.ColorPickerEnabled;
                ChkModulePinner.IsChecked = settings.WindowPinnerEnabled;
                ChkModuleCompressor.IsChecked = settings.CompressorEnabled;
                ChkModuleClipboard.IsChecked = settings.ClipboardHistoryEnabled;

                SelectTag(CmbTheme, settings.Theme);
                SelectTag(CmbLanguage, settings.Language);
                ChkStartWithWindows.IsChecked = settings.StartWithWindows;
                ChkLaunchBackground.IsChecked = settings.LaunchInBackground;
                ChkNotifications.IsChecked = settings.NotificationsEnabled;
                SelectTag(
                    CmbTrayClick,
                    string.Equals(settings.TrayLeftClickAction, "Capture", StringComparison.OrdinalIgnoreCase)
                        ? "Capture"
                        : "Dashboard");
                ChkPinnedFrame.IsChecked = settings.ShowPinnedBorder;
                ChkPinnedSound.IsChecked = settings.PinnedBorderSound;
                ChkContextMenu.IsChecked = settings.ContextMenuEnabled;
                TxtSavedLimit.Text = Math.Clamp(settings.SavedCapturesLimit, 0, 10000).ToString(CultureInfo.InvariantCulture);
                SelectTag(
                    CmbRecentScope,
                    string.Equals(settings.RecentCapturesScope, "AllTime", StringComparison.OrdinalIgnoreCase)
                        ? "AllTime"
                        : "Session");
                ChkAutoUpdates.IsChecked = settings.AutomaticUpdateChecks;
                ChkSaveRollback.IsChecked = settings.SaveRollbackOnUpdate;

                SelectTag(CmbPreset, settings.CompressorPreset);
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

            string version = $"v{UpdaterService.CurrentVersion}";
            TxtTitleVersion.Text = version;
            TxtStatusVersion.Text = version;
            TxtAboutVersion.Text = LocalizationService.Format("About_Version", version);
            RefreshHotkeyRows();
            RefreshRecentColors();
            ApplyRecentScope();
            RefreshPinnedState();
            RefreshClipboardState();
            RefreshRollbackUi();
            UpdateModuleSummary();
            UpdateHotkeyStatus();
            UpdateCompressionAvailability();
            ShowTab(_currentTab);
            TxtLastChecked.Text = SettingsService.Instance.Current.LastUpdateCheckUtc is DateTime checkedUtc
                ? checkedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : LocalizationService.Get("About_Never");
            if (!string.IsNullOrEmpty(SettingsService.Instance.Current.LastUpdateVersion) &&
                SettingsService.Instance.Current.LastUpdateOutcome == "available")
            {
                TxtUpdateStatus.Text = LocalizationService.Format(
                    "About_UpdateAvailable",
                    SettingsService.Instance.Current.LastUpdateVersion);
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
        }

        private static string? SelectedTag(ComboBox comboBox)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        }

        // ============================ Window chrome ============================
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount == 1)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                }
            }
        }

        private DispatcherTimer? _quitConfirmTimer;
        private bool _isQuitConfirmPending;

        private void BtnQuit_Click(object sender, RoutedEventArgs e)
        {
            if (_isQuitConfirmPending)
            {
                ResetQuitConfirmState();
                SaveWindowPosition();
                App.CurrentApp?.ExitApplication();
                return;
            }

            _isQuitConfirmPending = true;
            PathQuitIcon.Data = IconData.Check;
            PathQuitIcon.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty, "DangerTextBrush");
            BtnQuit.SetResourceReference(Control.BackgroundProperty, "DangerSoftBackgroundBrush");
            BtnQuit.ToolTip = LocalizationService.Get("Main_TooltipQuitConfirm");

            _quitConfirmTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.5)
            };
            _quitConfirmTimer.Tick -= QuitConfirmTimer_Tick;
            _quitConfirmTimer.Tick += QuitConfirmTimer_Tick;
            _quitConfirmTimer.Stop();
            _quitConfirmTimer.Start();
        }

        private void QuitConfirmTimer_Tick(object? sender, EventArgs e)
        {
            ResetQuitConfirmState();
        }

        private void ResetQuitConfirmState()
        {
            _quitConfirmTimer?.Stop();
            _isQuitConfirmPending = false;
            if (PathQuitIcon != null)
            {
                PathQuitIcon.Data = IconData.Power;
                PathQuitIcon.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty, "IconBrush");
            }
            if (BtnQuit != null)
            {
                BtnQuit.ClearValue(Control.BackgroundProperty);
                BtnQuit.ToolTip = LocalizationService.Get("Main_TooltipQuit");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (TxtSavedLimit != null && TxtSavedLimit.IsKeyboardFocusWithin)
            {
                TxtSavedLimit_LostFocus(TxtSavedLimit, new RoutedEventArgs());
            }
            ResetQuitConfirmState();
            SaveWindowPosition();
            Hide();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            ResetQuitConfirmState();
            if (_allowClose)
            {
                return;
            }

            e.Cancel = true;
            SaveWindowPosition();
            Hide();
        }

        public void PrepareForShutdown()
        {
            _allowClose = true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && TxtSavedLimit != null && TxtSavedLimit.IsKeyboardFocusWithin)
            {
                TxtSavedLimit_LostFocus(TxtSavedLimit, new RoutedEventArgs());
                Keyboard.ClearFocus();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && !(Keyboard.FocusedElement is TextBox))
            {
                Hide();
                e.Handled = true;
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (_initializing || sender is not RadioButton radio || radio.Tag is not string tag)
            {
                return;
            }

            ShowTab(tag);
        }

        public void ShowTab(string tab)
        {
            _currentTab = string.IsNullOrWhiteSpace(tab) ? "Overview" : tab;
            if (_currentTab == "About")
            {
                _currentTab = "General";
            }

            PanelOverview.Visibility = _currentTab == "Overview" ? Visibility.Visible : Visibility.Collapsed;
            PanelCapture.Visibility = _currentTab == "Capture" ? Visibility.Visible : Visibility.Collapsed;
            PanelTools.Visibility = _currentTab == "Tools" ? Visibility.Visible : Visibility.Collapsed;
            PanelHotkeys.Visibility = _currentTab == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
            PanelGeneral.Visibility = _currentTab == "General" ? Visibility.Visible : Visibility.Collapsed;

            RadioButton? target = _currentTab switch
            {
                "Capture" => TabCapture,
                "Tools" => TabTools,
                "Hotkeys" => TabHotkeys,
                "General" => TabGeneral,
                _ => TabOverview
            };
            if (target != null && target.IsChecked != true)
            {
                target.IsChecked = true;
            }

            if (!_initializing && _currentTab == "Overview")
            {
                ApplyRecentScope();
            }

            MainContentScroll?.ScrollToTop();
        }

        private void MainContentScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (MainContentScroll == null || e.Delta == 0)
            {
                return;
            }

            // Let scrollable inner ScrollViewers (e.g., All-Time recent gallery or Update notes) consume wheel
            // while they actually have room to scroll in the requested direction; forward everything else.
            var current = e.OriginalSource as DependencyObject;
            while (current != null && !ReferenceEquals(current, MainContentScroll))
            {
                if (current is ScrollViewer inner &&
                    !ReferenceEquals(inner, MainContentScroll) &&
                    inner.ComputedVerticalScrollBarVisibility == Visibility.Visible &&
                    inner.ScrollableHeight > 0.5)
                {
                    bool scrollingUp = e.Delta > 0;
                    bool canScroll = scrollingUp
                        ? inner.VerticalOffset > 0.5
                        : inner.VerticalOffset < inner.ScrollableHeight - 0.5;
                    if (canScroll)
                    {
                        return;
                    }
                    break;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            if (MainContentScroll.ScrollableHeight <= 0)
            {
                return;
            }

            double nextOffset = Math.Clamp(
                MainContentScroll.VerticalOffset - (e.Delta * 0.6),
                0,
                MainContentScroll.ScrollableHeight);
            MainContentScroll.ScrollToVerticalOffset(nextOffset);
            e.Handled = true;
        }

        // ============================ Module toggles ============================
        private void ModuleToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || _initializing)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            string? tag = (sender as ToggleButton)?.Tag as string;
            switch (tag)
            {
                case "Capture":
                    settings.CaptureEnabled = ChkModuleCapture.IsChecked == true;
                    break;
                case "Color":
                    settings.ColorPickerEnabled = ChkModuleColor.IsChecked == true;
                    break;
                case "Pinner":
                    settings.WindowPinnerEnabled = ChkModulePinner.IsChecked == true;
                    if (!settings.WindowPinnerEnabled && TopmostService.Instance.IsPinned)
                    {
                        TopmostService.Instance.Unpin();
                    }
                    break;
                case "Compressor":
                    settings.CompressorEnabled = ChkModuleCompressor.IsChecked == true;
                    break;
                case "Clipboard":
                    settings.ClipboardHistoryEnabled = ChkModuleClipboard.IsChecked == true;
                    break;
            }

            SettingsService.Instance.Save();
            UpdateModuleSummary();
            UpdateHotkeyStatus();
            UpdateCompressionAvailability();
            RefreshClipboardState();
        }

        private void ReadModuleToggles()
        {
            var settings = SettingsService.Instance.Current;
            settings.CaptureEnabled = ChkModuleCapture.IsChecked == true;
            settings.ColorPickerEnabled = ChkModuleColor.IsChecked == true;
            settings.WindowPinnerEnabled = ChkModulePinner.IsChecked == true;
            settings.CompressorEnabled = ChkModuleCompressor.IsChecked == true;
            settings.ClipboardHistoryEnabled = ChkModuleClipboard.IsChecked == true;
        }

        private void UpdateModuleSummary()
        {
            var settings = SettingsService.Instance.Current;
            int enabled = 0;
            if (settings.CaptureEnabled) enabled++;
            if (settings.ColorPickerEnabled) enabled++;
            if (settings.WindowPinnerEnabled) enabled++;
            if (settings.CompressorEnabled) enabled++;
            if (settings.ClipboardHistoryEnabled) enabled++;
            TxtModuleCount.Text = LocalizationService.Format("Main_ActiveModules", enabled);
        }

        // ============================ Settings ============================
        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || _initializing)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            ReadModuleToggles();
            settings.QuickSave = ChkQuickSave.IsChecked == true;
            settings.SaveFolder = TxtSaveFolder.Text == SettingsService.Instance.DefaultSaveFolder
                ? ""
                : TxtSaveFolder.Text;
            settings.AutoCopyCapture = ChkAutoCopy.IsChecked == true;
            settings.CaptureSound = ChkCaptureSound.IsChecked == true;
            settings.ShowCaptureLoupe = ChkShowLoupe.IsChecked == true;
            settings.ImageFormat = SelectedTag(CmbImageFormat) ?? "PNG";
            settings.CaptureDelaySeconds = int.TryParse(SelectedTag(CmbCaptureDelay), out int delay) ? delay : 0;
            settings.GifFps = int.TryParse(SelectedTag(CmbFps), out int fps) ? fps : 15;
            settings.RecorderFormat = SelectedTag(CmbRecFormat) ?? "GIF";
            settings.MaxRecordingSeconds = int.TryParse(SelectedTag(CmbMaxSeconds), out int seconds) ? seconds : 30;
            settings.RecorderCopyOnStop = ChkCopyOnStop.IsChecked == true;
            settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
            settings.LaunchInBackground = ChkLaunchBackground.IsChecked == true;
            settings.NotificationsEnabled = ChkNotifications.IsChecked == true;
            settings.TrayLeftClickAction = SelectedTag(CmbTrayClick) ?? "Dashboard";
            settings.ShowPinnedBorder = ChkPinnedFrame.IsChecked == true;
            settings.PinnedBorderSound = ChkPinnedSound.IsChecked == true;
            settings.ContextMenuEnabled = ChkContextMenu.IsChecked == true;
            settings.AutomaticUpdateChecks = ChkAutoUpdates.IsChecked == true;
            settings.SaveRollbackOnUpdate = ChkSaveRollback.IsChecked == true;
            settings.CompressorPreset = SelectedTag(CmbPreset) ?? settings.CompressorPreset;

            CryoContextMenuService.SetEnabled(settings.ContextMenuEnabled);
            StartupService.SetEnabled(settings.StartWithWindows);
            SettingsService.Instance.Save();
            UpdateCompressionAvailability();
            TopmostService.Instance.ApplySettings();
            RefreshPinnedState();
        }

        private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _initializing)
            {
                return;
            }

            string theme = SelectedTag(CmbTheme) ?? "Dark";
            SettingsService.Instance.Current.Theme = theme;
            ThemeService.Apply(theme);
            SettingsService.Instance.Save();
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _initializing)
            {
                return;
            }

            string language = SelectedTag(CmbLanguage) ?? "English";
            SettingsService.Instance.Current.Language = language;
            LocalizationService.Instance.Apply(language);
            SettingsService.Instance.Save();
        }

        private void OnLanguageChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LocalizationService.Instance.ApplyWindow(this);
                RefreshHotkeyRows();
                RefreshPinnedState();
                RefreshClipboardState();
                UpdateModuleSummary();
                UpdateHotkeyStatus();
                RefreshRecentColors();
            }));
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshHotkeyRows();
                RefreshRecentColors();
                UpdateCompressionAvailability();
            }));
        }

        private void Thickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtThickness == null)
            {
                return;
            }

            TxtThickness.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture);
            if (_loading || _initializing)
            {
                return;
            }

            SettingsService.Instance.Current.AnnotationThickness = (int)Math.Round(e.NewValue);
            SettingsService.Instance.SaveWithoutNotification();
        }

        private void TxtStepStart_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtStepStart.Text, out int step))
            {
                step = 1;
            }
            step = Math.Clamp(step, 1, 99);
            TxtStepStart.Text = step.ToString(CultureInfo.InvariantCulture);
            SettingsService.Instance.Current.StepStartNumber = step;
            SettingsService.Instance.SaveWithoutNotification();
        }

        private void BtnDefaultColor_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(
                    AnnotationRenderer.ParseColor(SettingsService.Instance.Current.AnnotationColor).R,
                    AnnotationRenderer.ParseColor(SettingsService.Instance.Current.AnnotationColor).G,
                    AnnotationRenderer.ParseColor(SettingsService.Instance.Current.AnnotationColor).B)
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            SettingsService.Instance.Current.AnnotationColor = hex;
            SettingsService.Instance.Save();
            TxtDefaultColor.Text = hex;
            DefaultColorSwatch.Background = new SolidColorBrush(
                Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        }

        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = LocalizationService.Get("Capture_Folder"),
                InitialDirectory = SettingsService.Instance.EffectiveSaveFolder
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            TxtSaveFolder.Text = dialog.FolderName;
            SettingsService.Instance.Current.SaveFolder = dialog.FolderName;
            SettingsService.Instance.Save();
            ApplyRecentScope();
        }

        private void TxtSavedLimit_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading || _initializing)
            {
                return;
            }

            if (!int.TryParse(TxtSavedLimit.Text, out int limit))
            {
                limit = 50;
            }
            limit = Math.Clamp(limit, 0, 10000);
            TxtSavedLimit.Text = limit.ToString(CultureInfo.InvariantCulture);
            SettingsService.Instance.Current.SavedCapturesLimit = limit;
            SettingsService.Instance.SaveWithoutNotification();
            ScreenCaptureService.EnforceSavedCaptureLimit();
            ApplyRecentScope();
        }

        private void BtnUseFallback_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.Instance.Current;
            settings.CaptureHotkeyModifiers = "Ctrl+Shift";
            settings.CaptureHotkeyKey = "S";
            settings.PrintScreenFallbackSuggested = false;
            SettingsService.Instance.Save();
            PrintScreenBanner.Visibility = Visibility.Collapsed;
            UpdateHotkeyStatus();
        }

        // ============================ Hotkeys ============================
        private void RefreshHotkeyRows()
        {
            HotkeyRowsPanel.Children.Clear();
            var settings = SettingsService.Instance.Current;

            AddHotkeyRow(
                LocalizationService.Get("Hotkeys_Capture"),
                LocalizationService.Get("Module_CaptureDesc"),
                settings.CaptureHotkeyModifiers,
                settings.CaptureHotkeyKey,
                (modifiers, key) =>
                {
                    settings.CaptureHotkeyModifiers = modifiers;
                    settings.CaptureHotkeyKey = key;
                });

            AddHotkeyRow(
                LocalizationService.Get("Hotkeys_ColorPicker"),
                LocalizationService.Get("Module_ColorDesc"),
                settings.ColorPickerHotkeyModifiers,
                settings.ColorPickerHotkeyKey,
                (modifiers, key) =>
                {
                    settings.ColorPickerHotkeyModifiers = modifiers;
                    settings.ColorPickerHotkeyKey = key;
                });

            AddHotkeyRow(
                LocalizationService.Get("Hotkeys_Pinner"),
                LocalizationService.Get("Module_PinnerDesc"),
                settings.WindowPinnerHotkeyModifiers,
                settings.WindowPinnerHotkeyKey,
                (modifiers, key) =>
                {
                    settings.WindowPinnerHotkeyModifiers = modifiers;
                    settings.WindowPinnerHotkeyKey = key;
                });

            AddHotkeyRow(
                LocalizationService.Get("Hotkeys_PlainPaste"),
                LocalizationService.Get("Module_ClipboardDesc"),
                settings.PlainPasteHotkeyModifiers,
                settings.PlainPasteHotkeyKey,
                (modifiers, key) =>
                {
                    settings.PlainPasteHotkeyModifiers = modifiers;
                    settings.PlainPasteHotkeyKey = key;
                });

            AddHotkeyRow(
                LocalizationService.Get("Hotkeys_History"),
                LocalizationService.Get("Module_ClipboardDesc"),
                settings.ClipboardHotkeyModifiers,
                settings.ClipboardHotkeyKey,
                (modifiers, key) =>
                {
                    settings.ClipboardHotkeyModifiers = modifiers;
                    settings.ClipboardHotkeyKey = key;
                });
        }

        private void AddHotkeyRow(
            string title,
            string description,
            string modifiers,
            string key,
            Action<string, string> apply)
        {
            var row = new Border
            {
                BorderBrush = ThemeService.GetBrush("DividerBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 10, 12, 10)
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
                Text = description,
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
                FontFamily = (FontFamily)FindResource("CryoMono"),
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
                    Owner = this
                };
                if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.KeyToken))
                {
                    return;
                }

                apply(dialog.ModifiersToken, dialog.KeyToken);
                SettingsService.Instance.Save();
                RefreshHotkeyRows();
                UpdateHotkeyStatus();
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
            settings.PrintScreenFallbackSuggested = false;
            SettingsService.Instance.Save();
            RefreshHotkeyRows();
            UpdateHotkeyStatus();
        }

        private void UpdateHotkeyStatus()
        {
            var settings = SettingsService.Instance.Current;
            string display = GlobalHotkeyService.FormatShortcutDisplay(
                settings.CaptureHotkeyModifiers,
                settings.CaptureHotkeyKey);
            if (TxtHeaderHotkey != null) TxtHeaderHotkey.Text = display;
            TxtHotkeyCapture.Text = display;
            TxtHotkeyColor.Text = GlobalHotkeyService.FormatShortcutDisplay(
                settings.ColorPickerHotkeyModifiers, settings.ColorPickerHotkeyKey);
            TxtHotkeyPinner.Text = GlobalHotkeyService.FormatShortcutDisplay(
                settings.WindowPinnerHotkeyModifiers, settings.WindowPinnerHotkeyKey);
            TxtHotkeyClipboard.Text = GlobalHotkeyService.FormatShortcutDisplay(
                settings.ClipboardHotkeyModifiers, settings.ClipboardHotkeyKey);

            bool printScreen = string.Equals(
                settings.CaptureHotkeyKey,
                "PrintScreen",
                StringComparison.OrdinalIgnoreCase);
            bool contested = settings.CaptureEnabled && printScreen &&
                !GlobalHotkeyService.Instance.IsRegistered(GlobalHotkeyService.CaptureHotkeyId);
            PrintScreenBanner.Visibility = contested ? Visibility.Visible : Visibility.Collapsed;
        }

        // ============================ Recent ============================
        private void CmbRecentScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _initializing)
            {
                return;
            }

            string scope = SelectedTag(CmbRecentScope) ?? "Session";
            SettingsService.Instance.Current.RecentCapturesScope = scope;
            SettingsService.Instance.SaveWithoutNotification();
            ApplyRecentScope();
        }

        private void ApplyRecentScope()
        {
            bool allTime = string.Equals(
                SettingsService.Instance.Current.RecentCapturesScope,
                "AllTime",
                StringComparison.OrdinalIgnoreCase);

            if (allTime)
            {
                RecentCaptureService.Instance.ReloadAllTime();
                RecentItems.ItemsSource = RecentCaptureService.Instance.AllTimeItems;
            }
            else
            {
                RecentItems.ItemsSource = RecentCaptureService.Instance.Items;
            }

            RefreshRecentStrip();
        }

        private void RecentItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RefreshRecentStrip));
        }

        private void RefreshRecentStrip()
        {
            bool hasItems = (RecentItems.ItemsSource as System.Collections.ICollection)?.Count > 0;
            RecentEmpty.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            RecentScroll.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RecentThumbnail_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not RecentCapture capture)
            {
                return;
            }

            if (!string.IsNullOrEmpty(capture.Path) &&
                File.Exists(capture.Path) &&
                string.Equals(IOPath.GetExtension(capture.Path), ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(capture.Path) { UseShellExecute = true });
                }
                catch
                {
                }
                return;
            }

            var bitmap = capture.Image;
            if (bitmap == null && !string.IsNullOrEmpty(capture.Path) && File.Exists(capture.Path))
            {
                try
                {
                    bitmap = ImageProcessingService.LoadWpfBitmap(capture.Path);
                }
                catch
                {
                }
            }

            if (bitmap == null)
            {
                return;
            }

            CaptureController.Instance.StartForImage(bitmap, capture.Path);
        }

        private void RecentCopy_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not RecentCapture capture)
            {
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(capture.Path) && File.Exists(capture.Path) &&
                    (IOPath.GetExtension(capture.Path).Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                     IOPath.GetExtension(capture.Path).Equals(".gif", StringComparison.OrdinalIgnoreCase)))
                {
                    var files = new System.Collections.Specialized.StringCollection { capture.Path };
                    Clipboard.SetFileDropList(files);
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
                    return;
                }

                var bitmap = capture.Image;
                if (bitmap == null && !string.IsNullOrEmpty(capture.Path) && File.Exists(capture.Path))
                {
                    bitmap = ImageProcessingService.LoadWpfBitmap(capture.Path);
                }

                if (bitmap == null)
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_FolderMissing"));
                    return;
                }

                Clipboard.SetImage(bitmap);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
            }
            catch
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Error"));
            }
        }

        private void RecentCompress_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not RecentCapture capture)
            {
                return;
            }

            string? path = capture.Path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (capture.Image is not BitmapSource srcBitmap)
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Tools_CompressFailed"));
                    return;
                }

                try
                {
                    string folder = IOPath.Combine(IOPath.GetTempPath(), "CryoUtils");
                    Directory.CreateDirectory(folder);
                    path = IOPath.Combine(folder, ScreenCaptureService.CreateTimestampedFileName(".png"));
                    ImageIO.SaveBitmap(srcBitmap, path);
                }
                catch
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Tools_CompressFailed"));
                    return;
                }
            }

            LoadCompressionSource(path);
            ShowTab("Tools");
            Activate();
        }

        private void RecentOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is RecentCapture capture &&
                !string.IsNullOrEmpty(capture.Path) &&
                File.Exists(capture.Path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{capture.Path}\"",
                        UseShellExecute = true
                    });
                }
                catch
                {
                }
                return;
            }

            try
            {
                string folder = SettingsService.Instance.EffectiveSaveFolder;
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_FolderMissing"));
            }
        }

        private DispatcherTimer? _deleteResetTimer;

        private void RecentDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not RecentCapture capture)
            {
                return;
            }

            if (!capture.IsConfirmingDelete)
            {
                // First click: activate confirmation mode on this card
                foreach (var item in RecentCaptureService.Instance.Items)
                {
                    if (item != capture && item.IsConfirmingDelete)
                    {
                        item.IsConfirmingDelete = false;
                    }
                }

                capture.IsConfirmingDelete = true;

                _deleteResetTimer?.Stop();
                _deleteResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                _deleteResetTimer.Tick += (_, _) =>
                {
                    capture.IsConfirmingDelete = false;
                    _deleteResetTimer?.Stop();
                };
                _deleteResetTimer.Start();
                return;
            }

            // Second click: user confirmed!
            _deleteResetTimer?.Stop();
            try
            {
                if (!string.IsNullOrEmpty(capture.Path) && File.Exists(capture.Path))
                {
                    File.Delete(capture.Path);
                }
                RecentCaptureService.Instance.Remove(capture);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_Deleted"));
            }
            catch
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_DeleteFailed"));
            }
        }

        // ============================ Compression ============================
        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
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

        private void DropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BrowseCompressionFile();
        }

        private void BtnBrowseCompress_Click(object sender, RoutedEventArgs e)
        {
            BrowseCompressionFile();
        }

        private void BrowseCompressionFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Get("Tools_Dropzone"),
                Filter = LocalizationService.Get("Common_ImageFilter"),
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
            {
                LoadCompressionSource(dialog.FileName);
            }
        }

        private void LoadCompressionSource(string path)
        {
            string extension = IOPath.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif"))
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Tools_CompressFailed"));
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
                var bytes = File.ReadAllBytes(path);
                ImgCompressedPreview.Source = ImageIO.DecodeBytes(bytes);
            }
            catch
            {
                ImgCompressedPreview.Source = null;
            }
            UpdateCompressionAvailability();
        }

        private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _initializing || TxtQualityValue == null)
            {
                return;
            }

            string preset = SelectedTag(CmbPreset) ?? "Discord";
            _loading = true;
            try
            {
                switch (preset)
                {
                    case "Discord":
                        SldQuality.Value = 88;
                        SldScale.Value = 100;
                        break;
                    case "WhatsApp":
                        SldQuality.Value = 70;
                        SldScale.Value = 80;
                        break;
                }
                TxtQualityValue.Text = $"{(int)SldQuality.Value}%";
                TxtScaleValue.Text = $"{(int)SldScale.Value}%";
            }
            finally
            {
                _loading = false;
            }

            SettingsService.Instance.Current.CompressorPreset = preset;
            SettingsService.Instance.SaveWithoutNotification();
            UpdatePresetSliderState();
        }

        private void UpdatePresetSliderState()
        {
            bool custom = SelectedTag(CmbPreset) == "Custom";
            SldQuality.IsEnabled = custom;
            SldScale.IsEnabled = custom;
        }

        private void Compression_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtQualityValue == null || TxtScaleValue == null)
            {
                return;
            }

            TxtQualityValue.Text = $"{(int)Math.Round(SldQuality.Value)}%";
            TxtScaleValue.Text = $"{(int)Math.Round(SldScale.Value)}%";

            if (_loading || _initializing)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            settings.CompressorQuality = (int)Math.Round(SldQuality.Value);
            settings.CompressorScale = (int)Math.Round(SldScale.Value);
            SettingsService.Instance.SaveWithoutNotification();
        }

        private void UpdateCompressionAvailability()
        {
            bool enabled = SettingsService.Instance.Current.CompressorEnabled;
            bool custom = SelectedTag(CmbPreset) == "Custom";
            BtnCompress.IsEnabled = enabled && !string.IsNullOrEmpty(_compressSourcePath);
            CmbPreset.IsEnabled = enabled;
            SldQuality.IsEnabled = enabled && custom;
            SldScale.IsEnabled = enabled && custom;
        }

        private async void BtnCompress_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_compressSourcePath) || !File.Exists(_compressSourcePath))
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Tools_NoResult"));
                return;
            }
            if (!SettingsService.Instance.Current.CompressorEnabled)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }

            string preset = SelectedTag(CmbPreset) ?? "Discord";
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
                var result = await CompressionService.CompressAsync(
                    _compressSourcePath,
                    preset,
                    quality,
                    scale);
                _compressResult = result;
                ImgCompressedPreview.Source = result.Preview;
                TxtBeforeSize.Text = ImageIO.FormatBytes(result.OriginalBytes);
                TxtAfterSize.Text = ImageIO.FormatBytes(result.CompressedBytes);
                TxtSaved.Text = result.SavedPercent > 0
                    ? $"{(int)Math.Round(result.SavedPercent)}%"
                    : "0%";
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
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Error_Body", ex.Message));
            }
            finally
            {
                SetCompressionBusy(false);
            }
        }

        private void SetCompressionBusy(bool busy)
        {
            BtnCompress.IsEnabled = !busy && SettingsService.Instance.Current.CompressorEnabled &&
                !string.IsNullOrEmpty(_compressSourcePath);
            BtnSaveCompressed.IsEnabled = !busy && _compressResult != null;
            BtnCopyCompressed.IsEnabled = !busy && _compressResult != null;
        }

        private void BtnSaveCompressed_Click(object sender, RoutedEventArgs e)
        {
            if (_compressResult == null)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Tools_SaveAs"),
                Filter = LocalizationService.Get("Common_ImageFilter"),
                DefaultExt = _compressResult.Extension,
                AddExtension = true,
                FileName = ScreenCaptureService.CreateTimestampedFileName(_compressResult.Extension),
                InitialDirectory = SettingsService.Instance.EffectiveSaveFolder
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                File.WriteAllBytes(dialog.FileName, _compressResult.Data);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("General_ExportDone"));
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Error_Body", ex.Message));
            }
        }

        private void BtnCopyCompressed_Click(object sender, RoutedEventArgs e)
        {
            if (_compressResult == null)
            {
                return;
            }

            try
            {
                string folder = IOPath.Combine(IOPath.GetTempPath(), "CryoUtils");
                Directory.CreateDirectory(folder);
                string path = IOPath.Combine(
                    folder,
                    ScreenCaptureService.CreateTimestampedFileName(_compressResult.Extension));
                File.WriteAllBytes(path, _compressResult.Data);
                Clipboard.SetFileDropList(new StringCollection { path });
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Error_Body", ex.Message));
            }
        }

        // ============================ Tools cards ============================
        private void RefreshRecentColors()
        {
            RecentColorsPanel.Children.Clear();
            var colors = SettingsService.Instance.Current.RecentColors;
            TxtRecentColorsEmpty.Visibility = colors.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

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
                        (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
                    }
                    catch
                    {
                    }
                };
                RecentColorsPanel.Children.Add(button);
            }
        }

        private void RefreshPinnedState()
        {
            var topmost = TopmostService.Instance;
            if (TxtPinnedWindow != null)
            {
                TxtPinnedWindow.Text = topmost.IsPinned
                    ? LocalizationService.Format("Tools_PinnedActive", topmost.PinnedTitle)
                    : LocalizationService.Get("Tools_PinnedNone");
            }
            if (BtnUnpin != null)
            {
                BtnUnpin.IsEnabled = topmost.IsPinned;
            }

            if (BtnOverviewUnpin != null)
            {
                BtnOverviewUnpin.Visibility = topmost.IsPinned ? Visibility.Visible : Visibility.Collapsed;
            }
            if (TxtOverviewPinnedStatus != null)
            {
                TxtOverviewPinnedStatus.Text = topmost.IsPinned
                    ? LocalizationService.Format("Tools_PinnedActive", topmost.PinnedTitle)
                    : LocalizationService.Get("Module_PinnerDesc");
                TxtOverviewPinnedStatus.Foreground = topmost.IsPinned
                    ? ThemeService.GetBrush("AccentTextBrush")
                    : ThemeService.GetBrush("TextMutedBrush");
            }
        }

        private void BtnUnpin_Click(object sender, RoutedEventArgs e)
        {
            TopmostService.Instance.Unpin();
        }

        private void BtnResetPinner_Click(object sender, RoutedEventArgs e)
        {
            TopmostService.Instance.ForceClearAll();
        }

        private void PinnedOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtPinnedOpacity == null) return;
            TxtPinnedOpacity.Text = $"{(int)Math.Round(SldPinnedOpacity.Value)}%";
            if (_loading || _initializing) return;
            SettingsService.Instance.Current.PinnedOpacity = (int)Math.Round(SldPinnedOpacity.Value);
            SettingsService.Instance.SaveWithoutNotification();
            TopmostService.Instance.ApplySettings();
        }

        private void PinnedClickThrough_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || _initializing) return;
            SettingsService.Instance.Current.PinnedClickThrough = ChkPinnedClickThrough.IsChecked == true;
            SettingsService.Instance.SaveWithoutNotification();
            TopmostService.Instance.ApplySettings();
        }

        private void ContrastColor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _initializing) return;
            UpdateDashboardContrast();
        }

        private void BtnSwapContrastColors_Click(object sender, RoutedEventArgs e)
        {
            string temp = TxtContrastA.Text;
            TxtContrastA.Text = TxtContrastB.Text;
            TxtContrastB.Text = temp;
        }

        private void UpdateDashboardContrast()
        {
            if (TxtContrastA == null || TxtContrastB == null || SwatchContrastA == null || SwatchContrastB == null || TxtDashboardContrastRatio == null)
            {
                return;
            }

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

            TxtWcagAaNormal.Text = $"{normalLabel}: {(eval.AaNormal ? passText : failText)}";
            BadgeWcagAaNormal.Background = eval.AaNormal ? ThemeService.GetBrush("SuccessMutedBrush") : ThemeService.GetBrush("InsetBackgroundBrush");
            TxtWcagAaNormal.Foreground = eval.AaNormal ? ThemeService.GetBrush("SuccessBrush") : ThemeService.GetBrush("TextMutedBrush");

            TxtWcagAaLarge.Text = $"{largeLabel}: {(eval.AaLarge ? passText : failText)}";
            BadgeWcagAaLarge.Background = eval.AaLarge ? ThemeService.GetBrush("SuccessMutedBrush") : ThemeService.GetBrush("InsetBackgroundBrush");
            TxtWcagAaLarge.Foreground = eval.AaLarge ? ThemeService.GetBrush("SuccessBrush") : ThemeService.GetBrush("TextMutedBrush");

            TxtWcagAaaNormal.Text = $"{normalLabel}: {(eval.AaaNormal ? passText : failText)}";
            BadgeWcagAaaNormal.Background = eval.AaaNormal ? ThemeService.GetBrush("SuccessMutedBrush") : ThemeService.GetBrush("InsetBackgroundBrush");
            TxtWcagAaaNormal.Foreground = eval.AaaNormal ? ThemeService.GetBrush("SuccessBrush") : ThemeService.GetBrush("TextMutedBrush");

            TxtWcagAaaLarge.Text = $"{largeLabel}: {(eval.AaaLarge ? passText : failText)}";
            BadgeWcagAaaLarge.Background = eval.AaaLarge ? ThemeService.GetBrush("SuccessMutedBrush") : ThemeService.GetBrush("InsetBackgroundBrush");
            TxtWcagAaaLarge.Foreground = eval.AaaLarge ? ThemeService.GetBrush("SuccessBrush") : ThemeService.GetBrush("TextMutedBrush");
        }

        private void TxtQrInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            RegenerateDashboardQr();
        }

        private void CmbQrCorrection_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RegenerateDashboardQr();
        }

        private void RegenerateDashboardQr()
        {
            if (TxtQrInput == null || ImgQrPreview == null || BtnCopyQr == null || BtnSaveQr == null)
            {
                return;
            }

            string text = TxtQrInput.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                ImgQrPreview.Source = null;
                BtnCopyQr.IsEnabled = false;
                BtnSaveQr.IsEnabled = false;
                return;
            }

            string levelStr = (CmbQrCorrection.SelectedItem as ComboBoxItem)?.Tag as string ?? "M";
            var level = levelStr switch
            {
                "L" => ZXing.QrCode.Internal.ErrorCorrectionLevel.L,
                "Q" => ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
                "H" => ZXing.QrCode.Internal.ErrorCorrectionLevel.H,
                _ => ZXing.QrCode.Internal.ErrorCorrectionLevel.M
            };

            try
            {
                var bitmap = QrCodeService.Encode(text, 256, level);
                ImgQrPreview.Source = bitmap;
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
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
                }
                catch
                {
                }
            }
        }

        private void BtnSaveQr_Click(object sender, RoutedEventArgs e)
        {
            if (ImgQrPreview.Source is not BitmapSource bitmap)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Tools_QrSave"),
                Filter = "PNG Image (*.png)|*.png",
                DefaultExt = ".png",
                FileName = $"QRCode_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };
            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    byte[] bytes = ImageIO.EncodePng(bitmap);
                    File.WriteAllBytes(dialog.FileName, bytes);
                    RecentCaptureService.Instance.Add(bitmap, dialog.FileName, LocalizationService.Get("Tools_QrTitle"));
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("General_ExportDone"));
                }
                catch (Exception ex)
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Format("Error_Body", ex.Message));
                }
            }
        }

        private void BtnQrScanScreen_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.StartCapture();
        }

        private void BtnQrScanFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Get("Tools_QrScanFile"),
                Filter = LocalizationService.Get("Common_ImageFilter")
            };
            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    var uri = new Uri(dialog.FileName, UriKind.Absolute);
                    var bitmap = new BitmapImage(uri);
                    var decoded = QrCodeService.Decode(bitmap);
                    if (decoded == null || string.IsNullOrWhiteSpace(decoded.Text))
                    {
                        (Application.Current as App)?.ShowToast(LocalizationService.Get("Qr_NotFound"));
                        return;
                    }

                    // Add scanned image to Recent Captures strip
                    RecentCaptureService.Instance.Add(bitmap, dialog.FileName, LocalizationService.Get("Tools_QrTitle"));

                    var window = new QrResultWindow(decoded.Text, decoded.FormatName, bitmap);
                    window.Show();
                    window.Activate();
                }
                catch (Exception ex)
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Format("Error_Body", ex.Message));
                }
            }
        }

        private void RefreshClipboardState()
        {
            if (TxtClipboardCount == null)
            {
                return;
            }

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

        private void BtnClipboardOpen_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.OpenClipboardHistory();
        }

        private void BtnClipboardClear_Click(object sender, RoutedEventArgs e)
        {
            ClipboardService.Instance.Clear();
            RefreshClipboardState();
        }

        // ============================ Launchers ============================
        private void BtnHeroCapture_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.StartCapture();
        }

        private void BtnRecordNow_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.StartRecording();
        }

        private void BtnQuickCapture_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.StartCapture();
        }

        private void BtnQuickColor_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.OpenColorPicker();
        }

        private void BtnQuickPinner_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.TogglePinnerFromDashboard();
        }

        private void BtnQuickClipboard_Click(object sender, RoutedEventArgs e)
        {
            App.CurrentApp?.OpenClipboardHistory();
        }

        private void BtnGoCompress_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("Tools");
        }

        private void BtnGoQr_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("Tools");
        }

        // ============================ Data ============================
        private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("General_Export"),
                Filter = LocalizationService.Get("Common_JsonFilter"),
                DefaultExt = ".json",
                AddExtension = true,
                FileName = $"CryoUtils_settings_{DateTime.Now:yyyy-MM-dd}.json"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                SettingsService.Instance.Export(dialog.FileName);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("General_ExportDone"));
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Error_Body", ex.Message));
            }
        }

        private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Get("General_Import"),
                Filter = LocalizationService.Get("Common_JsonFilter"),
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            if (MessageBox.Show(
                    this,
                    LocalizationService.Get("General_ImportConfirm"),
                    LocalizationService.Get("General_Import"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                SettingsService.Instance.Import(dialog.FileName);
                App.CurrentApp?.ApplyAllSettings();
                ReloadUi();
                (Application.Current as App)?.ShowToast(LocalizationService.Get("General_ImportDone"));
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Error_Body", ex.Message));
            }
        }

        private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                    this,
                    LocalizationService.Get("General_ResetConfirm"),
                    LocalizationService.Get("General_Reset"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            SettingsService.Instance.Reset();
            App.CurrentApp?.ApplyAllSettings();
            ReloadUi();
            (Application.Current as App)?.ShowToast(LocalizationService.Get("General_ResetDone"));
        }

        // ============================ About / updates ============================
        private void BtnRepo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/ZerroDevs/CryoUtils")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(silent: false);
        }

        private async void MaybeAutoCheckUpdates()
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.AutomaticUpdateChecks)
            {
                return;
            }

            DateTime? last = settings.LastUpdateCheckUtc;
            if (last.HasValue && DateTime.UtcNow - last.Value < TimeSpan.FromHours(24))
            {
                return;
            }

            await Task.Delay(1200);
            await CheckForUpdatesAsync(silent: true);
        }

        private async Task CheckForUpdatesAsync(bool silent)
        {
            if (_searchingUpdates || _downloadingUpdate)
            {
                return;
            }

            _searchingUpdates = true;
            BtnCheckUpdates.IsEnabled = false;
            if (!silent)
            {
                TxtUpdateStatus.Text = LocalizationService.Get("About_Checking");
            }

            var settings = SettingsService.Instance.Current;
            try
            {
                _availableUpdate = await UpdaterService.CheckAsync();
                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                if (_availableUpdate == null)
                {
                    settings.LastUpdateOutcome = "current";
                    settings.LastUpdateVersion = UpdaterService.CurrentVersion;
                    TxtUpdateStatus.Text = LocalizationService.Get("About_UpToDate");
                    UpdateNotesPanel.Visibility = Visibility.Collapsed;
                    BtnDownloadUpdate.Visibility = Visibility.Collapsed;
                }
                else
                {
                    settings.LastUpdateOutcome = "available";
                    settings.LastUpdateVersion = _availableUpdate.Version.ToString();
                    TxtUpdateStatus.Text = LocalizationService.Format(
                        "About_UpdateAvailable",
                        _availableUpdate.Version);
                    TxtUpdateNotes.Text = string.IsNullOrWhiteSpace(_availableUpdate.Notes)
                        ? LocalizationService.Get("About_WhatsNew")
                        : _availableUpdate.Notes;
                    UpdateNotesPanel.Visibility = Visibility.Visible;
                    BtnDownloadUpdate.Visibility = Visibility.Visible;
                    BtnDownloadUpdate.IsEnabled = _availableUpdate.HasAsset;
                    if (!_availableUpdate.HasAsset)
                    {
                        TxtUpdateStatus.Text = LocalizationService.Get("Update_NoAsset");
                    }
                    if (silent)
                    {
                        (Application.Current as App)?.ShowToast(
                            LocalizationService.Format("About_UpdateAvailable", _availableUpdate.Version));
                    }
                }
                SettingsService.Instance.SaveWithoutNotification();
            }
            catch (Exception)
            {
                settings.LastUpdateOutcome = "failed";
                TxtUpdateStatus.Text = LocalizationService.Get("About_CheckFailed");
                SettingsService.Instance.SaveWithoutNotification();
            }
            finally
            {
                TxtLastChecked.Text = settings.LastUpdateCheckUtc is DateTime checkedUtc
                    ? checkedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    : LocalizationService.Get("About_Never");
                BtnCheckUpdates.IsEnabled = true;
                _searchingUpdates = false;
                RefreshRollbackUi();
            }
        }

        private async void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate == null || _downloadingUpdate)
            {
                return;
            }

            var info = _availableUpdate;
            if (!info.HasAsset)
            {
                TxtUpdateStatus.Text = LocalizationService.Get("Update_NoAsset");
                BtnDownloadUpdate.IsEnabled = false;
                return;
            }
            if (MessageBox.Show(
                    this,
                    LocalizationService.Format("About_DownloadConfirm", info.Version),
                    LocalizationService.Get("About_Download"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            if (SettingsService.Instance.Current.SaveRollbackOnUpdate)
            {
                try
                {
                    UpdaterService.CreateBackup();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        LocalizationService.Format("Error_Body", ex.Message),
                        LocalizationService.Get("Error_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            _downloadingUpdate = true;
            BtnDownloadUpdate.IsEnabled = false;
            BtnCheckUpdates.IsEnabled = false;
            UpdateProgress.Visibility = Visibility.Visible;
            UpdateProgress.Value = 0;
            try
            {
                var progress = new Progress<double>(value =>
                {
                    UpdateProgress.Value = Math.Clamp(value * 100, 0, 100);
                    TxtUpdateStatus.Text = LocalizationService.Format(
                        "About_Downloading",
                        (int)Math.Round(value * 100));
                });
                string installer = await UpdaterService.DownloadAsync(info, progress);
                TxtUpdateStatus.Text = LocalizationService.Get("Update_Ready");
                UpdaterService.ApplyUpdate(installer);
            }
            catch (Exception ex)
            {
                TxtUpdateStatus.Text = LocalizationService.Get("Update_InstallFailed");
                MessageBox.Show(
                    this,
                    LocalizationService.Format("Error_Body", ex.Message),
                    LocalizationService.Get("Error_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                BtnDownloadUpdate.IsEnabled = true;
                BtnCheckUpdates.IsEnabled = true;
                _downloadingUpdate = false;
                UpdateProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshRollbackUi()
        {
            bool ready = UpdaterService.IsBackupReady;
            BtnRestore.IsEnabled = ready;
            BtnDeleteBackup.IsEnabled = ready;
            if (ready)
            {
                TxtRollbackStatus.Text = LocalizationService.Format(
                    "About_BackupReady",
                    string.IsNullOrEmpty(UpdaterService.BackupVersion)
                        ? "?"
                        : UpdaterService.BackupVersion);
            }
            else
            {
                TxtRollbackStatus.Text = LocalizationService.Get("About_NoBackup");
            }
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!UpdaterService.IsBackupReady)
            {
                return;
            }

            if (MessageBox.Show(
                    this,
                    LocalizationService.Get("About_RestoreConfirm"),
                    LocalizationService.Get("About_Restore"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                UpdaterService.RestoreBackup();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    LocalizationService.Format("Error_Body", ex.Message),
                    LocalizationService.Get("Error_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnDeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            if (!UpdaterService.IsBackupReady)
            {
                return;
            }

            if (MessageBox.Show(
                    this,
                    LocalizationService.Get("About_DeleteBackup"),
                    LocalizationService.Get("About_DeleteBackup"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                UpdaterService.DeleteBackup();
                RefreshRollbackUi();
                (Application.Current as App)?.ShowToast(LocalizationService.Get("About_BackupDeleted"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    LocalizationService.Format("Error_Body", ex.Message),
                    LocalizationService.Get("Error_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            RecentCaptureService.Instance.Items.CollectionChanged -= RecentItems_CollectionChanged;
            RecentCaptureService.Instance.AllTimeItems.CollectionChanged -= RecentItems_CollectionChanged;
            TopmostService.Instance.StateChanged -= RefreshPinnedState;
            ClipboardService.Instance.Changed -= RefreshClipboardState;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            ThemeService.ThemeChanged -= OnThemeChanged;
            base.OnClosed(e);
        }
    }
}
