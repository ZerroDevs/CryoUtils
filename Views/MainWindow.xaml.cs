using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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
using CryoUtils.Services.Recording;
using CryoUtils.Views.Tabs;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace CryoUtils.Views
{
    public partial class MainWindow : Window
    {
        private bool _initializing = true;
        private bool _loading;
        private bool _allowClose;
        private string _currentTab = "Overview";
        private DispatcherTimer? _replayTimer;
        private CaptureTabView? _captureTab;
        private ToolsTabView? _toolsTab;
        private HotkeysTabView? _hotkeysTab;
        private GeneralTabView? _generalTab;

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
            ReplayService.Instance.StateChanged += RefreshReplayCard;
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
            WorkflowService.Instance.RunsChanged += RefreshActivityCenter;
            CaptureLibraryService.Instance.Changed += RefreshActivityCenter;
            FileActionQueueService.Instance.Changed += RefreshActivityCenter;
            UploadHistoryService.Instance.Changed += RefreshActivityCenter;
            TopmostService.Instance.StateChanged += RefreshActivityCenter;
            _replayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _replayTimer.Tick += (_, _) =>
            {
                if (IsVisible && ReplayService.Instance.IsRunning)
                {
                    RefreshReplayCard();
                }
                else
                {
                    _replayTimer?.Stop();
                }
            };
            IsVisibleChanged += (_, _) => SyncReplayTimer();
            Deactivated += (_, _) => MemoryTrimService.ScheduleTrim(1000);

            ReloadUi();
            _initializing = false;
            Loaded += MainWindow_Loaded;
            SourceInitialized += (_, _) =>
            {
                if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source &&
                    source.CompositionTarget != null)
                {
                    // Keep dashboard rendering software-only so opening/idling it never spins up GPU contexts,
                    // while leaving full-screen capture overlays hardware-accelerated.
                    source.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                }
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.Instance.ApplyWindow(this);
            RestoreWindowPosition();
            RefreshPinnedState();
            RefreshClipboardState();
            UpdateHotkeyStatus();
            RefreshReplayCard();
            SyncReplayTimer();
            RefreshActivityCenter();
            MaybeAutoCheckUpdates();
            MemoryTrimService.ScheduleTrim(1000);
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
                ChkModuleCapture.IsChecked = settings.CaptureEnabled;
                ChkModuleColor.IsChecked = settings.ColorPickerEnabled;
                ChkModulePinner.IsChecked = settings.WindowPinnerEnabled;
                ChkModuleCompressor.IsChecked = settings.CompressorEnabled;
                ChkModuleClipboard.IsChecked = settings.ClipboardHistoryEnabled;

                SelectTag(
                    CmbRecentScope,
                    string.Equals(settings.RecentCapturesScope, "AllTime", StringComparison.OrdinalIgnoreCase)
                        ? "AllTime"
                        : "Session");
            }
            finally
            {
                _loading = false;
            }

            string version = $"v{UpdaterService.CurrentVersion}";
            TxtTitleVersion.Text = version;
            TxtStatusVersion.Text = version;
            _captureTab?.LoadFromSettings();
            _toolsTab?.LoadFromSettings();
            _hotkeysTab?.RefreshRows();
            _generalTab?.LoadFromSettings();

            ApplyRecentExpanded(SettingsService.Instance.Current.RecentCapturesExpanded, reload: false);
            ApplyActivityCenterExpanded(SettingsService.Instance.Current.ActivityCenterExpanded, refresh: false);
            ApplyRecentScope();
            RefreshPinnedState();
            RefreshClipboardState();
            UpdateModuleSummary();
            UpdateHotkeyStatus();
            ApplyScreenshotOnlyUi();
            ShowTab(_currentTab);
        }

        private static void SelectTag(ComboBox comboBox, string? tag) => MainTabHelpers.SelectTag(comboBox, tag);

        private static string? SelectedTag(ComboBox comboBox) => MainTabHelpers.SelectedTag(comboBox);

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
            Keyboard.ClearFocus();
            ResetQuitConfirmState();
            SaveWindowPosition();
            Close();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            ResetQuitConfirmState();
            if (!_allowClose)
            {
                SaveWindowPosition();
            }
        }

        public void PrepareForShutdown()
        {
            _allowClose = true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox)
            {
                Keyboard.ClearFocus();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && !(Keyboard.FocusedElement is TextBox))
            {
                SaveWindowPosition();
                Close();
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

        private CaptureTabView EnsureCaptureTab()
        {
            if (_captureTab != null) return _captureTab;
            _captureTab = new CaptureTabView();
            _captureTab.SaveFolderChanged += ApplyRecentScope;
            _captureTab.HotkeyStatusChanged += UpdateHotkeyStatus;
            _captureTab.RecorderSettingsClosed += RefreshReplayCard;
            HostCapture.Content = _captureTab;
            _captureTab.LoadFromSettings();
            return _captureTab;
        }

        private ToolsTabView EnsureToolsTab()
        {
            if (_toolsTab != null) return _toolsTab;
            _toolsTab = new ToolsTabView();
            HostTools.Content = _toolsTab;
            _toolsTab.LoadFromSettings();
            return _toolsTab;
        }

        private HotkeysTabView EnsureHotkeysTab()
        {
            if (_hotkeysTab != null) return _hotkeysTab;
            _hotkeysTab = new HotkeysTabView();
            _hotkeysTab.HotkeysChanged += UpdateHotkeyStatus;
            HostHotkeys.Content = _hotkeysTab;
            _hotkeysTab.RefreshRows();
            return _hotkeysTab;
        }

        private GeneralTabView EnsureGeneralTab()
        {
            if (_generalTab != null) return _generalTab;
            _generalTab = new GeneralTabView();
            _generalTab.FullReloadRequested += ReloadUi;
            _generalTab.SavedLimitChanged += ApplyRecentScope;
            HostGeneral.Content = _generalTab;
            _generalTab.LoadFromSettings();
            return _generalTab;
        }

        public void ShowTab(string tab)
        {
            _currentTab = string.IsNullOrWhiteSpace(tab) ? "Overview" : tab;
            if (_currentTab == "About") _currentTab = "General";
            if (SettingsService.Instance.Current.ScreenshotOnlyMode && _currentTab == "Tools")
            {
                _currentTab = "Overview";
            }

            PanelOverview.Visibility = _currentTab == "Overview" ? Visibility.Visible : Visibility.Collapsed;
            HostCapture.Visibility = _currentTab == "Capture" ? Visibility.Visible : Visibility.Collapsed;
            HostTools.Visibility = _currentTab == "Tools" ? Visibility.Visible : Visibility.Collapsed;
            HostHotkeys.Visibility = _currentTab == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
            HostGeneral.Visibility = _currentTab == "General" ? Visibility.Visible : Visibility.Collapsed;

            switch (_currentTab)
            {
                case "Capture": EnsureCaptureTab(); break;
                case "Tools": EnsureToolsTab(); RefreshReplayCard(); break;
                case "Hotkeys": EnsureHotkeysTab(); break;
                case "General": EnsureGeneralTab(); break;
            }

            RadioButton? target = _currentTab switch
            {
                "Capture" => TabCapture,
                "Tools" => TabTools,
                "Hotkeys" => TabHotkeys,
                "General" => TabGeneral,
                _ => TabOverview
            };
            if (target != null && target.IsChecked != true) target.IsChecked = true;

            if (!_initializing && _currentTab == "Overview")
            {
                ApplyRecentScope();
                RefreshActivityCenter();
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
            _toolsTab?.UpdateCompressionAvailability();
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
            if (settings.ScreenshotOnlyMode)
            {
                TxtModuleCount.Text = LocalizationService.Get("Main_ScreenshotOnlyBadge");
                return;
            }

            int enabled = 0;
            if (settings.CaptureEnabled) enabled++;
            if (settings.ColorPickerEnabled) enabled++;
            if (settings.WindowPinnerEnabled) enabled++;
            if (settings.CompressorEnabled) enabled++;
            if (settings.ClipboardHistoryEnabled) enabled++;
            TxtModuleCount.Text = LocalizationService.Format("Main_ActiveModules", enabled);
        }

        private void ApplyScreenshotOnlyUi()
        {
            bool lite = SettingsService.Instance.Current.ScreenshotOnlyMode;
            UpdateModuleSummary();

            if (BtnTitleRecord != null) BtnTitleRecord.Visibility = lite ? Visibility.Collapsed : Visibility.Visible;
            if (BtnHeroRecord != null) BtnHeroRecord.Visibility = lite ? Visibility.Collapsed : Visibility.Visible;
            if (ScreenshotOnlyBanner != null) ScreenshotOnlyBanner.Visibility = lite ? Visibility.Visible : Visibility.Collapsed;
            if (TabTools != null) TabTools.Visibility = lite ? Visibility.Collapsed : Visibility.Visible;

            if (BtnQuickRecordTile != null) { BtnQuickRecordTile.IsEnabled = !lite; BtnQuickRecordTile.Opacity = lite ? 0.45 : 1.0; }
            if (BtnQuickColorTile != null) { BtnQuickColorTile.IsEnabled = !lite; BtnQuickColorTile.Opacity = lite ? 0.45 : 1.0; }
            if (CardColorModule != null) { CardColorModule.IsEnabled = !lite; CardColorModule.Opacity = lite ? 0.45 : 1.0; }
            if (CardPinnerModule != null) { CardPinnerModule.IsEnabled = !lite; CardPinnerModule.Opacity = lite ? 0.45 : 1.0; }
            if (CardCompressorModule != null) { CardCompressorModule.IsEnabled = !lite; CardCompressorModule.Opacity = lite ? 0.45 : 1.0; }
            if (CardClipboardModule != null) { CardClipboardModule.IsEnabled = !lite; CardClipboardModule.Opacity = lite ? 0.45 : 1.0; }

            if (lite && _currentTab == "Tools")
            {
                ShowTab("Overview");
            }

            _captureTab?.ApplyScreenshotOnlyUi();
            _hotkeysTab?.RefreshRows();
            _generalTab?.ApplyScreenshotOnlyUi();
        }

        private void BtnRestoreFullMode_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.Current.ScreenshotOnlyMode = false;
            SettingsService.Instance.Save();
            ReloadUi();
        }

        private void OnLanguageChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LocalizationService.Instance.ApplyWindow(this);
                _hotkeysTab?.RefreshRows();
                RefreshPinnedState();
                RefreshClipboardState();
                UpdateModuleSummary();
                UpdateHotkeyStatus();
                _toolsTab?.RefreshRecentColors();
                _captureTab?.RefreshRecorderSummary();
                _generalTab?.RefreshRollbackUi();
            }));
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _hotkeysTab?.RefreshRows();
                _toolsTab?.RefreshRecentColors();
                _toolsTab?.UpdateCompressionAvailability();
            }));
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
            _captureTab?.UpdateContestedBanner();
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

        private CancellationTokenSource? _recentReloadCts;

        private void ApplyRecentScope()
        {
            bool allTime = string.Equals(
                SettingsService.Instance.Current.RecentCapturesScope,
                "AllTime",
                StringComparison.OrdinalIgnoreCase);

            _recentReloadCts?.Cancel();
            _recentReloadCts = null;

            if (allTime)
            {
                RecentItems.ItemsSource = RecentCaptureService.Instance.AllTimeItems;
                if (SettingsService.Instance.Current.RecentCapturesExpanded)
                {
                    var cts = new CancellationTokenSource();
                    _recentReloadCts = cts;
                    _ = LoadAllTimeAsync(cts);
                }
            }
            else
            {
                RecentItems.ItemsSource = RecentCaptureService.Instance.Items;
            }

            RefreshRecentStrip();
        }

        private void BtnRecentToggle_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            ToggleRecentExpanded();
        }

        private void RecentCapturesHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Ignore clicks that originated inside the scope ComboBox or Open folder / Toggle buttons.
            if (IsInteractiveHeaderChild(e.OriginalSource as DependencyObject))
            {
                return;
            }

            ToggleRecentExpanded();
        }

        private void ToggleRecentExpanded()
        {
            bool next = !SettingsService.Instance.Current.RecentCapturesExpanded;
            SettingsService.Instance.Current.RecentCapturesExpanded = next;
            SettingsService.Instance.SaveWithoutNotification();
            ApplyRecentExpanded(next, reload: true);
        }

        private void ApplyRecentExpanded(bool expanded, bool reload)
        {
            if (RecentCapturesBody != null)
            {
                RecentCapturesBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }
            if (PathRecentChevron != null)
            {
                PathRecentChevron.Data = expanded ? IconData.ChevronDown : IconData.ChevronRight;
            }
            if (BtnRecentToggle != null)
            {
                BtnRecentToggle.ToolTip = LocalizationService.Get(expanded ? "Common_Collapse" : "Common_Expand");
            }
            if (expanded && reload)
            {
                ApplyRecentScope();
            }
            else if (!expanded)
            {
                _recentReloadCts?.Cancel();
                _recentReloadCts = null;
            }
        }

        private static bool IsInteractiveHeaderChild(DependencyObject? source)
        {
            var current = source;
            while (current != null)
            {
                if (current is ButtonBase or ComboBox or ComboBoxItem)
                {
                    return true;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private async Task LoadAllTimeAsync(CancellationTokenSource cts)
        {
            try
            {
                await RecentCaptureService.Instance.ReloadAllTimeAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Format("Error_Body", ex.Message));
            }
            finally
            {
                if (ReferenceEquals(_recentReloadCts, cts))
                {
                    _recentReloadCts = null;
                    RefreshRecentStrip();
                    MemoryTrimService.ScheduleTrim(600);
                }
                cts.Dispose();
            }
        }

        private void RecentItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RefreshRecentStrip));
        }

        private void RefreshRecentStrip()
        {
            int count = (RecentItems.ItemsSource as System.Collections.ICollection)?.Count ?? 0;
            if (TxtRecentCount != null)
            {
                TxtRecentCount.Text = count.ToString(CultureInfo.InvariantCulture);
            }
            bool hasItems = count > 0;
            RecentEmpty.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            RecentScroll.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OpenRecording_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not RecentCapture capture ||
                string.IsNullOrEmpty(capture.Path) || !File.Exists(capture.Path))
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_FolderMissing"));
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(capture.Path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Error_Body", ex.Message));
            }
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

            EnsureToolsTab().LoadCompressionSource(path);
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
                foreach (var item in RecentCaptureService.Instance.Items.Concat(
                             RecentCaptureService.Instance.AllTimeItems))
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
        private void RefreshPinnedState()
        {
            _toolsTab?.RefreshPinnedState();
            var topmost = TopmostService.Instance;
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

        private void BtnUnpin_Click(object sender, RoutedEventArgs e) => TopmostService.Instance.Unpin();

        private void RefreshClipboardState() => _toolsTab?.RefreshClipboardState();

        private void BtnHeroCapture_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.StartCapture();
        private void BtnRecordNow_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.StartRecording();

        private void OnSettingsChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnSettingsChanged));
                return;
            }
            if (_loading) return;

            _captureTab?.RefreshRecorderSummary();
            RefreshReplayCard();
            RefreshRecentStrip();
            ApplyScreenshotOnlyUi();
        }

        private void SyncReplayTimer()
        {
            if (_replayTimer == null) return;
            if (IsVisible && ReplayService.Instance.IsRunning && _toolsTab != null)
            {
                if (!_replayTimer.IsEnabled) _replayTimer.Start();
            }
            else if (_replayTimer.IsEnabled)
            {
                _replayTimer.Stop();
            }
        }

        private void RefreshReplayCard()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshReplayCard));
                return;
            }

            _toolsTab?.RefreshReplayCard();
            SyncReplayTimer();
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

        private void BtnActivityRefresh_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            RefreshActivityCenter();
        }

        private void BtnActivityToggle_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            ToggleActivityCenterExpanded();
        }

        private void ActivityCenterHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractiveHeaderChild(e.OriginalSource as DependencyObject))
            {
                return;
            }

            ToggleActivityCenterExpanded();
        }

        private void ToggleActivityCenterExpanded()
        {
            bool next = !SettingsService.Instance.Current.ActivityCenterExpanded;
            SettingsService.Instance.Current.ActivityCenterExpanded = next;
            SettingsService.Instance.SaveWithoutNotification();
            ApplyActivityCenterExpanded(next, refresh: true);
        }

        private void ApplyActivityCenterExpanded(bool expanded, bool refresh)
        {
            if (ActivityCenterBody != null)
            {
                ActivityCenterBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }
            if (BtnActivityRefresh != null)
            {
                BtnActivityRefresh.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }
            if (PathActivityChevron != null)
            {
                PathActivityChevron.Data = expanded ? IconData.ChevronDown : IconData.ChevronRight;
            }
            if (BtnActivityToggle != null)
            {
                BtnActivityToggle.ToolTip = LocalizationService.Get(expanded ? "Common_Collapse" : "Common_Expand");
            }

            if (expanded && refresh)
            {
                RefreshActivityCenter();
            }
            else if (!expanded && ActivitySectionsPanel != null)
            {
                ActivitySectionsPanel.Children.Clear();
            }
        }

        private void BtnActivityOcr_Click(object sender, RoutedEventArgs e) =>
            App.CurrentApp?.OpenOcrFile();

        private void BtnActivityPrivacy_Click(object sender, RoutedEventArgs e) =>
            App.CurrentApp?.OpenPrivacy();

        private void BtnLiveDraw_Click(object sender, RoutedEventArgs e) =>
            App.CurrentApp?.ToggleLiveDraw();

        private void BtnScreenRuler_Click(object sender, RoutedEventArgs e) =>
            App.CurrentApp?.OpenScreenRuler();

        private void BtnFileShelf_Click(object sender, RoutedEventArgs e) =>
            App.CurrentApp?.OpenFileShelf();

        private void BtnImageEditor_Click(object sender, RoutedEventArgs e) =>
            App.CurrentApp?.OpenImageEditor();

        private void BtnDeveloperToolbox_Click(object sender, RoutedEventArgs e) =>
            App.CurrentApp?.OpenDeveloperToolbox();

        private void BtnPrivacy_Click(object sender, RoutedEventArgs e) =>
            App.CurrentApp?.OpenPrivacy();

        private void RefreshActivityCenter()
        {
            if (ActivitySectionsPanel == null)
            {
                return;
            }
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshActivityCenter));
                return;
            }

            if (!SettingsService.Instance.Current.ActivityCenterExpanded)
            {
                ActivitySectionsPanel.Children.Clear();
                return;
            }

            ActivitySectionsPanel.Children.Clear();

            var uploads = UploadHistoryService.Instance.Records
                .Take(2)
                .Select(record => $"{record.FileName} · {record.MetaDisplay}")
                .ToList();
            AddActivitySection("Workbench_RecentUploads", uploads, "Workbench_NoUploads");

            var runs = WorkflowService.Instance.Runs
                .Take(2)
                .Select(run => run.DisplayLine)
                .ToList();
            AddActivitySection("Workbench_WorkflowRuns", runs, "Workbench_NoRuns");

            var jobs = FileActionQueueService.Instance.Jobs
                .Where(job => job.Status is FileActionStatus.Pending or FileActionStatus.Running)
                .Take(2)
                .Select(job => job.DisplayLine)
                .ToList();
            AddActivitySection("Workbench_FileJobs", jobs, "Workbench_NoJobs");

            var pins = TopmostService.Instance.PinnedWindows
                .Take(2)
                .Select(pin => $"{pin.Title} · {pin.ProcessName} · {pin.Opacity}%")
                .ToList();
            AddActivitySection("Workbench_Pinned", pins, "Workbench_NoPins");

            string folder = SettingsService.Instance.EffectiveSaveFolder;
            long captureBytes = FolderBytes(folder, null);
            int captureCount = FolderFileCount(folder, null);
            long uploadBytes = FolderBytes(UploadHistoryService.Instance.LocalFolder, null);
            long replayBytes = FolderBytes(
                IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CryoUtils",
                    "ReplayBuffer"),
                "*.mp4");
            AddActivitySection(
                "Workbench_Storage",
                new[]
                {
                    $"{LocalizationService.Format("Workbench_StorageCaptures", ImageIO.FormatBytes(captureBytes), captureCount)} · {LocalizationService.Format("Workbench_StorageUploads", ImageIO.FormatBytes(uploadBytes))}",
                    $"{LocalizationService.Format("Workbench_StorageReplay", ImageIO.FormatBytes(replayBytes))} · {LocalizationService.Format("Workbench_Retention", Math.Clamp(SettingsService.Instance.Current.SavedCapturesLimit, 0, 10000), captureCount)}"
                },
                "Workbench_StorageUnavailable");

            AddQuickActionsSection();

            double cardWidth = ActivitySectionsPanel.Children.Count <= 2 ? 320 : 230;
            foreach (var child in ActivitySectionsPanel.Children)
            {
                if (child is Border card)
                {
                    card.Width = cardWidth;
                }
            }
        }

        private void AddActivitySection(string titleKey, IReadOnlyList<string> lines, string emptyKey)
        {
            if (lines.Count == 0 && !string.Equals(titleKey, "Workbench_Storage", StringComparison.Ordinal))
            {
                return;
            }

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get(titleKey),
                Foreground = ThemeService.GetBrush("TextPrimaryBrush"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold
            });

            if (lines.Count == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = LocalizationService.Get(emptyKey),
                    Style = (Style)FindResource("MutedText"),
                    FontSize = 10.5,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            else
            {
                foreach (string line in lines)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = line,
                        Foreground = ThemeService.GetBrush("TextSecondaryBrush"),
                        FontSize = 10.5,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
            }

            var border = new Border
            {
                Style = (Style)FindResource("InsetBorder"),
                Width = 230,
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(8, 6, 8, 6),
                Child = stack
            };
            ActivitySectionsPanel.Children.Add(border);
        }

        private void AddQuickActionsSection()
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("Workbench_QuickActions"),
                Foreground = ThemeService.GetBrush("TextPrimaryBrush"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold
            });
            var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            wrap.Children.Add(ActivityButton("Main_CaptureNow", BtnQuickCapture_Click));
            wrap.Children.Add(ActivityButton("Workbench_QuickOcr", BtnActivityOcr_Click));
            wrap.Children.Add(ActivityButton("Workbench_QuickClipboard", BtnQuickClipboard_Click));
            wrap.Children.Add(ActivityButton("Workbench_Privacy", BtnActivityPrivacy_Click));
            stack.Children.Add(wrap);

            var border = new Border
            {
                Style = (Style)FindResource("InsetBorder"),
                Width = 230,
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(8, 6, 8, 6),
                Child = stack
            };
            ActivitySectionsPanel.Children.Add(border);
        }

        private Button ActivityButton(string contentKey, RoutedEventHandler handler)
        {
            var button = new Button
            {
                Content = LocalizationService.Get(contentKey),
                Style = (Style)FindResource("SecondaryButton"),
                Margin = new Thickness(0, 0, 4, 2),
                Padding = new Thickness(7, 1, 7, 1),
                Height = 22,
                FontSize = 10.5
            };
            button.Click += handler;
            return button;
        }

        private static long FolderBytes(string folder, string? pattern)
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    return 0;
                }
                return Directory.EnumerateFiles(folder, pattern ?? "*", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Sum(info => info.Length);
            }
            catch
            {
                return 0;
            }
        }

        private static int FolderFileCount(string folder, string? pattern)
        {
            try
            {
                return Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder, pattern ?? "*", SearchOption.TopDirectoryOnly).Count()
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private void BtnQrScanScreen_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.StartCapture();

        private async void MaybeAutoCheckUpdates()
        {
            var settings = SettingsService.Instance.Current;
            if (!settings.AutomaticUpdateChecks || settings.ScreenshotOnlyMode)
            {
                return;
            }

            DateTime? last = settings.LastUpdateCheckUtc;
            if (last.HasValue && DateTime.UtcNow - last.Value < TimeSpan.FromHours(24))
            {
                return;
            }

            await Task.Delay(1200);
            try
            {
                var info = await UpdaterService.CheckAsync();
                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                if (info != null)
                {
                    settings.LastUpdateOutcome = "available";
                    settings.LastUpdateVersion = info.Version.ToString();
                    App.CurrentApp?.ShowToast(LocalizationService.Format("About_UpdateAvailable", info.Version));
                }
                else
                {
                    settings.LastUpdateOutcome = "current";
                    settings.LastUpdateVersion = UpdaterService.CurrentVersion;
                }
                SettingsService.Instance.SaveWithoutNotification();
            }
            catch
            {
                settings.LastUpdateOutcome = "failed";
                SettingsService.Instance.SaveWithoutNotification();
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
            ReplayService.Instance.StateChanged -= RefreshReplayCard;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            WorkflowService.Instance.RunsChanged -= RefreshActivityCenter;
            CaptureLibraryService.Instance.Changed -= RefreshActivityCenter;
            FileActionQueueService.Instance.Changed -= RefreshActivityCenter;
            UploadHistoryService.Instance.Changed -= RefreshActivityCenter;
            TopmostService.Instance.StateChanged -= RefreshActivityCenter;
            _recentReloadCts?.Cancel();
            _recentReloadCts = null;
            _replayTimer?.Stop();
            _replayTimer = null;
            _quitConfirmTimer?.Stop();
            _quitConfirmTimer = null;
            _deleteResetTimer?.Stop();
            _deleteResetTimer = null;
            if (RecentItems != null) RecentItems.ItemsSource = null;
            ActivitySectionsPanel?.Children.Clear();
            _toolsTab?.ReleaseResources();
            HostCapture.Content = null;
            HostTools.Content = null;
            HostHotkeys.Content = null;
            HostGeneral.Content = null;
            _captureTab = null;
            _toolsTab = null;
            _hotkeysTab = null;
            _generalTab = null;
            base.OnClosed(e);
        }
    }
}
