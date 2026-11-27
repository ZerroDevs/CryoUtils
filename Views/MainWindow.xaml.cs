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
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CryoUtils.Models;
using CryoUtils.Services;
using CryoUtils.Services.Recording;
using CryoUtils.Views.Tabs;
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
        private WorkbenchTabView? _workbenchTab;
        private HotkeysTabView? _hotkeysTab;
        private GeneralTabView? _generalTab;
        private Point _recentDragStart;
        private RecentCapture? _recentDragCandidate;
        private RecentCapture? _lastRecentSelectAnchor;
        private bool _suppressRecentClick;
        private bool _suppressSearchTextChanged;
        private CancellationTokenSource? _recentReloadCts;
        private DispatcherTimer? _quitConfirmTimer;
        private DispatcherTimer? _inactiveTabTimer;
        private bool _isQuitConfirmPending;
        private DispatcherTimer? _deleteResetTimer;
        private bool _isDeleteAllConfirmPending;
        private DispatcherTimer? _deleteAllConfirmTimer;
        private bool _windowResizedByUser;

        public static readonly DependencyProperty IsRecentSelectModeProperty =
            DependencyProperty.Register(nameof(IsRecentSelectMode), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        public bool IsRecentSelectMode
        {
            get => (bool)GetValue(IsRecentSelectModeProperty);
            set => SetValue(IsRecentSelectModeProperty, value);
        }

        public MainWindow()
        {
            _initializing = true;
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.Manual;
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
            Activated += (_, _) => MemoryTrimService.PauseForInteraction(TimeSpan.FromMilliseconds(1500));
            Deactivated += (_, _) => MemoryTrimService.ScheduleTrim(MemoryTrimService.DefaultDelayMs, skipIfRecentlyTrimmed: true);

            ReloadUi();
            PositionAboveTray();
            _initializing = false;
            Loaded += MainWindow_Loaded;
            SourceInitialized += (_, _) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                NativeMethods.DisableWindowTransitions(hwnd);
                if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source &&
                    source.CompositionTarget != null)
                {
                    source.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                }
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.Instance.ApplyWindow(this);
            PositionAboveTray();
            RefreshPinnedState();
            RefreshClipboardState();
            UpdateHotkeyStatus();
            RefreshReplayCard();
            SyncReplayTimer();
            RefreshActivityCenter();
            MaybeAutoCheckUpdates();
            MemoryTrimService.ScheduleTrim(1000);
        }

        public void PositionAboveTray()
        {
            var settings = SettingsService.Instance.Current;
            var (monitorBounds, workArea) = WindowPlacementHelper.GetMonitorAndWorkAreaForTrayDips(this);

            double maxWorkW = Math.Max(MinWidth, workArea.Width - 24);
            double maxWorkH = Math.Max(MinHeight, workArea.Height - 24);
            const double defaultWidth = 480;
            if (settings.WindowSizeCustomized && settings.WindowWidth is double savedWidth && savedWidth >= MinWidth)
            {
                Width = Math.Clamp(savedWidth, MinWidth, maxWorkW);
            }
            else
            {
                Width = Math.Clamp(defaultWidth, MinWidth, maxWorkW);
            }

            double defaultHeight = settings.ScreenshotOnlyMode ? 490 : 565;
            if (settings.WindowSizeCustomized && settings.WindowHeight is double savedHeight && savedHeight >= MinHeight)
            {
                Height = Math.Clamp(savedHeight, MinHeight, maxWorkH);
            }
            else
            {
                Height = Math.Clamp(defaultHeight, MinHeight, maxWorkH);
            }

            if (!settings.AnchorToTray &&
                settings.WindowLeft is double savedLeft &&
                settings.WindowTop is double savedTop)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                Rect savedWorkArea = WindowPlacementHelper.GetWorkAreaForSavedPositionDips(
                    savedLeft,
                    savedTop,
                    Width,
                    Height,
                    dpi.DpiScaleX,
                    dpi.DpiScaleY);
                var (clampedLeft, clampedTop) = WindowPlacementHelper.ClampToWorkArea(
                    savedWorkArea,
                    savedLeft,
                    savedTop,
                    Width,
                    Height,
                    margin: 8);
                Left = clampedLeft;
                Top = clampedTop;
                return;
            }

            var (trayLeft, trayTop) = WindowPlacementHelper.ComputeTrayAnchoredPosition(
                monitorBounds,
                workArea,
                Width,
                Height,
                margin: 12);
            Left = trayLeft;
            Top = trayTop;
        }

        private void SaveWindowPosition()
        {
            var settings = SettingsService.Instance.Current;
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            if (_windowResizedByUser && Width >= MinWidth && Height >= MinHeight)
            {
                settings.WindowWidth = Math.Round(Width);
                settings.WindowHeight = Math.Round(Height);
                settings.WindowSizeCustomized = true;
            }
            SettingsService.Instance.SaveWithoutNotification();
        }

        private void ResizeCornerThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            Rect workArea = WindowPlacementHelper.GetMonitorWorkAreaDips(this);
            double maxW = Math.Max(MinWidth, workArea.Right - Left - 8);
            double maxH = Math.Max(MinHeight, workArea.Bottom - Top - 8);
            Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, maxW);
            Height = Math.Clamp(Height + e.VerticalChange, MinHeight, maxH);
            _windowResizedByUser = true;
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
                TglSuiteMode.IsChecked = !settings.ScreenshotOnlyMode;

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
            RefreshHeroProfileLabel();
            ApplyScreenshotOnlyUi();
            ShowTab(_currentTab);
        }

        private static void SelectTag(ComboBox comboBox, string? tag) => MainTabHelpers.SelectTag(comboBox, tag);

        private static string? SelectedTag(ComboBox comboBox) => MainTabHelpers.SelectedTag(comboBox);

        // ============================ Window chrome ============================
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractiveHeaderChild(e.OriginalSource as DependencyObject))
            {
                return;
            }

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

            _quitConfirmTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            _quitConfirmTimer.Tick -= QuitConfirmTimer_Tick;
            _quitConfirmTimer.Tick += QuitConfirmTimer_Tick;
            _quitConfirmTimer.Stop();
            _quitConfirmTimer.Start();
        }

        private void QuitConfirmTimer_Tick(object? sender, EventArgs e) => ResetQuitConfirmState();

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
            if (SearchPopup != null)
            {
                SearchPopup.IsOpen = false;
            }
            if (!_allowClose)
            {
                SaveWindowPosition();
            }
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (SearchPopup != null)
            {
                SearchPopup.IsOpen = false;
            }
        }

        public void PrepareForShutdown() => _allowClose = true;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                TxtQuickSearch?.Focus();
                TxtQuickSearch?.SelectAll();
                OpenSearchPopup(TxtQuickSearch?.Text);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (SearchPopup?.IsOpen == true)
                {
                    SearchPopup.IsOpen = false;
                    Keyboard.ClearFocus();
                    e.Handled = true;
                    return;
                }
                if (Keyboard.FocusedElement is TextBox)
                {
                    Keyboard.ClearFocus();
                    e.Handled = true;
                    return;
                }
                if (IsRecentSelectMode && TglRecentSelect != null)
                {
                    TglRecentSelect.IsChecked = false;
                    e.Handled = true;
                    return;
                }

                SaveWindowPosition();
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox tb && !ReferenceEquals(tb, TxtQuickSearch))
            {
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        // ============================ CryoTray Command Search ============================
        private void TxtQuickSearch_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            OpenSearchPopup(TxtQuickSearch.Text);
        }

        private void TxtQuickSearch_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (SearchPopup != null &&
                !IsKeyboardFocusWithin &&
                !(SearchPopup.Child?.IsMouseOver ?? false))
            {
                SearchPopup.IsOpen = false;
            }
        }

        private void TxtQuickSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = TxtQuickSearch.Text ?? "";
            TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            BtnClearSearch.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;
            if (_suppressSearchTextChanged)
            {
                return;
            }

            OpenSearchPopup(query);
        }

        private void OpenSearchPopup(string? query)
        {
            if (SearchPopup == null)
            {
                return;
            }

            bool lite = SettingsService.Instance.Current.ScreenshotOnlyMode;
            var hits = DashboardSearchService.Search(query, lite, limit: 10).ToList();
            if (!lite && !string.IsNullOrWhiteSpace(query) && query.Trim().Length >= 2)
            {
                string cat = LocalizationService.Get("Search_CategoryCapture");
                foreach (var cap in CaptureLibraryService.Instance.SearchByText(query, limit: 3))
                {
                    hits.Add(new DashboardSearchHit
                    {
                        Id = "lib-" + cap.Path,
                        Title = cap.Title,
                        Category = cat,
                        ActionId = "open_capture:" + cap.Path
                    });
                }
            }
            LstSearchResults.ItemsSource = hits;
            bool hasHits = hits.Count > 0;
            LstSearchResults.Visibility = hasHits ? Visibility.Visible : Visibility.Collapsed;
            PnlSearchEmpty.Visibility = hasHits ? Visibility.Collapsed : Visibility.Visible;
            TxtSearchSummary.Visibility = Visibility.Visible;
            TxtSearchSummary.Text = string.IsNullOrWhiteSpace(query)
                ? LocalizationService.Get("Search_Suggested")
                : LocalizationService.Format("Search_Matches", hits.Count);

            if (hasHits)
            {
                LstSearchResults.SelectedIndex = 0;
            }

            SearchPopup.IsOpen = true;
        }

        private void TxtQuickSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                if (SearchPopup?.IsOpen != true)
                {
                    OpenSearchPopup(TxtQuickSearch.Text);
                }
                else if (LstSearchResults.Items.Count > 0)
                {
                    LstSearchResults.SelectedIndex = Math.Min(LstSearchResults.SelectedIndex + 1, LstSearchResults.Items.Count - 1);
                    LstSearchResults.ScrollIntoView(LstSearchResults.SelectedItem);
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up && SearchPopup?.IsOpen == true && LstSearchResults.Items.Count > 0)
            {
                LstSearchResults.SelectedIndex = Math.Max(LstSearchResults.SelectedIndex - 1, 0);
                LstSearchResults.ScrollIntoView(LstSearchResults.SelectedItem);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (LstSearchResults.SelectedItem is DashboardSearchHit hit)
                {
                    ExecuteSearchHit(hit);
                    e.Handled = true;
                }
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (SearchPopup != null)
                {
                    SearchPopup.IsOpen = false;
                }
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void LstSearchResults_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((ItemsControl.ContainerFromElement(LstSearchResults, e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext is DashboardSearchHit hit)
            {
                ExecuteSearchHit(hit);
            }
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            _suppressSearchTextChanged = true;
            TxtQuickSearch.Text = "";
            _suppressSearchTextChanged = false;
            TxtSearchPlaceholder.Visibility = Visibility.Visible;
            BtnClearSearch.Visibility = Visibility.Collapsed;
            OpenSearchPopup("");
            TxtQuickSearch.Focus();
        }

        private void ExecuteSearchHit(DashboardSearchHit hit)
        {
            if (SearchPopup != null)
            {
                SearchPopup.IsOpen = false;
            }
            _suppressSearchTextChanged = true;
            TxtQuickSearch.Text = "";
            _suppressSearchTextChanged = false;
            TxtSearchPlaceholder.Visibility = Visibility.Visible;
            BtnClearSearch.Visibility = Visibility.Collapsed;
            Keyboard.ClearFocus();
            MemoryTrimService.PauseForInteraction();

            if (hit.ActionId != null && hit.ActionId.StartsWith("open_capture:", StringComparison.Ordinal))
            {
                string capturePath = hit.ActionId.Substring("open_capture:".Length);
                CaptureLibraryService.Instance.OpenFile(capturePath);
                return;
            }

            switch (hit.ActionId)
            {
                case "capture": App.CurrentApp?.StartCapture(); return;
                case "snap": App.CurrentApp?.SnapActiveWindow(waitForFocusRestore: true); return;
                case "record": App.CurrentApp?.StartRecording(); return;
                case "color": App.CurrentApp?.OpenColorPicker(); return;
                case "pin": App.CurrentApp?.TogglePinnerFromDashboard(); return;
                case "clipboard": App.CurrentApp?.OpenClipboardHistory(); return;
                case "livedraw": App.CurrentApp?.ToggleLiveDraw(); return;
                case "ruler": App.CurrentApp?.OpenScreenRuler(); return;
                case "shelf": App.CurrentApp?.OpenFileShelf(); return;
                case "editor": App.CurrentApp?.OpenImageEditor(); return;
                case "dev": App.CurrentApp?.OpenDeveloperToolbox(); return;
            }

            ShowTab(hit.Tab, hit.SubTab);
        }

        // ============================ Tabs ============================
        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (_initializing || sender is not RadioButton radio || radio.Tag is not string tag)
            {
                return;
            }

            MemoryTrimService.PauseForInteraction();
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

        private WorkbenchTabView EnsureWorkbenchTab()
        {
            if (_workbenchTab != null) return _workbenchTab;
            _workbenchTab = new WorkbenchTabView();
            HostWorkspace.Content = _workbenchTab;
            return _workbenchTab;
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
            _generalTab.AnchorToTrayChanged += PositionAboveTray;
            HostGeneral.Content = _generalTab;
            _generalTab.LoadFromSettings();
            return _generalTab;
        }

        public void ShowTab(string tab, string? subTab = null)
        {
            _currentTab = string.IsNullOrWhiteSpace(tab) ? "Overview" : tab;
            if (_currentTab is "About" or "Settings") _currentTab = "General";
            if (SettingsService.Instance.Current.ScreenshotOnlyMode && _currentTab is "Tools" or "Workspace")
            {
                _currentTab = "Overview";
            }

            bool isWorkspace = _currentTab == "Workspace";
            MainContentScroll.Visibility = isWorkspace ? Visibility.Collapsed : Visibility.Visible;
            HostWorkspace.Visibility = isWorkspace ? Visibility.Visible : Visibility.Collapsed;

            PanelOverview.Visibility = _currentTab == "Overview" ? Visibility.Visible : Visibility.Collapsed;
            HostCapture.Visibility = _currentTab == "Capture" ? Visibility.Visible : Visibility.Collapsed;
            HostTools.Visibility = _currentTab == "Tools" ? Visibility.Visible : Visibility.Collapsed;
            HostHotkeys.Visibility = _currentTab == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
            HostGeneral.Visibility = _currentTab == "General" ? Visibility.Visible : Visibility.Collapsed;

            switch (_currentTab)
            {
                case "Capture": EnsureCaptureTab(); break;
                case "Tools": EnsureToolsTab(); RefreshReplayCard(); break;
                case "Workspace":
                    var wb = EnsureWorkbenchTab();
                    wb.SelectSubTab(subTab);
                    break;
                case "Hotkeys": EnsureHotkeysTab(); break;
                case "General": EnsureGeneralTab(); break;
            }

            RadioButton? target = _currentTab switch
            {
                "Capture" => TabCapture,
                "Tools" => TabTools,
                "Workspace" => TabWorkspace,
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

            SyncInactiveHeavyTabTimer();

            if (!isWorkspace)
            {
                MainContentScroll?.ScrollToTop();
            }
        }

        private void SyncInactiveHeavyTabTimer()
        {
            if (_currentTab is "Tools" or "Workspace")
            {
                _inactiveTabTimer?.Stop();
                return;
            }

            if (_toolsTab == null && _workbenchTab == null)
            {
                _inactiveTabTimer?.Stop();
                return;
            }

            if (_inactiveTabTimer == null)
            {
                _inactiveTabTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(30)
                };
                _inactiveTabTimer.Tick += (_, _) =>
                {
                    _inactiveTabTimer?.Stop();
                    ReleaseInactiveHeavyTabs();
                };
            }

            _inactiveTabTimer.Stop();
            _inactiveTabTimer.Start();
        }

        private void ReleaseInactiveHeavyTabs()
        {
            if (_currentTab is "Tools" or "Workspace")
            {
                return;
            }

            if (_toolsTab != null)
            {
                _toolsTab.ReleaseResources();
                HostTools.Content = null;
                _toolsTab = null;
            }

            if (_workbenchTab != null)
            {
                _workbenchTab.ReleaseResources();
                HostWorkspace.Content = null;
                _workbenchTab = null;
            }
        }

        public void PlayTrayRevealAnimation(bool animate)
        {
            if (RootChromeBorder == null || RootRevealTransform == null)
            {
                return;
            }

            if (!animate)
            {
                RootChromeBorder.BeginAnimation(OpacityProperty, null);
                RootChromeBorder.Opacity = 1.0;
                RootRevealTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                RootRevealTransform.Y = 0;
                return;
            }

            var duration = new Duration(TimeSpan.FromMilliseconds(90));
            var ease = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            };

            var fade = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, duration)
            {
                EasingFunction = ease,
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };
            RootChromeBorder.Opacity = 1.0;
            RootChromeBorder.BeginAnimation(OpacityProperty, fade);

            var slide = new System.Windows.Media.Animation.DoubleAnimation(8.0, 0.0, duration)
            {
                EasingFunction = ease,
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };
            RootRevealTransform.Y = 0.0;
            RootRevealTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
        }

        private void MainContentScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (MainContentScroll == null || MainContentScroll.Visibility != Visibility.Visible || e.Delta == 0)
            {
                return;
            }

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

        // ============================ Mode & Module toggles ============================
        private void TglSuiteMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || _initializing)
            {
                return;
            }

            MemoryTrimService.PauseForInteraction();
            bool fullSuite = TglSuiteMode.IsChecked == true;
            SetScreenshotOnlyMode(!fullSuite);
        }

        private void SetScreenshotOnlyMode(bool lite)
        {
            var settings = SettingsService.Instance.Current;
            if (settings.ScreenshotOnlyMode == lite)
            {
                ApplyScreenshotOnlyUi();
                return;
            }

            settings.ScreenshotOnlyMode = lite;
            SettingsService.Instance.Save();
            ReloadUi();
            PositionAboveTray();
        }

        private void BtnModeMenu_Click(object sender, RoutedEventArgs e)
        {
            if (ModeContextMenu == null) return;
            ModeContextMenu.PlacementTarget = BtnModeMenu;
            ModeContextMenu.Placement = PlacementMode.Bottom;
            ModeContextMenu.IsOpen = true;
        }

        private void ModeContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.Instance.Current;
            bool lite = settings.ScreenshotOnlyMode;
            MenuModeFull.IsChecked = !lite;
            MenuModeLite.IsChecked = lite;
            bool trayCapture = !lite && string.Equals(settings.TrayLeftClickAction, "Capture", StringComparison.OrdinalIgnoreCase);
            MenuTrayCapture.IsChecked = trayCapture;
            MenuTrayDashboard.IsChecked = !trayCapture;
            MenuTrayCapture.IsEnabled = !lite;
            MenuTrayDashboard.IsEnabled = !lite;
        }

        private void MenuModeFull_Click(object sender, RoutedEventArgs e) => SetScreenshotOnlyMode(false);
        private void MenuModeLite_Click(object sender, RoutedEventArgs e) => SetScreenshotOnlyMode(true);

        private void MenuTrayDashboard_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.Current.TrayLeftClickAction = "Dashboard";
            SettingsService.Instance.Save();
            _generalTab?.LoadFromSettings();
            UpdateApertureHeroState();
        }

        private void MenuTrayCapture_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.Instance.Current.TrayLeftClickAction = "Capture";
            SettingsService.Instance.Save();
            _generalTab?.LoadFromSettings();
            UpdateApertureHeroState();
        }

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

        private int CountActiveModules(AppSettings settings)
        {
            int enabled = 0;
            if (settings.CaptureEnabled) enabled++;
            if (settings.ColorPickerEnabled) enabled++;
            if (settings.WindowPinnerEnabled) enabled++;
            if (settings.CompressorEnabled) enabled++;
            if (settings.ClipboardHistoryEnabled) enabled++;
            return enabled;
        }

        private void UpdateModuleSummary()
        {
            var settings = SettingsService.Instance.Current;
            if (settings.ScreenshotOnlyMode)
            {
                TxtModuleCount.Text = LocalizationService.Get("Main_ScreenshotOnlyBadge");
            }
            else
            {
                TxtModuleCount.Text = LocalizationService.Format("Main_ActiveModules", CountActiveModules(settings));
            }
            UpdateApertureHeroState();
        }

        private void UpdateApertureHeroState()
        {
            if (ApertureTile == null) return;
            var settings = SettingsService.Instance.Current;
            bool lite = settings.ScreenshotOnlyMode;

            if (lite)
            {
                ApertureTile.BorderBrush = new SolidColorBrush(Color.FromRgb(230, 138, 87));
                if (ApertureHaloRing != null)
                {
                    ApertureHaloRing.BorderBrush = new SolidColorBrush(Color.FromRgb(230, 138, 87));
                    ApertureHaloRing.Opacity = 0.28;
                }
                ApertureStop1.Color = Color.FromRgb(38, 22, 14);
                ApertureStop2.Color = Color.FromRgb(62, 34, 21);
                ApertureStop3.Color = Color.FromRgb(28, 16, 11);
                ApertureCornerTraces.Stroke = new SolidColorBrush(Color.FromRgb(245, 166, 112));
                ApertureReticle.Stroke = new SolidColorBrush(Color.FromRgb(230, 138, 87));
                ApertureOuterRing.Stroke = new SolidColorBrush(Color.FromRgb(255, 220, 194));
                ApertureCoreDot.Fill = new SolidColorBrush(Color.FromRgb(244, 154, 96));
                TxtModeSummary.Text = LocalizationService.Get("Overview_ModeLiteSummary");
            }
            else
            {
                ApertureTile.BorderBrush = new SolidColorBrush(Color.FromRgb(53, 215, 255));
                if (ApertureHaloRing != null)
                {
                    ApertureHaloRing.BorderBrush = new SolidColorBrush(Color.FromRgb(53, 215, 255));
                    ApertureHaloRing.Opacity = 0.34;
                }
                ApertureStop1.Color = Color.FromRgb(7, 26, 43);
                ApertureStop2.Color = Color.FromRgb(11, 42, 64);
                ApertureStop3.Color = Color.FromRgb(7, 23, 34);
                ApertureCornerTraces.Stroke = new SolidColorBrush(Color.FromRgb(85, 230, 255));
                ApertureReticle.Stroke = new SolidColorBrush(Color.FromRgb(53, 215, 255));
                ApertureOuterRing.Stroke = new SolidColorBrush(Color.FromRgb(210, 250, 255));
                ApertureCoreDot.Fill = new SolidColorBrush(Color.FromRgb(53, 215, 255));
                TxtModeSummary.Text = LocalizationService.Format(
                    "Overview_ModeFullSummary",
                    CountActiveModules(settings));
            }
        }

        private void ApplyScreenshotOnlyUi()
        {
            bool lite = SettingsService.Instance.Current.ScreenshotOnlyMode;
            _loading = true;
            try
            {
                if (TglSuiteMode != null)
                {
                    TglSuiteMode.IsChecked = !lite;
                }
            }
            finally
            {
                _loading = false;
            }

            UpdateModuleSummary();

            if (BtnHeroRecord != null) BtnHeroRecord.Visibility = lite ? Visibility.Collapsed : Visibility.Visible;
            if (TabTools != null) TabTools.Visibility = lite ? Visibility.Collapsed : Visibility.Visible;
            if (TabWorkspace != null) TabWorkspace.Visibility = lite ? Visibility.Collapsed : Visibility.Visible;

            Visibility moduleVis = lite ? Visibility.Collapsed : Visibility.Visible;
            if (BtnModuleRightClick != null) BtnModuleRightClick.Visibility = moduleVis;
            if (RowSnapModule != null) RowSnapModule.Visibility = lite ? Visibility.Visible : Visibility.Collapsed;
            if (LiteQuickBar != null)
            {
                LiteQuickBar.Visibility = lite ? Visibility.Visible : Visibility.Collapsed;
                if (lite) RefreshLiteQuickBar();
            }
            RefreshRightClickButtons();
            if (RowColorModule != null) RowColorModule.Visibility = moduleVis;
            if (RowPinnerModule != null) RowPinnerModule.Visibility = moduleVis;
            if (RowCompressorModule != null) RowCompressorModule.Visibility = moduleVis;
            if (RowClipboardModule != null) RowClipboardModule.Visibility = moduleVis;
            if (RowQrModule != null) RowQrModule.Visibility = moduleVis;
            if (RowActivitySection != null) RowActivitySection.Visibility = moduleVis;
            if (TxtHeroTitle != null)
            {
                TxtHeroTitle.Text = LocalizationService.Get(lite ? "Main_OverviewTitleLite" : "Main_OverviewTitle");
            }
            if (TxtHeroSubtitle != null)
            {
                TxtHeroSubtitle.Text = LocalizationService.Get(lite ? "Main_OverviewSubtitleLite" : "Main_OverviewSubtitle");
            }

            if (lite && _currentTab is "Tools" or "Workspace")
            {
                ShowTab("Overview");
            }
            if (lite)
            {
                HostTools.Content = null;
                _toolsTab?.ReleaseResources();
                _toolsTab = null;

                HostWorkspace.Content = null;
                _workbenchTab?.ReleaseResources();
                _workbenchTab = null;

                RecentCaptureService.Instance.ReleaseBackgroundCache(screenshotOnly: true);
            }

            _captureTab?.ApplyScreenshotOnlyUi();
            _hotkeysTab?.RefreshRows();
            _generalTab?.ApplyScreenshotOnlyUi();
        }

        private void OnLanguageChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LocalizationService.Instance.ApplyWindow(this);
                RefreshHeroProfileLabel();
                bool lite = SettingsService.Instance.Current.ScreenshotOnlyMode;
                if (TxtHeroTitle != null)
                {
                    TxtHeroTitle.Text = LocalizationService.Get(lite ? "Main_OverviewTitleLite" : "Main_OverviewTitle");
                }
                if (TxtHeroSubtitle != null)
                {
                    TxtHeroSubtitle.Text = LocalizationService.Get(lite ? "Main_OverviewSubtitleLite" : "Main_OverviewSubtitle");
                }
                RefreshLiteQuickBar();
                RefreshRightClickButtons();
                RefreshRecentBulkBar();
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
            string captureDisplay = GlobalHotkeyService.FormatShortcutDisplay(
                settings.CaptureHotkeyModifiers,
                settings.CaptureHotkeyKey);

            if (TxtHeroKeyBig != null)
            {
                TxtHeroKeyBig.Text = captureDisplay;
                TxtHeroKeyBig.FontSize = captureDisplay.Length <= 6 ? 38 : (captureDisplay.Length <= 11 ? 26 : 19);
            }

            TxtHotkeyCapture.Text = captureDisplay;
            if (TxtHotkeySnap != null)
            {
                TxtHotkeySnap.Text = GlobalHotkeyService.FormatShortcutDisplay(
                    settings.ActiveWindowSnapHotkeyModifiers, settings.ActiveWindowSnapHotkeyKey);
            }
            TxtHotkeyColor.Text = GlobalHotkeyService.FormatShortcutDisplay(
                settings.ColorPickerHotkeyModifiers, settings.ColorPickerHotkeyKey);
            TxtHotkeyPinner.Text = GlobalHotkeyService.FormatShortcutDisplay(
                settings.WindowPinnerHotkeyModifiers, settings.WindowPinnerHotkeyKey);
            TxtHotkeyClipboard.Text = GlobalHotkeyService.FormatShortcutDisplay(
                settings.ClipboardHotkeyModifiers, settings.ClipboardHotkeyKey);
            _captureTab?.UpdateContestedBanner();
        }

        // ============================ Recent Captures ============================
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

            ClearRecentSelections();
            ResetDeleteAllConfirm();
            if (TglRecentSelect != null && TglRecentSelect.IsChecked == true)
            {
                TglRecentSelect.IsChecked = false;
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
            if (!expanded)
            {
                _recentReloadCts?.Cancel();
                _recentReloadCts = null;
            }
            ApplyRecentLayout();
        }

        private void BtnRecentLayout_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var s = SettingsService.Instance.Current;
            s.RecentCompactRows = !s.RecentCompactRows;
            SettingsService.Instance.SaveWithoutNotification();
            ApplyRecentLayout();
        }

        private void ApplyRecentLayout()
        {
            if (RecentItems == null)
            {
                return;
            }

            bool compact = SettingsService.Instance.Current.RecentCompactRows;
            RecentItems.ItemsPanel = (ItemsPanelTemplate)FindResource(compact ? "RecentRowPanel" : "RecentCardPanel");
            RecentItems.ItemTemplate = (DataTemplate)FindResource(compact ? "RecentRowTemplate" : "RecentCardTemplate");
            if (PathRecentLayout != null)
            {
                PathRecentLayout.Data = compact ? IconData.Tools : IconData.Menu;
            }
        }

        private static bool IsInteractiveHeaderChild(DependencyObject? source)
        {
            var current = source;
            while (current != null)
            {
                if (current is ButtonBase or ComboBox or ComboBoxItem or TextBox)
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

        private List<RecentCapture> GetActiveRecentList() =>
            (RecentItems?.ItemsSource as IEnumerable<RecentCapture>)?.ToList() ?? new List<RecentCapture>();

        private void ClearRecentSelections()
        {
            _lastRecentSelectAnchor = null;
            foreach (var item in RecentCaptureService.Instance.Items.Concat(RecentCaptureService.Instance.AllTimeItems))
            {
                if (item.IsSelected)
                {
                    item.IsSelected = false;
                }
            }
        }

        private void ResetDeleteAllConfirm()
        {
            _deleteAllConfirmTimer?.Stop();
            _isDeleteAllConfirmPending = false;
            if (PathRecentDeleteAll != null)
            {
                PathRecentDeleteAll.Data = IconData.Trash;
            }
            if (BtnRecentDeleteAll != null)
            {
                BtnRecentDeleteAll.ClearValue(BackgroundProperty);
                BtnRecentDeleteAll.ClearValue(BorderBrushProperty);
                BtnRecentDeleteAll.ToolTip = LocalizationService.Get("Recent_DeleteAllTip");
            }
            if (BtnRecentBulkDeleteAll != null)
            {
                BtnRecentBulkDeleteAll.Content = LocalizationService.Get("Recent_DeleteAll");
            }
        }

        private void RefreshRecentBulkBar()
        {
            var items = GetActiveRecentList();
            bool hasItems = items.Count > 0;
            bool selectMode = hasItems && IsRecentSelectMode;
            if (RecentBulkBar != null)
            {
                RecentBulkBar.Visibility = selectMode ? Visibility.Visible : Visibility.Collapsed;
            }
            if (!selectMode)
            {
                return;
            }

            int selectedCount = items.Count(item => item.IsSelected);
            bool allSelected = selectedCount == items.Count;
            if (BtnRecentSelectAll != null)
            {
                BtnRecentSelectAll.Content = LocalizationService.Get(allSelected ? "Recent_ClearSelection" : "Recent_SelectAll");
            }
            if (TxtRecentSelectedCount != null)
            {
                TxtRecentSelectedCount.Text = LocalizationService.Format("Recent_SelectedCount", selectedCount);
            }
            if (BtnRecentDeleteSelected != null)
            {
                BtnRecentDeleteSelected.Content = LocalizationService.Format("Recent_DeleteSelected", selectedCount);
                BtnRecentDeleteSelected.IsEnabled = selectedCount > 0;
            }
            if (BtnRecentBulkDeleteAll != null && !_isDeleteAllConfirmPending)
            {
                BtnRecentBulkDeleteAll.Content = LocalizationService.Get("Recent_DeleteAll");
            }
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
            if (TglRecentSelect != null)
            {
                TglRecentSelect.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
                if (!hasItems && TglRecentSelect.IsChecked == true)
                {
                    TglRecentSelect.IsChecked = false;
                }
            }
            if (BtnRecentDeleteAll != null)
            {
                BtnRecentDeleteAll.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            }
            if (!hasItems)
            {
                IsRecentSelectMode = false;
                ClearRecentSelections();
                ResetDeleteAllConfirm();
            }
            RefreshRecentBulkBar();
        }

        private void TglRecentSelect_Changed(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            IsRecentSelectMode = TglRecentSelect?.IsChecked == true;
            if (!IsRecentSelectMode)
            {
                ClearRecentSelections();
                ResetDeleteAllConfirm();
            }
            else if (SettingsService.Instance.Current.RecentCapturesExpanded == false)
            {
                SettingsService.Instance.Current.RecentCapturesExpanded = true;
                SettingsService.Instance.SaveWithoutNotification();
                ApplyRecentExpanded(true, reload: true);
            }
            RefreshRecentBulkBar();
        }

        private void RecentSelectCheck_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if ((sender as FrameworkElement)?.Tag is RecentCapture capture)
            {
                HandleRecentSelectionClick(capture, capture.IsSelected);
                return;
            }
            RefreshRecentBulkBar();
        }

        private void HandleRecentSelectionClick(RecentCapture capture, bool targetState)
        {
            var items = GetActiveRecentList();
            bool shiftHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (shiftHeld && _lastRecentSelectAnchor != null)
            {
                int start = items.IndexOf(_lastRecentSelectAnchor);
                int end = items.IndexOf(capture);
                if (start >= 0 && end >= 0)
                {
                    int lo = Math.Min(start, end);
                    int hi = Math.Max(start, end);
                    for (int i = lo; i <= hi; i++)
                    {
                        items[i].IsSelected = targetState;
                    }
                }
                else
                {
                    capture.IsSelected = targetState;
                }
            }
            else
            {
                capture.IsSelected = targetState;
            }

            _lastRecentSelectAnchor = capture;
            RefreshRecentBulkBar();
        }

        private void BtnRecentSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var items = GetActiveRecentList();
            if (items.Count == 0)
            {
                return;
            }
            bool selectAll = items.Any(item => !item.IsSelected);
            foreach (var item in items)
            {
                item.IsSelected = selectAll;
            }
            RefreshRecentBulkBar();
        }

        private void BtnRecentDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetActiveRecentList().Where(item => item.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var (deleted, failed) = RecentCaptureService.Instance.DeleteCaptures(selected);
            ResetDeleteAllConfirm();
            if (GetActiveRecentList().Count == 0 && TglRecentSelect != null)
            {
                TglRecentSelect.IsChecked = false;
            }
            RefreshRecentStrip();

            if (deleted > 0)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Format("Recent_DeletedMany", deleted));
            }
            else if (failed > 0)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_DeleteFailed"));
            }
        }

        private void BtnRecentDeleteAll_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            var items = GetActiveRecentList();
            if (items.Count == 0)
            {
                return;
            }

            if (!_isDeleteAllConfirmPending)
            {
                _isDeleteAllConfirmPending = true;
                if (PathRecentDeleteAll != null)
                {
                    PathRecentDeleteAll.Data = IconData.Check;
                }
                if (BtnRecentDeleteAll != null)
                {
                    BtnRecentDeleteAll.Background = ThemeService.GetBrush("DangerSoftBackgroundBrush");
                    BtnRecentDeleteAll.BorderBrush = ThemeService.GetBrush("DangerTextBrush");
                    BtnRecentDeleteAll.ToolTip = LocalizationService.Format("Recent_ConfirmDeleteAll", items.Count);
                }
                if (BtnRecentBulkDeleteAll != null)
                {
                    BtnRecentBulkDeleteAll.Content = LocalizationService.Format("Recent_ConfirmDeleteAll", items.Count);
                }

                _deleteAllConfirmTimer?.Stop();
                _deleteAllConfirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                _deleteAllConfirmTimer.Tick += (_, _) => ResetDeleteAllConfirm();
                _deleteAllConfirmTimer.Start();
                return;
            }

            ResetDeleteAllConfirm();
            var (deleted, failed) = RecentCaptureService.Instance.DeleteCaptures(items);
            if (TglRecentSelect != null)
            {
                TglRecentSelect.IsChecked = false;
            }
            RefreshRecentStrip();

            if (deleted > 0)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Format("Recent_DeletedMany", deleted));
            }
            else if (failed > 0)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_DeleteFailed"));
            }
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

        private void RecentThumbnail_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsRecentSelectMode)
            {
                return;
            }
            if (e.ChangedButton == MouseButton.Left &&
                (sender as FrameworkElement)?.Tag is RecentCapture capture)
            {
                _recentDragStart = e.GetPosition(this);
                _recentDragCandidate = capture;
                _suppressRecentClick = false;
            }
        }

        private void RecentThumbnail_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_recentDragCandidate == null || e.LeftButton != MouseButtonState.Pressed || sender is not DependencyObject dragSource)
            {
                return;
            }

            Point pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _recentDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _recentDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var capture = _recentDragCandidate;
            _recentDragCandidate = null;
            _suppressRecentClick = true;

            string? path = !string.IsNullOrEmpty(capture.Path) && File.Exists(capture.Path)
                ? capture.Path
                : (capture.ResolveFullImage() is BitmapSource resolved ? CaptureController.WriteTempDragPng(resolved, "CryoUtils") : null);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { path });
                DragDrop.DoDragDrop(dragSource, data, DragDropEffects.Copy);
            }
            catch
            {
            }
        }

        private void RecentThumbnail_Click(object sender, MouseButtonEventArgs e)
        {
            _recentDragCandidate = null;
            if (_suppressRecentClick)
            {
                _suppressRecentClick = false;
                return;
            }

            if ((sender as FrameworkElement)?.Tag is not RecentCapture capture)
            {
                return;
            }

            if (IsRecentSelectMode)
            {
                bool nextState = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || !capture.IsSelected;
                HandleRecentSelectionClick(capture, nextState);
                return;
            }

            OpenRecentCapture(capture);
        }

        private static void OpenRecentCapture(RecentCapture capture)
        {
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

            var bitmap = capture.ResolveFullImage();
            if (bitmap == null)
            {
                return;
            }

            CaptureController.Instance.StartForImage(bitmap, capture.Path);
        }

        private void RecentMore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not RecentCapture capture)
            {
                return;
            }

            OpenRecentContextMenu(element, capture);
        }

        private void RecentItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not RecentCapture capture)
            {
                return;
            }

            e.Handled = true;
            OpenRecentContextMenu(element, capture);
        }

        private void OpenRecentContextMenu(FrameworkElement anchor, RecentCapture capture)
        {
            var menu = new ContextMenu
            {
                Style = (Style)FindResource("CryoContextMenu"),
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom
            };

            bool hasDiskFile = !string.IsNullOrEmpty(capture.Path) && File.Exists(capture.Path);

            var openItem = new MenuItem
            {
                Header = LocalizationService.Get(capture.IsVideo ? "Recent_OpenRecording" : "Recent_OpenInEditor")
            };
            openItem.Click += (_, _) => OpenRecentCapture(capture);
            menu.Items.Add(openItem);

            var openFolderItem = new MenuItem
            {
                Header = LocalizationService.Get("Recent_OpenFolder"),
                IsEnabled = hasDiskFile
            };
            openFolderItem.Click += (_, _) => RevealCaptureInExplorer(capture.Path);
            menu.Items.Add(openFolderItem);
            menu.Items.Add(new Separator());

            var copyPathItem = new MenuItem
            {
                Header = LocalizationService.Get("Recent_CopyPath"),
                IsEnabled = hasDiskFile
            };
            copyPathItem.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(capture.Path))
                {
                    CopyTextWithToast(capture.Path);
                }
            };
            menu.Items.Add(copyPathItem);

            var copyMdItem = new MenuItem
            {
                Header = LocalizationService.Get("Recent_CopyMarkdown")
            };
            copyMdItem.Click += (_, _) =>
            {
                string? md = FormatRecentMarkdown(
                    hasDiskFile ? capture.Path : null,
                    capture.DisplayName,
                    capture.IsVideo ? null : () => TryGetCaptureBase64Uri(capture));
                if (!string.IsNullOrEmpty(md))
                {
                    CopyTextWithToast(md);
                }
                else
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_FolderMissing"));
                }
            };
            menu.Items.Add(copyMdItem);

            var copyBase64Item = new MenuItem
            {
                Header = LocalizationService.Get("Recent_CopyBase64"),
                IsEnabled = !capture.IsVideo
            };
            copyBase64Item.Click += (_, _) =>
            {
                string? uri = TryGetCaptureBase64Uri(capture);
                if (!string.IsNullOrEmpty(uri))
                {
                    CopyTextWithToast(uri);
                }
                else
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_FolderMissing"));
                }
            };
            menu.Items.Add(copyBase64Item);
            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem
            {
                Header = LocalizationService.Get("Recent_Delete")
            };
            deleteItem.Click += (_, _) =>
            {
                var (deleted, failed) = RecentCaptureService.Instance.DeleteCaptures(new[] { capture });
                RefreshRecentStrip();
                if (deleted > 0)
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_Deleted"));
                }
                else if (failed > 0)
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_DeleteFailed"));
                }
            };
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
        }

        private static void CopyTextWithToast(string text)
        {
            try
            {
                Clipboard.SetText(text);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
            }
            catch
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Error"));
            }
        }

        internal static string? FormatRecentMarkdown(
            string? diskPath,
            string displayName,
            Func<string?>? base64Provider = null)
        {
            string alt = string.IsNullOrWhiteSpace(displayName) ? "capture" : displayName;
            if (!string.IsNullOrEmpty(diskPath))
            {
                string normalized = diskPath.Replace('\\', '/');
                return $"![{alt}](file:///{normalized})";
            }

            string? uri = base64Provider?.Invoke();
            return string.IsNullOrEmpty(uri) ? null : $"![{alt}]({uri})";
        }

        private static string? TryGetCaptureBase64Uri(RecentCapture capture)
        {
            if (capture.IsVideo)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(capture.Path) && File.Exists(capture.Path))
            {
                try
                {
                    return ImageProcessingService.ToBase64DataUri(capture.Path);
                }
                catch
                {
                }
            }

            byte[]? pngBytes = capture.CompressedPng;
            if ((pngBytes == null || pngBytes.Length == 0) &&
                capture.ResolveFullImage() is BitmapSource resolved)
            {
                try
                {
                    pngBytes = ImageIO.EncodePng(resolved);
                }
                catch
                {
                }
            }

            return pngBytes != null && pngBytes.Length > 0
                ? ImageProcessingService.ToBase64DataUri(pngBytes, "image/png")
                : null;
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
                    var files = new StringCollection { capture.Path };
                    Clipboard.SetFileDropList(files);
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
                    return;
                }

                var bitmap = capture.ResolveFullImage();
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
                if (capture.ResolveFullImage() is not BitmapSource srcBitmap)
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
                RevealCaptureInExplorer(capture.Path);
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

        private static void RevealCaptureInExplorer(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_FolderMissing"));
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void RecentDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not RecentCapture capture)
            {
                return;
            }

            if (!capture.IsConfirmingDelete)
            {
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

            _deleteResetTimer?.Stop();
            var (deleted, failed) = RecentCaptureService.Instance.DeleteCaptures(new[] { capture });
            if (deleted > 0)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_Deleted"));
            }
            else if (failed > 0)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Recent_DeleteFailed"));
            }
        }

        // ============================ Utilities & Actions ============================
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
        private void BtnSnapWindow_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.SnapActiveWindow(waitForFocusRestore: true);
        private void BtnRecordNow_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.StartRecording();

        private void BtnHeroProfile_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsService.Instance.Current;
            settings.ActiveCaptureProfile = CaptureController.CycleCaptureProfile(settings.ActiveCaptureProfile);
            SettingsService.Instance.Save();
            RefreshHeroProfileLabel();
        }

        private void RefreshHeroProfileLabel()
        {
            if (BtnHeroProfile == null)
            {
                return;
            }

            string profile = CaptureController.NormalizeCaptureProfile(SettingsService.Instance.Current.ActiveCaptureProfile);
            string key = profile switch
            {
                "Discord" => "Profile_DiscordShort",
                "BugReport" => "Profile_BugReportShort",
                "OcrOnly" => "Profile_OcrOnlyShort",
                _ => "Profile_DefaultShort"
            };
            BtnHeroProfile.Content = LocalizationService.Get(key);
        }

        private void RefreshLiteQuickBar()
        {
            if (BtnLiteFormat == null)
            {
                return;
            }

            var s = SettingsService.Instance.Current;
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                BtnLiteFormat.Content = LocalizationService.Format("Lite_QuickFormat", (s.ImageFormat ?? "PNG").ToUpperInvariant());
                TglLiteAutoCopy.IsChecked = s.AutoCopyCapture;
                TglLiteFrame.IsChecked = s.PrettyFrameExport;
                TglLiteInstant.IsChecked = s.InstantCaptureCopy;
                if (TglLiteCoords != null) TglLiteCoords.IsChecked = s.ShowCursorCoordinates;
                BtnLiteDelay.Content = LocalizationService.Format("Lite_QuickDelay", s.CaptureDelaySeconds);
                RefreshRightClickButtons();
            }
            finally
            {
                _loading = wasLoading;
            }
        }

        private void BtnLiteFormat_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsService.Instance.Current;
            s.ImageFormat = (s.ImageFormat ?? "PNG").ToUpperInvariant() switch
            {
                "PNG" => "JPG",
                "JPG" => "WEBP",
                _ => "PNG"
            };
            SettingsService.Instance.Save();
            _captureTab?.LoadFromSettings();
            RefreshLiteQuickBar();
        }

        private void BtnLiteDelay_Click(object sender, RoutedEventArgs e)
        {
            var s = SettingsService.Instance.Current;
            s.CaptureDelaySeconds = s.CaptureDelaySeconds switch
            {
                0 => 3,
                3 => 5,
                _ => 0
            };
            SettingsService.Instance.Save();
            _captureTab?.LoadFromSettings();
            RefreshLiteQuickBar();
        }

        private void RefreshRightClickButtons()
        {
            var mode = CaptureController.ParseRightClickAction(SettingsService.Instance.Current.CaptureRightClickAction);
            string shortKey = mode switch
            {
                CaptureRightClickAction.GoBackThenClose => "Capture_RightClickShort_GoBackClose",
                CaptureRightClickAction.Close => "Capture_RightClickShort_Close",
                CaptureRightClickAction.ContextMenu => "Capture_RightClickShort_Menu",
                CaptureRightClickAction.Nothing => "Capture_RightClickShort_Nothing",
                _ => "Capture_RightClickShort_GoBack"
            };
            string label = LocalizationService.Get(shortKey) + " ▾";
            if (BtnLiteRightClick != null) BtnLiteRightClick.Content = label;
            if (BtnModuleRightClick != null) BtnModuleRightClick.Content = label;
        }

        private void BtnRightClickMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button anchor) return;

            var current = CaptureController.ParseRightClickAction(SettingsService.Instance.Current.CaptureRightClickAction);
            var menu = new ContextMenu
            {
                Style = (Style)FindResource("CryoContextMenu"),
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false
            };

            (string Tag, string Key)[] options =
            {
                ("GoBack", "Capture_RightClickGoBack"),
                ("GoBackThenClose", "Capture_RightClickGoBackClose"),
                ("Close", "Capture_RightClickClose"),
                ("ContextMenu", "Capture_RightClickMenu"),
                ("Nothing", "Capture_RightClickNothing")
            };

            foreach (var (tag, key) in options)
            {
                string prefix = current.ToString() == tag ? "✓  " : "     ";
                var item = new MenuItem { Header = prefix + LocalizationService.Get(key) };
                string selectedTag = tag;
                item.Click += (_, _) =>
                {
                    SettingsService.Instance.Current.CaptureRightClickAction = selectedTag;
                    SettingsService.Instance.Save();
                    _captureTab?.LoadFromSettings();
                    RefreshRightClickButtons();
                };
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

        private void LiteQuickToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || _initializing)
            {
                return;
            }

            var s = SettingsService.Instance.Current;
            s.AutoCopyCapture = TglLiteAutoCopy.IsChecked == true;
            s.PrettyFrameExport = TglLiteFrame.IsChecked == true;
            s.InstantCaptureCopy = TglLiteInstant.IsChecked == true;
            if (TglLiteCoords != null) s.ShowCursorCoordinates = TglLiteCoords.IsChecked == true;
            SettingsService.Instance.Save();
            _captureTab?.LoadFromSettings();
        }

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
            RefreshHeroProfileLabel();
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

        private void BtnQuickCapture_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.StartCapture();
        private void BtnQuickColor_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenColorPicker();
        private void BtnQuickPinner_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.TogglePinnerFromDashboard();
        private void BtnQuickClipboard_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenClipboardHistory();
        private void BtnGoCompress_Click(object sender, RoutedEventArgs e) => ShowTab("Tools");
        private void BtnGoQr_Click(object sender, RoutedEventArgs e) => ShowTab("Tools");
        private void BtnQrScanScreen_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.StartCapture();

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

        private void BtnActivityOcr_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenOcrFile();
        private void BtnActivityPrivacy_Click(object sender, RoutedEventArgs e) => ShowTab("Workspace", "Privacy");
        private void BtnLiveDraw_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.ToggleLiveDraw();
        private void BtnScreenRuler_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenScreenRuler();
        private void BtnFileShelf_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenFileShelf();
        private void BtnImageEditor_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenImageEditor();
        private void BtnDeveloperToolbox_Click(object sender, RoutedEventArgs e) => App.CurrentApp?.OpenDeveloperToolbox();

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

            double cardWidth = ActivitySectionsPanel.Children.Count <= 2 ? 290 : 196;
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
                Width = 196,
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
                Width = 196,
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
                if (!Directory.Exists(folder)) return 0;
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
            _inactiveTabTimer?.Stop();
            _inactiveTabTimer = null;
            _quitConfirmTimer?.Stop();
            _quitConfirmTimer = null;
            _deleteResetTimer?.Stop();
            _deleteResetTimer = null;
            if (RecentItems != null) RecentItems.ItemsSource = null;
            ActivitySectionsPanel?.Children.Clear();
            _toolsTab?.ReleaseResources();
            _workbenchTab?.ReleaseResources();
            HostCapture.Content = null;
            HostTools.Content = null;
            HostWorkspace.Content = null;
            HostHotkeys.Content = null;
            HostGeneral.Content = null;
            _captureTab = null;
            _toolsTab = null;
            _workbenchTab = null;
            _hotkeysTab = null;
            _generalTab = null;
            base.OnClosed(e);
        }
    }
}
