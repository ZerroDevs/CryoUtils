using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CryoUtils.Services;
using CryoUtils.Services.Recording;
using CryoUtils.Tray;
using CryoUtils.Views;

namespace CryoUtils
{
    public partial class App : Application
    {
        public static App? CurrentApp { get; private set; }
        public bool IsExiting => _exiting;

        private TrayIconManager? _tray;
        private MainWindow? _dashboard;
        private ColorPickerWindow? _colorPicker;
        private ClipboardHistoryWindow? _clipboardWindow;
        private LiveDrawWindow? _liveDraw;
        private ScreenRulerWindow? _screenRuler;
        private FileShelfWindow? _fileShelf;
        private ImageEditorWindow? _imageEditor;
        private DeveloperToolboxWindow? _developerToolbox;
        private ToastWindow? _toast;
        private string? _lastLanguage;
        private bool _lastContextMenuEnabled;
        private bool _lastScreenshotOnly;
        private bool _captureRestoresDashboard;
        private bool _pickerRestoresDashboard;
        private bool _exiting;
        private bool _recordingActive;
        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _activateEvent;
        private RegisteredWaitHandle? _activateWaitHandle;
        private readonly List<PinWindow> _pinWindows = new();
        private static bool _uiResourcesLoaded;
        private static readonly object UiResourcesLock = new();

        public static void EnsureUiResourcesLoaded()
        {
            if (_uiResourcesLoaded)
            {
                return;
            }

            var app = Current;
            if (app == null)
            {
                return;
            }

            lock (UiResourcesLock)
            {
                if (_uiResourcesLoaded)
                {
                    return;
                }

                var existing = app.Resources.MergedDictionaries.Any(d =>
                    d.Source?.OriginalString.Contains("Resources/Controls.xaml", StringComparison.OrdinalIgnoreCase) == true);
                if (!existing)
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("Resources/Controls.xaml", UriKind.Relative)
                    });
                }

