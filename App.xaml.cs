using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CryoUtils.Services;
using CryoUtils.Tray;
using CryoUtils.Views;

namespace CryoUtils
{
    public partial class App : Application
    {
        public static App? CurrentApp { get; private set; }

        private TrayIconManager? _tray;
        private MainWindow? _dashboard;
        private ColorPickerWindow? _colorPicker;
        private ClipboardHistoryWindow? _clipboardWindow;
        private ToastWindow? _toast;
        private bool _captureRestoresDashboard;
        private bool _pickerRestoresDashboard;
        private bool _exiting;

        public App()
        {
            CurrentApp = this;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            var settings = SettingsService.Instance.Current;
            ThemeService.Initialize(settings.Theme);
            LocalizationService.Instance.Apply(settings.Language);
            StartupService.SetEnabled(settings.StartWithWindows);

            GlobalHotkeyService.Instance.HotkeyPressed += OnHotkeyPressed;
            SettingsService.Instance.SettingsChanged += ApplyAllSettings;

            ApplyHotkeys();
            ClipboardService.Instance.SetEnabled(settings.ClipboardHistoryEnabled);
            _tray = new TrayIconManager();

            bool background = e.Args.Any(argument =>
                string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
            if (!background && !settings.LaunchInBackground)
            {
                ShowDashboard();
            }
        }

        public bool IsDashboardVisible => _dashboard?.IsVisible == true;

        public void ShowDashboard(string tab = "Overview")
        {
            if (_exiting)
            {
                return;
            }

            if (_dashboard == null)
            {
                _dashboard = new MainWindow();
                _dashboard.Closed += (_, _) => _dashboard = null;
            }

            _dashboard.ShowTab(tab);
            if (!_dashboard.IsVisible)
            {
                _dashboard.Show();
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

        public void StartCapture()
        {
            if (!SettingsService.Instance.Current.CaptureEnabled)
            {
                ShowToast(LocalizationService.Get("Module_OffNote"));
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

            _colorPicker = new ColorPickerWindow();
            _colorPicker.Closed += (_, _) =>
            {
                _colorPicker = null;
                if (_pickerRestoresDashboard && !_exiting)
                {
                    _pickerRestoresDashboard = false;
                    ShowDashboard();
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

            bool restore = IsDashboardVisible;
            if (restore)
            {
                _dashboard?.Hide();
                await Task.Delay(140);
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

            _clipboardWindow = new ClipboardHistoryWindow();
            _clipboardWindow.Closed += (_, _) => _clipboardWindow = null;
            _clipboardWindow.Show();
            _clipboardWindow.Activate();
        }

        public void PrepareForCapture()
        {
            if (IsDashboardVisible)
            {
                _captureRestoresDashboard = true;
                _dashboard?.Hide();
            }

            _toast?.Close();
            _toast = null;
            if (_colorPicker != null)
            {
                _pickerRestoresDashboard = false;
                _colorPicker.Close();
            }
            if (_clipboardWindow != null)
            {
                _clipboardWindow.Close();
            }
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
                new Action(() => ShowDashboard()));
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
            ThemeService.Apply(settings.Theme);
            LocalizationService.Instance.Apply(settings.Language);
            StartupService.SetEnabled(settings.StartWithWindows);
            ClipboardService.Instance.SetEnabled(settings.ClipboardHistoryEnabled);
            if (!settings.WindowPinnerEnabled)
            {
                TopmostService.Instance.Unpin(silent: true);
            }
            TopmostService.Instance.ApplySettings();
            ApplyHotkeys();
        }

        private void ApplyHotkeys()
        {
            var settings = SettingsService.Instance.Current;
            RegisterHotkey(
                GlobalHotkeyService.CaptureHotkeyId,
                settings.CaptureEnabled,
                settings.CaptureHotkeyModifiers,
                settings.CaptureHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.ColorPickerHotkeyId,
                settings.ColorPickerEnabled,
                settings.ColorPickerHotkeyModifiers,
                settings.ColorPickerHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.WindowPinnerHotkeyId,
                settings.WindowPinnerEnabled,
                settings.WindowPinnerHotkeyModifiers,
                settings.WindowPinnerHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.PlainPasteHotkeyId,
                settings.ClipboardHistoryEnabled,
                settings.PlainPasteHotkeyModifiers,
                settings.PlainPasteHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.ClipboardHistoryHotkeyId,
                settings.ClipboardHistoryEnabled,
                settings.ClipboardHotkeyModifiers,
                settings.ClipboardHotkeyKey);
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
            }
        }

        public void ExitApplication()
        {
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            SettingsService.Instance.Save();
            _dashboard?.PrepareForShutdown();
            _tray?.Dispose();
            _tray = null;
            GlobalHotkeyService.Instance.Dispose();
            ClipboardService.Instance.Dispose();
            TopmostService.Instance.RestoreForExit();
            ThemeService.Shutdown();
            Shutdown();
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
            ShowToast(LocalizationService.Format("Error_Body", e.Exception.Message));
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    ShowToast(LocalizationService.Format("Error_Body", exception.Message))));
            }
        }
    }
}