                _uiResourcesLoaded = true;
            }
        }

        public App()
        {
            CurrentApp = this;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            // Installer cleanup hook. This must run before any startup/settings side effects.
            if (e.Args.Any(argument =>
                    string.Equals(argument, "--uninstall-cleanup", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(argument, "--unregister-context-menu", StringComparison.OrdinalIgnoreCase)))
            {
                CryoContextMenuService.UnregisterAll();
                StartupService.SetEnabled(false);
                Shutdown();
                return;
            }

            var settings = SettingsService.Instance.Current;
            if (e.Args.Any(argument =>
                    string.Equals(argument, "--safe-mode", StringComparison.OrdinalIgnoreCase)))
            {
                settings.PinnedClickThrough = false;
                settings.PinnedOpacity = 100;
                settings.ShowPinnedBorder = false;
                settings.PinnerRestoreEnabled = false;
                SettingsService.Instance.SaveWithoutNotification();
                TopmostService.ReleaseSavedTopmosts();
            }
            _lastLanguage = settings.Language;
            ThemeService.Initialize(settings.Theme);
            LocalizationService.Instance.Apply(settings.Language);
            StartupService.SetEnabled(settings.StartWithWindows);

            // Handle CLI file action commands: CryoUtils.exe --action <action_id> "<file_path>"
            int actionIdx = Array.FindIndex(e.Args, a => string.Equals(a, "--action", StringComparison.OrdinalIgnoreCase));
            if (actionIdx >= 0 && actionIdx + 2 < e.Args.Length)
            {
                string action = e.Args[actionIdx + 1];
                string targetFile = e.Args[actionIdx + 2];
                _ = HandleCliActionAsync(action, targetFile);
                return;
            }

            if (e.Args.Any(argument =>
                    string.Equals(argument, "--capture", StringComparison.OrdinalIgnoreCase)))
            {
                EnsureUiResourcesLoaded();
                CaptureController.Instance.Start(oneShotExit: true);
                return;
            }

            bool backgroundLaunch = e.Args.Any(argument =>
                string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--tray", StringComparison.OrdinalIgnoreCase));

            _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\CryoUtils.SingleInstance.v1", out bool createdNew);
            try
            {
                _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CryoUtils.Activate.v1");
            }
            catch
            {
                _activateEvent = null;
            }

            if (!createdNew)
            {
                if (!backgroundLaunch)
                {
                    try { _activateEvent?.Set(); } catch { }
                }

                try { _activateEvent?.Dispose(); } catch { }
                try { _singleInstanceMutex?.Dispose(); } catch { }
                _activateEvent = null;
                _singleInstanceMutex = null;
                Shutdown(0);
                return;
            }

            if (_activateEvent != null)
            {
                _activateWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                    _activateEvent,
                    (_, __) => Dispatcher.BeginInvoke(new Action(() => ShowDashboard())),
                    null,
                    Timeout.Infinite,
                    executeOnlyOnce: false);
            }

            // Only rewrite HKCU Explorer keys if missing, path-changed, or language-changed.
            CryoContextMenuService.SyncIfNeeded(settings.ContextMenuEnabled);
            _lastContextMenuEnabled = settings.ContextMenuEnabled;
            _lastScreenshotOnly = settings.ScreenshotOnlyMode;

            GlobalHotkeyService.Instance.HotkeyPressed += OnHotkeyPressed;
            SettingsService.Instance.SettingsChanged += ApplyAllSettings;
            ThemeService.ThemeChanged += OnCapturePoolInvalidated;
            LocalizationService.Instance.LanguageChanged += OnCapturePoolLanguageInvalidated;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnCapturePoolInvalidated;

            // Pre-load UI resources eagerly so the first hotkey/tray-click capture
            // does not pay the dictionary-merge cost (~100-300 ms) on the UI thread.
            EnsureUiResourcesLoaded();

            ApplyHotkeys();
            if (!settings.ScreenshotOnlyMode)
            {
                ClipboardService.Instance.SetEnabled(settings.ClipboardHistoryEnabled);
                if (settings.WindowPinnerEnabled)
                {
                    TopmostService.Instance.RestoreSavedPins();
                }
            }
            _tray = new TrayIconManager();

            if (!settings.ScreenshotOnlyMode && settings.ReplayEnabled)
            {
                Dispatcher.BeginInvoke(new Action(() => ApplyReplayPreference()));
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => CaptureController.Instance.PreWarmIdle()));

            bool background = e.Args.Any(argument =>
                string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
            if (!background && !settings.LaunchInBackground)
            {
                ShowDashboard();
            }
            else
            {
                MemoryTrimService.ScheduleTrim(1200);
            }
        }

        public bool IsDashboardVisible => _dashboard?.IsVisible == true;

        public void ShowDashboard(string tab = "Overview", string? subTab = null, bool animateReveal = true)
        {
            if (_exiting)
            {
                return;
            }

            EnsureUiResourcesLoaded();
            if (_dashboard == null)
            {
                _dashboard = new MainWindow();
                _dashboard.Closed += (_, _) =>
                {
                    _dashboard = null;
                    RecentCaptureService.Instance.ReleaseBackgroundCache(SettingsService.Instance.Current.ScreenshotOnlyMode);
                    if (!_exiting)
                    {
                        MemoryTrimService.ScheduleTrim(400);
                    }
                };
            }

            _dashboard.ShowTab(tab, subTab);
            bool wasVisible = _dashboard.IsVisible;
            if (!wasVisible)
            {
                _dashboard.PositionAboveTray();
                _dashboard.Show();
                _dashboard.PlayTrayRevealAnimation(animateReveal);
            }
            if (_dashboard.WindowState == WindowState.Minimized)
            {
                _dashboard.WindowState = WindowState.Normal;
            }
            _dashboard.Activate();
            _dashboard.Topmost = true;
            _dashboard.Topmost = false;
            _dashboard.Focus();
        }

        public void StartCapture(int delaySeconds = -1)
        {
            MemoryTrimService.PauseForInteraction(TimeSpan.FromSeconds(5));
            if (!SettingsService.Instance.Current.CaptureEnabled)
            {
                ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }

            int delay = delaySeconds >= 0
                ? delaySeconds
                : SettingsService.Instance.Current.CaptureDelaySeconds;

            if (delay > 0)
            {
                EnsureUiResourcesLoaded();
                PrepareForCapture();
                try
                {
                    var countdown = new CountdownWindow(
                        delay,
                        () => CaptureController.Instance.Start(),
                        FinishCapture);
                    countdown.Show();
                }
                catch (Exception ex)
                {
                    FinishCapture();
                    ShowToast(LocalizationService.Format("Error_Body", ex.Message));
                }
                return;
            }

            CaptureController.Instance.Start();
        }

        public void StartRecording()
        {
            if (!SettingsService.Instance.Current.CaptureEnabled)
            {
                ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }
            CaptureController.Instance.StartRecordingMode();
        }

        public void OpenRecorderSettings()
        {
            EnsureUiResourcesLoaded();
            var window = new RecorderSettingsWindow
            {
                Owner = _dashboard != null && _dashboard.IsVisible ? _dashboard : null
            };
            window.ShowDialog();
        }

        public void SetRecordingState(bool active)
        {
            _recordingActive = active;
            _tray?.SetRecordingBadge(active || ReplayService.Instance.IsRunning);
        }

        public void SyncRecordingBadge()
        {
            _tray?.SetRecordingBadge(_recordingActive || ReplayService.Instance.IsRunning);
        }

        public void ApplyReplayPreference()
        {
            var enabled = SettingsService.Instance.Current.ReplayEnabled;
            if (enabled && !ReplayService.Instance.IsRunning)
            {
                if (!ReplayService.Instance.Start())
                {
                    ShowToast(LocalizationService.Format(
                        "Error_Body",
                        ReplayService.Instance.LastError));
                }
                else if (!ReplayService.Instance.IsUsingWgc)
                {
                    ShowToast(LocalizationService.Get("Rec_GdiFallback"));
                }
                else if ((SettingsService.Instance.Current.RecorderSystemAudio ||
                          SettingsService.Instance.Current.RecorderMicrophone) &&
                         !ReplayService.Instance.HasAudio)
                {
                    ShowToast(LocalizationService.Get("Rec_AudioUnavailable"));
                }
            }
            else if (!enabled && ReplayService.Instance.IsRunning)
            {
                ReplayService.Instance.Stop();
            }

            SyncRecordingBadge();
            ApplyHotkeys();
        }

        public void ToggleReplayBuffer()
        {
            if (ReplayService.Instance.IsRunning)
            {
                SettingsService.Instance.Current.ReplayEnabled = false;
                ReplayService.Instance.Stop();
                SettingsService.Instance.Save();
            }
            else
            {
                SettingsService.Instance.Current.ReplayEnabled = true;
                SettingsService.Instance.Save();
                ApplyReplayPreference();
            }

            SyncRecordingBadge();
            ApplyHotkeys();
        }

        public async void SaveReplay()
        {
            try
            {
                if (!ReplayService.Instance.IsRunning)
                {
                    ShowToast(LocalizationService.Get("Rec_ReplayNotRunning"));
                    return;
                }

                var settings = SettingsService.Instance.Current;
                string path;
                if (settings.RecorderTrimBeforeSave)
                {
                    string temp = Path.Combine(
                        Path.GetTempPath(),
                        "CryoUtils",
                        $"replay_trim_{Guid.NewGuid():N}.mp4");
                    Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                    path = await ReplayService.Instance.SaveLastAsync(
                        outputPath: temp,
                        copyToClipboard: false,
                        addToLibrary: false);
                    string suggested = Path.Combine(
                        SettingsService.Instance.EffectiveSaveFolder,
                        ScreenCaptureService.CreateTimestampedFileName(".mp4"));
                    EnsureUiResourcesLoaded();
                    var trim = new RecordingTrimWindow(path, suggested)
                    {
                        Owner = IsDashboardVisible ? _dashboard : null
                    };
                    if (trim.ShowDialog() != true || string.IsNullOrWhiteSpace(trim.SavedPath))
                    {
                        TryDeleteFile(temp);
                        return;
                    }

                    TryDeleteFile(temp);
                    path = trim.SavedPath;
                    await ReplayService.AddToLibraryAsync(path).ConfigureAwait(true);
                }
                else
                {
                    path = await ReplayService.Instance.SaveLastAsync(
                        copyToClipboard: false);
                }

                if (settings.RecorderCopyOnStop)
                {
                    try
                    {
                        Clipboard.SetFileDropList(new StringCollection { path });
                    }
                    catch
                    {
                    }
                }

                ShowToast(LocalizationService.Format("Capture_RecordSaved", path));
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Error_Body", ex.Message));
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        public void OpenColorPicker()
        {
            if (!SettingsService.Instance.Current.ColorPickerEnabled)
            {
                ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }

            if (_colorPicker != null)
            {
                _colorPicker.Activate();
                return;
            }

            _pickerRestoresDashboard = IsDashboardVisible;
            if (_pickerRestoresDashboard)
            {
                _dashboard?.Hide();
            }

            EnsureUiResourcesLoaded();
            _colorPicker = new ColorPickerWindow();
            _colorPicker.Closed += (_, _) =>
            {
                _colorPicker = null;
                if (_pickerRestoresDashboard && !_exiting)
                {
                    _pickerRestoresDashboard = false;
                    ShowDashboard();
                }
                else if (!_exiting)
                {
                    MemoryTrimService.ScheduleTrim(400);
                }
            };
            _colorPicker.Show();
            _colorPicker.Activate();
        }

        public async void TogglePinnerFromDashboard()
        {
            if (!SettingsService.Instance.Current.WindowPinnerEnabled)
            {
                ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }

            if (TopmostService.Instance.IsPinned)
            {
                TopmostService.Instance.Unpin();
                return;
            }

            bool restore = IsDashboardVisible;
            if (restore)
            {
                _dashboard?.Hide();
                await Task.Delay(180);
            }

            TopmostService.Instance.ToggleFocusedWindow();
            if (restore && !_exiting)
            {
                ShowDashboard();
            }
        }

        public void OpenClipboardHistory()
        {
            if (!SettingsService.Instance.Current.ClipboardHistoryEnabled)
            {
                ShowToast(LocalizationService.Get("Tools_ClipboardDisabled"));
                return;
            }

            if (_clipboardWindow != null)
            {
                _clipboardWindow.Activate();
                return;
            }

            EnsureUiResourcesLoaded();
            _clipboardWindow = new ClipboardHistoryWindow();
            _clipboardWindow.Closed += (_, _) =>
            {
                _clipboardWindow = null;
                if (!_exiting)
                {
                    MemoryTrimService.ScheduleTrim(400);
                }
            };
            _clipboardWindow.Show();
            _clipboardWindow.Activate();
        }

        public void ToggleLiveDraw()
        {
            if (_liveDraw != null)
            {
                _liveDraw.Close();
                return;
            }

            EnsureUiResourcesLoaded();
            _liveDraw = new LiveDrawWindow();
            _liveDraw.Closed += (_, _) =>
            {
                _liveDraw = null;
                if (!_exiting)
                {
                    MemoryTrimService.ScheduleTrim(400);
                }
            };
            _liveDraw.Show();
            _liveDraw.Activate();
        }

        public void OpenScreenRuler()
        {
            if (_screenRuler != null)
            {
                _screenRuler.Activate();
                return;
            }

            EnsureUiResourcesLoaded();
            _screenRuler = new ScreenRulerWindow();
            _screenRuler.Closed += (_, _) =>
            {
                _screenRuler = null;
                if (!_exiting)
                {
                    MemoryTrimService.ScheduleTrim(400);
                }
            };
            _screenRuler.Show();
            _screenRuler.Activate();
        }

        public void OpenFileShelf()
        {
            if (_fileShelf != null)
            {
                if (!_fileShelf.IsVisible)
                {
                    _fileShelf.Show();
                }
                _fileShelf.Activate();
                return;
            }

            EnsureUiResourcesLoaded();
            _fileShelf = new FileShelfWindow
            {
                Owner = IsDashboardVisible ? _dashboard : null
            };
            _fileShelf.Closed += (_, _) =>
            {
                _fileShelf = null;
                if (!_exiting)
                {
                    MemoryTrimService.ScheduleTrim(400);
                }
            };
            _fileShelf.Show();
            _fileShelf.Activate();
        }

        public void OpenImageEditor(string? path = null)
        {
            if (_imageEditor != null)
            {
                _imageEditor.Activate();
                return;
            }

            EnsureUiResourcesLoaded();
            var editor = new ImageEditorWindow(path)
            {
                Owner = IsDashboardVisible ? _dashboard : null
            };
            editor.Closed += (_, _) =>
            {
                _imageEditor = null;
                if (!_exiting)
                {
                    MemoryTrimService.ScheduleTrim(400);
                }
            };
            _imageEditor = editor;
            editor.Show();
            editor.Activate();
        }

        public void OpenDeveloperToolbox()
        {
            if (_developerToolbox != null)
            {
                if (!_developerToolbox.IsVisible)
                {
                    _developerToolbox.Show();
                }
                _developerToolbox.Activate();
                return;
            }

            EnsureUiResourcesLoaded();
            _developerToolbox = new DeveloperToolboxWindow
            {
                Owner = IsDashboardVisible ? _dashboard : null
            };
            _developerToolbox.Closed += (_, _) =>
            {
                _developerToolbox = null;
                if (!_exiting)
                {
                    MemoryTrimService.ScheduleTrim(400);
                }
            };
            _developerToolbox.Show();
            _developerToolbox.Activate();
        }

        public void OpenPrivacy() => OpenWorkbench("Privacy");

        public async void OpenOcrFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = LocalizationService.Get("Workbench_QuickOcr"),
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.jfif|All files|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(_dashboard) != true)
            {
                return;
            }

            try
            {
                var image = ImageIO.LoadThumbnail(dialog.FileName, 12000);
                string text = await OcrService.RecognizeAsync(image);
                if (string.IsNullOrWhiteSpace(text))
                {
                    ShowToast(LocalizationService.Get("Capture_OcrEmpty"));
                    return;
                }

                EnsureUiResourcesLoaded();
                var ocrWin = new OcrResultWindow(text);
                ocrWin.Closed += (_, _) =>
                {
                    if (!_exiting)
                    {
                        MemoryTrimService.ScheduleTrim(400);
                    }
                };
                ocrWin.Show();
            }
            catch (Exception ex)
            {
                ShowToast(LocalizationService.Format("Error_Body", ex.Message));
            }
        }

        public void OpenWorkbench(string? subTab = null)
        {
            ShowDashboard("Workspace", subTab);
        }

        public bool PrepareForCapture()
        {
            bool hidAny = false;
            if (IsDashboardVisible)
            {
                _captureRestoresDashboard = true;
                _dashboard?.Hide();
                hidAny = true;
            }

            if (_toast != null)
            {
                _toast.Close();
                _toast = null;
                hidAny = true;
            }
            if (_colorPicker != null)
            {
                _pickerRestoresDashboard = false;
                _colorPicker.Close();
                hidAny = true;
            }
            if (_clipboardWindow != null)
            {
                _clipboardWindow.Close();
                hidAny = true;
            }
            if (_liveDraw != null)
            {
                _liveDraw.Close();
                hidAny = true;
            }
            if (_screenRuler != null)
            {
                _screenRuler.Close();
                hidAny = true;
            }

            return hidAny;
        }

        private void OnCapturePoolInvalidated(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => CaptureController.Instance.InvalidateOverlayPool()));
        }

        private void OnCapturePoolLanguageInvalidated()
        {
            Dispatcher.BeginInvoke(new Action(() => CaptureController.Instance.InvalidateOverlayPool()));
        }

        public void FinishCapture()
        {
            if (!_captureRestoresDashboard || _exiting)
            {
                return;
            }

            _captureRestoresDashboard = false;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => ShowDashboard(animateReveal: false)));
        }

        public void RepeatLastRegion()
        {
            if (!SettingsService.Instance.Current.CaptureEnabled)
            {
                ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }

            CaptureController.Instance.StartLastRegion();
        }

        public async void CompleteOneShotCapture(bool waitForToast)
        {
            if (_exiting)
            {
                return;
            }

            if (waitForToast)
            {
                await Task.Delay(1400);
            }

            ThemeService.Shutdown();
            Shutdown();
        }

        public void ShowToast(string message)
        {
            if (_exiting || string.IsNullOrWhiteSpace(message) ||
                !SettingsService.Instance.Current.NotificationsEnabled)
            {
                return;
            }

            try
            {
                EnsureUiResourcesLoaded();
                _toast?.Close();
                _toast = new ToastWindow(message);
                _toast.Closed += (_, _) => _toast = null;
                _toast.Show();
            }
            catch
            {
            }
        }

        public void ApplyAllSettings()
        {
            if (_exiting)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            ThemeService.Apply(settings.Theme, accentPreset: settings.AccentPreset);
            LocalizationService.Instance.Apply(settings.Language);
            bool languageChanged = !string.Equals(_lastLanguage, settings.Language, StringComparison.Ordinal);
            if (languageChanged)
            {
                _lastLanguage = settings.Language;
            }

            bool contextMenuChanged = settings.ContextMenuEnabled != _lastContextMenuEnabled;
            if (contextMenuChanged)
            {
                _lastContextMenuEnabled = settings.ContextMenuEnabled;
                CryoContextMenuService.SetEnabled(settings.ContextMenuEnabled);
            }
            else if (languageChanged && settings.ContextMenuEnabled)
            {
                // The menu is localized, so rebuild it only when the language changes.
                CryoContextMenuService.RegisterAll();
            }
            StartupService.SetEnabled(settings.StartWithWindows);
            bool full = !settings.ScreenshotOnlyMode;
            ClipboardService.Instance.SetEnabled(full && settings.ClipboardHistoryEnabled);
            ClipboardService.Instance.SetPersistence(settings.ClipboardPersistenceEnabled);
            if (!full || !settings.WindowPinnerEnabled)
            {
                TopmostService.Instance.Unpin(silent: true);
            }
            TopmostService.Instance.ApplySettings();
            if (!full && ReplayService.Instance.IsRunning)
            {
                ApplyReplayPreference();
            }
            ApplyHotkeys();

            bool modeChanged = _lastScreenshotOnly != settings.ScreenshotOnlyMode;
            bool enteredScreenshotOnly = !_lastScreenshotOnly && settings.ScreenshotOnlyMode;
            _lastScreenshotOnly = settings.ScreenshotOnlyMode;
            if (modeChanged)
            {
                CaptureController.Instance.InvalidateOverlayPool();
            }
            if (!full)
            {
                CloseNonCaptureUtilityWindows();
                if (enteredScreenshotOnly)
                {
                    ShowToast(LocalizationService.Get("Toast_ScreenshotOnlyEnabled"));
                }
                MemoryTrimService.ScheduleTrim(200);
            }
        }

        private void CloseNonCaptureUtilityWindows()
        {
            CloseUtilityWindow(ref _clipboardWindow);
            CloseUtilityWindow(ref _colorPicker);
            CloseUtilityWindow(ref _liveDraw);
            CloseUtilityWindow(ref _screenRuler);
            CloseUtilityWindow(ref _fileShelf);
            CloseUtilityWindow(ref _developerToolbox);
        }

        private static void CloseUtilityWindow<T>(ref T? window) where T : Window
        {
            if (window == null)
            {
                return;
            }

            try
            {
                window.Close();
            }
            catch
            {
            }
            window = null;
        }

        public void ApplyHotkeys()
        {
            var settings = SettingsService.Instance.Current;
            bool full = !settings.ScreenshotOnlyMode;
            RegisterHotkey(
                GlobalHotkeyService.CaptureHotkeyId,
                settings.CaptureEnabled,
                settings.CaptureHotkeyModifiers,
                settings.CaptureHotkeyKey);

            string? repeatModifiers = BuildRepeatRegionModifiers(settings.CaptureHotkeyModifiers);
            RegisterHotkey(
                GlobalHotkeyService.RepeatLastRegionHotkeyId,
                settings.CaptureEnabled && repeatModifiers != null,
                repeatModifiers,
                settings.CaptureHotkeyKey);

            RegisterHotkey(
                GlobalHotkeyService.ColorPickerHotkeyId,
                full && settings.ColorPickerEnabled,
                settings.ColorPickerHotkeyModifiers,
                settings.ColorPickerHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.WindowPinnerHotkeyId,
                full && settings.WindowPinnerEnabled,
                settings.WindowPinnerHotkeyModifiers,
                settings.WindowPinnerHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.PlainPasteHotkeyId,
                full && settings.ClipboardHistoryEnabled,
                settings.PlainPasteHotkeyModifiers,
                settings.PlainPasteHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.ClipboardHistoryHotkeyId,
                full && settings.ClipboardHistoryEnabled,
                settings.ClipboardHotkeyModifiers,
                settings.ClipboardHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.WorkbenchHotkeyId,
                full,
                settings.WorkbenchHotkeyModifiers,
                settings.WorkbenchHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.ReplayHotkeyId,
                full && settings.ReplayEnabled,
                settings.ReplayHotkeyModifiers,
                settings.ReplayHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.LiveDrawHotkeyId,
                full,
                settings.LiveDrawHotkeyModifiers,
                settings.LiveDrawHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.ActiveWindowSnapHotkeyId,
                settings.CaptureEnabled,
                settings.ActiveWindowSnapHotkeyModifiers,
                settings.ActiveWindowSnapHotkeyKey);
        }

        private static string? BuildRepeatRegionModifiers(string? baseModifiers)
        {
            string mods = (baseModifiers ?? "").Trim();
            if (!mods.Contains("Shift", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(mods) ? "Shift" : $"{mods}+Shift";
            }
            if (!mods.Contains("Alt", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(mods) ? "Alt" : $"{mods}+Alt";
            }
            return null;
        }

        private static void RegisterHotkey(
            int id,
            bool enabled,
            string? modifiers,
            string? key)
        {
            if (!enabled)
            {
                GlobalHotkeyService.Instance.Unregister(id);
                return;
            }

            GlobalHotkeyService.Instance.Register(id, modifiers, key, out _);
        }

        private async void OnHotkeyPressed(int id)
        {
            switch (id)
            {
                case GlobalHotkeyService.CaptureHotkeyId:
                    StartCapture();
                    break;
                case GlobalHotkeyService.RepeatLastRegionHotkeyId:
                    RepeatLastRegion();
                    break;
                case GlobalHotkeyService.ActiveWindowSnapHotkeyId:
                    SnapActiveWindow();
                    break;
                case GlobalHotkeyService.ColorPickerHotkeyId:
                    OpenColorPicker();
                    break;
                case GlobalHotkeyService.WindowPinnerHotkeyId:
                    if (!SettingsService.Instance.Current.WindowPinnerEnabled)
                    {
                        return;
                    }
                    if (IsDashboardVisible)
                    {
                        _dashboard?.Hide();
                        await Task.Delay(140);
                    }
                    TopmostService.Instance.ToggleFocusedWindow();
                    break;
                case GlobalHotkeyService.PlainPasteHotkeyId:
                    if (SettingsService.Instance.Current.ClipboardHistoryEnabled)
                    {
                        ClipboardService.Instance.PlainPaste();
                    }
                    break;
                case GlobalHotkeyService.ClipboardHistoryHotkeyId:
                    OpenClipboardHistory();
                    break;
                case GlobalHotkeyService.WorkbenchHotkeyId:
                    OpenWorkbench();
                    break;
                case GlobalHotkeyService.ReplayHotkeyId:
                    SaveReplay();
                    break;
                case GlobalHotkeyService.LiveDrawHotkeyId:
                    ToggleLiveDraw();
                    break;
            }
        }

        public async void SnapActiveWindow(bool waitForFocusRestore = false)
        {
            if (waitForFocusRestore)
            {
                await Task.Delay(160);
            }
            CaptureController.Instance.SnapForegroundWindow();
        }

        public void RegisterPinWindow(PinWindow pin)
        {
            if (!_pinWindows.Contains(pin))
            {
                _pinWindows.Add(pin);
            }
        }

        public void UnregisterPinWindow(PinWindow pin)
        {
            _pinWindows.Remove(pin);
        }

        public bool HasClickThroughPinWindows() => _pinWindows.Any(p => p.IsClickThrough);

        public void RestoreClickThroughPins()
        {
            foreach (var pin in _pinWindows.ToArray())
            {
                if (pin.IsClickThrough)
                {
                    pin.SetClickThrough(false);
                }
            }
        }

        public void CloseAllPinWindows()
        {
            foreach (var pin in _pinWindows.ToArray())
            {
                try { pin.Close(); } catch { }
            }
            _pinWindows.Clear();
        }

        public void RefreshTrayMenu()
        {
            // TrayIconManager rebuilds its popup menu on every ShowContextMenu().
        }

        public void ExitApplication()
        {
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            _recordingActive = false;
            MemoryTrimService.Cancel(shuttingDown: true);
            ThemeService.ThemeChanged -= OnCapturePoolInvalidated;
            LocalizationService.Instance.LanguageChanged -= OnCapturePoolLanguageInvalidated;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnCapturePoolInvalidated;
            CaptureController.Instance.DisposeOverlayPool();
            SettingsService.Instance.Save();
            CloseAllPinWindows();
            _dashboard?.PrepareForShutdown();
            _tray?.Dispose();
            _tray = null;
            GlobalHotkeyService.Instance.Dispose();
            ClipboardService.Instance.Dispose();
            ReplayService.Instance.Dispose();
            TopmostService.Instance.RestoreForExit();
            ThemeService.Shutdown();
            try { _activateWaitHandle?.Unregister(null); } catch { }
            _activateWaitHandle = null;
            try { _activateEvent?.Dispose(); } catch { }
            _activateEvent = null;
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
            try { _singleInstanceMutex?.Dispose(); } catch { }
            _singleInstanceMutex = null;
            Shutdown();
        }

        private async Task HandleCliActionAsync(string action, string rawTargetPath)
        {
            EnsureUiResourcesLoaded();
            bool keepAliveForWindow = false;
            try
            {
                string filePath = (rawTargetPath ?? "").Trim().Trim('"');
                if (string.IsNullOrEmpty(filePath) ||
                    (!System.IO.File.Exists(filePath) && !System.IO.Directory.Exists(filePath)))
                {
                    Shutdown();
                    return;
                }

                bool isDir = System.IO.Directory.Exists(filePath);
                string dir = isDir ? filePath : (System.IO.Path.GetDirectoryName(filePath) ?? ".");
                string baseName = isDir
                    ? new System.IO.DirectoryInfo(filePath).Name
                    : System.IO.Path.GetFileNameWithoutExtension(filePath);
                string ext = isDir ? "" : System.IO.Path.GetExtension(filePath);

                switch (action.ToLowerInvariant())
                {
                    // Interactive window actions (process stays alive until window closes)
                    case "upload-file":
                        if (!UploadConsentService.EnsureUploadAllowed())
                        {
                            break;
                        }
                        var uploadWin = UploadProgressWindow.StartFileUpload(filePath);
                        uploadWin.Closed += (_, _) => Shutdown();
                        keepAliveForWindow = true;
                        return;

                    case "ocr-text":
                        var ocrBmp = ImageProcessingService.LoadWpfBitmap(filePath);
                        string recognized = await OcrService.RecognizeAsync(ocrBmp);
                        if (string.IsNullOrWhiteSpace(recognized))
                        {
                            ShowToast(LocalizationService.Get("Capture_OcrEmpty"));
                            break;
                        }
                        CopyTextSafe(recognized);
                        var ocrWin = new OcrResultWindow(recognized);
                        ocrWin.Closed += (_, _) => Shutdown();
                        ocrWin.Show();
                        ocrWin.Activate();
                        keepAliveForWindow = true;
                        return;

                    case "scan-qr":
                        var qrSrcBmp = ImageProcessingService.LoadWpfBitmap(filePath);
                        var qrResult = QrCodeService.Decode(qrSrcBmp);
                        if (qrResult == null || string.IsNullOrWhiteSpace(qrResult.Text))
                        {
                            ShowToast(LocalizationService.Get("Qr_NotFound"));
                            break;
                        }
                        CopyTextSafe(qrResult.Text);
                        BitmapSource cleanQr = qrSrcBmp;
                        try
                        {
                            cleanQr = QrCodeService.Encode(qrResult.Text, 512);
                        }
                        catch
                        {
                        }
                        var qrWin = new QrResultWindow(qrResult.Text, qrResult.FormatName, cleanQr);
                        qrWin.Closed += (_, _) => Shutdown();
                        qrWin.Show();
                        qrWin.Activate();
                        keepAliveForWindow = true;
                        return;

                    case "pin-image":
                        var pinBmp = ImageProcessingService.LoadWpfBitmap(filePath);
                        var pinWin = new PinWindow(pinBmp);
                        pinWin.Closed += (_, _) => Shutdown();
                        pinWin.Show();
                        pinWin.Activate();
                        keepAliveForWindow = true;
                        return;

                    case "text-to-qr":
                        string textContent = (await System.IO.File.ReadAllTextAsync(filePath)).Trim();
                        if (string.IsNullOrWhiteSpace(textContent))
                        {
                            ShowToast(LocalizationService.Get("Action_FileEmpty"));
                            break;
                        }
                        if (textContent.Length > 2900)
                        {
                            ShowToast(LocalizationService.Get("Action_QrTextTooLong"));
                            break;
                        }
                        var encodedQr = QrCodeService.Encode(textContent, 512);
                        var textQrWin = new QrResultWindow(textContent, "QR_CODE", encodedQr);
                        textQrWin.Closed += (_, _) => Shutdown();
                        textQrWin.Show();
                        textQrWin.Activate();
                        keepAliveForWindow = true;
                        return;

                    // PDF actions
                    case "pdf-to-png":
                        string pngDir = System.IO.Path.Combine(dir, $"{baseName}_pages");
                        int pages = await PdfToolsService.PdfToImagesAsync(filePath, pngDir, "PNG");
                        ShowToast(LocalizationService.Format("Action_Success", $"{pages} pages -> {baseName}_pages"));
                        break;

                    case "pdf-to-jpg":
                        string jpgDir = System.IO.Path.Combine(dir, $"{baseName}_pages");
                        int jpgPages = await PdfToolsService.PdfToImagesAsync(filePath, jpgDir, "JPG");
                        ShowToast(LocalizationService.Format("Action_Success", $"{jpgPages} pages -> {baseName}_pages"));
                        break;

                    case "compress-pdf":
                        string outCompPdf = System.IO.Path.Combine(dir, $"{baseName}_compressed.pdf");
                        long saved = await PdfToolsService.CompressPdfAsync(filePath, outCompPdf);
                        ShowToast(LocalizationService.Format("Action_Success", $"{baseName}_compressed.pdf ({saved / 1024} KB saved)"));
                        break;

                    case "pdf-grayscale":
                        string outGrayPdf = System.IO.Path.Combine(dir, $"{baseName}_grayscale.pdf");
                        await PdfToolsService.ConvertPdfToGrayscaleAsync(filePath, outGrayPdf);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outGrayPdf)));
                        break;

                    case "pdf-rotate-90":
                        string outRot90Pdf = System.IO.Path.Combine(dir, $"{baseName}_rot90.pdf");
                        await PdfToolsService.RotatePdfAsync(filePath, outRot90Pdf, 90);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outRot90Pdf)));
                        break;

                    case "pdf-rotate-180":
                        string outRot180Pdf = System.IO.Path.Combine(dir, $"{baseName}_rot180.pdf");
                        await PdfToolsService.RotatePdfAsync(filePath, outRot180Pdf, 180);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outRot180Pdf)));
                        break;

                    // Image actions
                    case "img-to-pdf":
                        string outPdf = System.IO.Path.Combine(dir, $"{baseName}.pdf");
                        PdfToolsService.ImagesToPdf(new[] { filePath }, outPdf);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outPdf)));
                        break;

                    case "quick-compress":
                        string outCompImg = System.IO.Path.Combine(dir, $"{baseName}_min{ext}");
                        var res = await CompressionService.CompressAsync(filePath, "Balanced", 75, 100);
                        await System.IO.File.WriteAllBytesAsync(outCompImg, res.Data);
                        long imgSaved = res.OriginalBytes - res.CompressedBytes;
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(outCompImg)} ({imgSaved / 1024} KB saved)"));
                        break;

                    case "remove-bg":
                        string outNoBg = System.IO.Path.Combine(dir, $"{baseName}_nobg.png");
                        ImageProcessingService.RemoveBackground(filePath, outNoBg);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outNoBg)));
                        break;

                    case "trim-alpha":
                        string outTrim = System.IO.Path.Combine(dir, $"{baseName}_trimmed.png");
                        var (tw, th) = ImageProcessingService.TrimTransparentMargins(filePath, outTrim);
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(outTrim)} ({tw}x{th})"));
                        break;

                    case "flatten-white":
                        string outFlat = System.IO.Path.Combine(dir, $"{baseName}_flat.jpg");
                        ImageProcessingService.FlattenWhite(filePath, outFlat);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outFlat)));
                        break;

                    case "strip-exif":
                        string outClean = System.IO.Path.Combine(dir, $"{baseName}_clean{ext}");
                        ImageProcessingService.StripExif(filePath, outClean);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outClean)));
                        break;

                    case "resize-50":
                        string out50 = System.IO.Path.Combine(dir, $"{baseName}_50pct{ext}");
                        var (w50, h50) = ImageProcessingService.ResizePreset(filePath, out50, "50pct");
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(out50)} ({w50}x{h50})"));
                        break;

                    case "resize-1920":
                        string out1920 = System.IO.Path.Combine(dir, $"{baseName}_1920w{ext}");
                        var (w19, h19) = ImageProcessingService.ResizePreset(filePath, out1920, "w1920");
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(out1920)} ({w19}x{h19})"));
                        break;

                    case "resize-512":
                        string out512 = System.IO.Path.Combine(dir, $"{baseName}_512px{ext}");
                        var (w51, h51) = ImageProcessingService.ResizePreset(filePath, out512, "s512");
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(out512)} ({w51}x{h51})"));
                        break;

                    case "gif-frames":
                        string framesDir = System.IO.Path.Combine(dir, $"{baseName}_frames");
                        int frameCnt = ImageProcessingService.ExtractGifFrames(filePath, framesDir);
                        ShowToast(LocalizationService.Format("Action_Success", $"{frameCnt} frames -> {baseName}_frames"));
                        break;

                    case "copy-base64":
                        string dataUri = ImageProcessingService.ToBase64DataUri(filePath);
                        CopyTextSafe(dataUri);
                        ShowToast(LocalizationService.Get("Common_Copied"));
                        break;

                    case "copy-pixels":
                        var clipBmp = ImageProcessingService.LoadWpfBitmap(filePath);
                        CopyImageSafe(clipBmp);
                        ShowToast(LocalizationService.Get("Capture_Copied"));
                        break;

                    case "convert-to-png":
                        string outPng = SafeOutPath(dir, baseName, ext, ".png");
                        ImageProcessingService.ConvertFormat(filePath, outPng, "PNG");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outPng)));
                        break;

                    case "convert-to-jpg":
                        string outJpg = SafeOutPath(dir, baseName, ext, ".jpg");
                        ImageProcessingService.ConvertFormat(filePath, outJpg, "JPG");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outJpg)));
                        break;

                    case "convert-to-webp":
                        string outWebp = SafeOutPath(dir, baseName, ext, ".webp");
                        ImageProcessingService.ConvertFormat(filePath, outWebp, "WEBP");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outWebp)));
                        break;

                    case "convert-to-ico":
                        string outIco = SafeOutPath(dir, baseName, ext, ".ico");
                        ImageProcessingService.ConvertFormat(filePath, outIco, "ICO");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outIco)));
                        break;

                    // Video & Audio actions
                    case "video-compress":
                        string outVidComp = System.IO.Path.Combine(dir, $"{baseName}_compressed.mp4");
                        long vidSaved = await MediaConversionService.CompressVideoForDiscordAsync(filePath, outVidComp);
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(outVidComp)} ({vidSaved / 1024} KB saved)"));
                        break;

                    case "video-mute":
                        string outVidMute = System.IO.Path.Combine(dir, $"{baseName}_muted.mp4");
                        await MediaConversionService.MuteVideoAsync(filePath, outVidMute);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outVidMute)));
                        break;

                    case "video-thumbnail":
                        string outThumb = System.IO.Path.Combine(dir, $"{baseName}_thumb.jpg");
                        await MediaConversionService.SaveThumbnailAsync(filePath, outThumb);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outThumb)));
                        break;

                    case "video-to-mp3":
                    case "audio-to-mp3":
                        string outMp3 = SafeOutPath(dir, baseName, ext, ".mp3");
                        await MediaConversionService.ConvertAudioAsync(filePath, outMp3, "MP3");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outMp3)));
                        break;

                    case "audio-to-wav":
                        string outWav = SafeOutPath(dir, baseName, ext, ".wav");
                        await MediaConversionService.ConvertAudioAsync(filePath, outWav, "WAV");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outWav)));
                        break;

                    case "audio-to-m4a":
                        string outM4a = SafeOutPath(dir, baseName, ext, ".m4a");
                        await MediaConversionService.ConvertAudioAsync(filePath, outM4a, "M4A");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outM4a)));
                        break;

                    case "video-to-gif":
                        string outGif = System.IO.Path.Combine(dir, $"{baseName}.gif");
                        var videoGifProgress = new OperationProgressWindow(LocalizationService.Get("Progress_VideoGif"));
                        videoGifProgress.Show();
                        int frames;
                        try
                        {
                            frames = await MediaConversionService.VideoToGifAsync(
                                filePath,
                                outGif,
                                progress: new Progress<int>(videoGifProgress.Report),
                                cancellationToken: videoGifProgress.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            ShowToast(LocalizationService.Get("Progress_Cancelled"));
                            break;
                        }
                        finally
                        {
                            videoGifProgress.Complete();
                        }
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(outGif)} ({frames} frames)"));
                        break;

                    // Developer & Checksums
                    case "json-pretty":
                        string outPrettyJson = System.IO.Path.Combine(dir, $"{baseName}.pretty.json");
                        FileToolsService.FormatJson(filePath, outPrettyJson, indent: true);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outPrettyJson)));
                        break;

                    case "json-minify":
                        string outMinJson = System.IO.Path.Combine(dir, $"{baseName}.min.json");
                        FileToolsService.FormatJson(filePath, outMinJson, indent: false);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outMinJson)));
                        break;

                    case "xml-pretty":
                        string outPrettyXml = System.IO.Path.Combine(dir, $"{baseName}.pretty.xml");
                        FileToolsService.FormatXml(filePath, outPrettyXml);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outPrettyXml)));
                        break;

                    case "hash-sha256":
                        string sha256 = FileToolsService.ComputeHash(filePath, System.Security.Cryptography.HashAlgorithmName.SHA256);
                        CopyTextSafe(sha256);
                        ShowToast($"SHA-256: {sha256[..Math.Min(16, sha256.Length)]}...");
                        break;

                    case "hash-md5":
                        string md5 = FileToolsService.ComputeHash(filePath, System.Security.Cryptography.HashAlgorithmName.MD5);
                        CopyTextSafe(md5);
                        ShowToast($"MD5: {md5}");
                        break;

                    // Directory batch tools
                    case "dir-imgs-pdf":
                        var dirImgs = FileToolsService.CollectSortedImages(filePath);
                        if (dirImgs.Count == 0)
                        {
                            ShowToast(LocalizationService.Get("Action_NoImages"));
                            break;
                        }
                        string outDirPdf = System.IO.Path.Combine(filePath, $"{baseName}_merged.pdf");
                        int mergedCount = PdfToolsService.ImagesToPdf(dirImgs, outDirPdf);
                        ShowToast(LocalizationService.Format("Action_Success", $"{mergedCount} images -> {System.IO.Path.GetFileName(outDirPdf)}"));
                        break;

                    case "dir-imgs-gif":
                        string outDirGif = System.IO.Path.Combine(filePath, $"{baseName}.gif");
                        var folderGifProgress = new OperationProgressWindow(LocalizationService.Get("Progress_FolderGif"));
                        folderGifProgress.Show();
                        int gifCount;
                        try
                        {
                            var folderProgress = new Progress<int>(folderGifProgress.Report);
                            gifCount = await Task.Run(
                                () => FileToolsService.FolderImagesToGif(
                                    filePath,
                                    outDirGif,
                                    progress: folderProgress,
                                    cancellationToken: folderGifProgress.Token),
                                folderGifProgress.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            ShowToast(LocalizationService.Get("Progress_Cancelled"));
                            break;
                        }
                        finally
                        {
                            folderGifProgress.Complete();
                        }
                        ShowToast(gifCount > 0
                            ? LocalizationService.Format("Action_Success", $"{gifCount} frames -> {System.IO.Path.GetFileName(outDirGif)}")
                            : LocalizationService.Get("Action_NoImages"));
                        break;

                    case "dir-compress-imgs":
                        var (compCount, compSaved) = await FileToolsService.BatchCompressFolderAsync(filePath);
                        ShowToast(compCount > 0
                            ? LocalizationService.Format("Action_Success", $"{compCount} images -> _compressed ({compSaved / 1024} KB saved)")
                            : LocalizationService.Get("Action_NoImages"));
                        break;
                }

                await Task.Delay(2600);
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                string logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cli.log");
                System.IO.File.AppendAllText(logFile, $"ERROR [{action}]: {ex}\n");
                ShowToast(LocalizationService.Format("Action_Failed", ex.Message));
                if (!keepAliveForWindow)
                {
                    await Task.Delay(2600);
                }
            }
            finally
            {
                if (!keepAliveForWindow)
                {
                    Shutdown();
                }
            }
        }

        private static string SafeOutPath(string dir, string baseName, string sourceExt, string targetExt)
        {
            string name = string.Equals(sourceExt, targetExt, StringComparison.OrdinalIgnoreCase)
                ? $"{baseName}_converted{targetExt}"
                : $"{baseName}{targetExt}";
            string candidate = System.IO.Path.Combine(dir, name);
            if (!System.IO.File.Exists(candidate))
            {
                return candidate;
            }

            string stem = System.IO.Path.GetFileNameWithoutExtension(candidate);
            string extension = System.IO.Path.GetExtension(candidate);
            for (int suffix = 2; suffix < 1000; suffix++)
            {
                string next = System.IO.Path.Combine(dir, $"{stem}_{suffix}{extension}");
                if (!System.IO.File.Exists(next))
                {
                    return next;
                }
            }

            return System.IO.Path.Combine(dir, $"{stem}_{Guid.NewGuid():N}{extension}");
        }

        private static void CopyTextSafe(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                try
                {
                    var data = new DataObject();
                    data.SetText(text);
                    Clipboard.SetDataObject(data, true);
                }
                catch
                {
                }
            }
        }

        private static void CopyImageSafe(System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            try
            {
                Clipboard.SetImage(bitmap);
            }
            catch
            {
                try
                {
                    var data = new DataObject();
                    data.SetImage(bitmap);
                    Clipboard.SetDataObject(data, true);
                }
                catch
                {
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            _tray = null;
            GlobalHotkeyService.Instance.Dispose();
            ClipboardService.Instance.Dispose();
            TopmostService.Instance.RestoreForExit();
            ThemeService.Shutdown();
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            DiagnosticsService.Log("Unhandled dispatcher exception", e.Exception);
            ShowToast(LocalizationService.Format("Error_Body", e.Exception.Message));
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                DiagnosticsService.Log("Unhandled domain exception", exception);
                Dispatcher.BeginInvoke(new Action(() =>
                    ShowToast(LocalizationService.Format("Error_Body", exception.Message))));
            }
        }
    }
}
