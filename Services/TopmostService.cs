using System;
using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CryoUtils.Views;

namespace CryoUtils.Services
{
    public sealed class TopmostService : IDisposable
    {
        private static TopmostService? _instance;
        public static TopmostService Instance => _instance ??= new TopmostService();

        private readonly DispatcherTimer _tracker;
        private PinnedBorderWindow? _border;
        private IntPtr _pinnedHandle;
        private long _originalExStyle;

        public event Action? StateChanged;

        public IntPtr PinnedHandle => _pinnedHandle;
        public bool IsPinned => _pinnedHandle != IntPtr.Zero;
        public string PinnedTitle { get; private set; } = "";
        public int CurrentOpacity => SettingsService.Instance.Current.PinnedOpacity;
        public bool IsClickThrough => SettingsService.Instance.Current.PinnedClickThrough;

        private TopmostService()
        {
            _tracker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _tracker.Tick += (_, _) => TrackBorder();
        }

        public void ToggleFocusedWindow()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                Notify(LocalizationService.Get("Pinner_NoWindow"));
                return;
            }

            if (_pinnedHandle == foreground)
            {
                Unpin();
                return;
            }

            if (!IsValidTargetWindow(foreground))
            {
                Notify(LocalizationService.Get("Pinner_NoWindow"));
                return;
            }

            if (_pinnedHandle != IntPtr.Zero)
            {
                Unpin(silent: true);
            }

            _originalExStyle = NativeMethods.GetWindowLongPtr(foreground, NativeMethods.GWL_EXSTYLE).ToInt64();
            long value = _originalExStyle | NativeMethods.WS_EX_TOPMOST;

            var settings = SettingsService.Instance.Current;
            int opacity = Math.Clamp(settings.PinnedOpacity, 20, 100);
            if (opacity < 100 || settings.PinnedClickThrough)
            {
                value |= NativeMethods.WS_EX_LAYERED;
            }
            if (settings.PinnedClickThrough)
            {
                value |= NativeMethods.WS_EX_TRANSPARENT;
            }

            NativeMethods.SetWindowLongPtr(foreground, NativeMethods.GWL_EXSTYLE, new IntPtr(value));
            if (opacity < 100 || settings.PinnedClickThrough)
            {
                byte alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255.0 / 100.0), 25, 255);
                NativeMethods.SetLayeredWindowAttributes(foreground, 0, alpha, NativeMethods.LWA_ALPHA);
            }

            NativeMethods.SetWindowPos(
                foreground,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

            _pinnedHandle = foreground;
            PinnedTitle = ScreenCaptureService.GetWindowTitle(foreground);
            if (string.IsNullOrWhiteSpace(PinnedTitle))
            {
                PinnedTitle = LocalizationService.Get("Common_Unknown");
            }

            if (settings.ShowPinnedBorder)
            {
                ShowBorder();
            }
            if (settings.PinnedBorderSound)
            {
                SystemSounds.Asterisk.Play();
            }

            _tracker.Start();
            Notify(LocalizationService.Format("Pinner_Pinned", PinnedTitle));
            StateChanged?.Invoke();
        }

        private static bool IsValidTargetWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
            {
                return false;
            }

            if (NativeMethods.IsIconic(hwnd))
            {
                return false;
            }

            // Exclude CryoUtils' own windows
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == Environment.ProcessId)
            {
                return false;
            }

            // Exclude desktop and shell windows
            var sb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            string className = sb.ToString();

            string[] shellClasses =
            {
                "Progman",
                "WorkerW",
                "Shell_TrayWnd",
                "Shell_SecondaryTrayWnd",
                "Windows.UI.Core.CoreWindow",
                "DV2ControlHost",
                "SideBar_AppBarWindow",
                "NotifyIconOverflowWindow"
            };

            foreach (string shellClass in shellClasses)
            {
                if (string.Equals(className, shellClass, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Exclude tiny or invisible helper windows
            var bounds = ScreenCaptureService.GetWindowBounds(hwnd);
            if (bounds.Width < 120 || bounds.Height < 80)
            {
                return false;
            }

            // Exclude windows without a title (e.g. background utilities, IPC helpers, tray hosts)
            string title = ScreenCaptureService.GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            return true;
        }

        public void Unpin(bool silent = false)
        {
            if (_pinnedHandle != IntPtr.Zero && NativeMethods.IsWindow(_pinnedHandle))
            {
                NativeMethods.SetLayeredWindowAttributes(_pinnedHandle, 0, 255, NativeMethods.LWA_ALPHA);
                long currentStyle = NativeMethods.GetWindowLongPtr(_pinnedHandle, NativeMethods.GWL_EXSTYLE).ToInt64();
                long restoreStyle = _originalExStyle != 0 ? _originalExStyle : currentStyle;

                restoreStyle &= ~(long)NativeMethods.WS_EX_TOPMOST;
                restoreStyle &= ~(long)NativeMethods.WS_EX_TRANSPARENT;

                if ((_originalExStyle & NativeMethods.WS_EX_LAYERED) == 0)
                {
                    restoreStyle &= ~(long)NativeMethods.WS_EX_LAYERED;
                }

                NativeMethods.SetWindowLongPtr(_pinnedHandle, NativeMethods.GWL_EXSTYLE, new IntPtr(restoreStyle));
                NativeMethods.SetWindowPos(
                    _pinnedHandle,
                    NativeMethods.HWND_NOTOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

                NativeMethods.RedrawWindow(
                    _pinnedHandle,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_FRAME | NativeMethods.RDW_ALLCHILDREN);

                NativeMethods.RedrawWindow(
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_ERASE | NativeMethods.RDW_ALLCHILDREN | NativeMethods.RDW_UPDATENOW);
            }

            _originalExStyle = 0;
            _pinnedHandle = IntPtr.Zero;
            PinnedTitle = "";
            _tracker.Stop();
            if (_border != null)
            {
                _border.Close();
                _border = null;
            }
            if (!silent)
            {
                Notify(LocalizationService.Get("Pinner_Unpinned"));
            }
            StateChanged?.Invoke();
        }

        public void ForceClearAll()
        {
            Unpin(silent: true);
            if (_border != null)
            {
                _border.Close();
                _border = null;
            }
            NativeMethods.RedrawWindow(
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_ERASE | NativeMethods.RDW_ALLCHILDREN | NativeMethods.RDW_UPDATENOW);
            StateChanged?.Invoke();
            Notify(LocalizationService.Get("Pinner_ForceCleared"));
        }

        public void ApplySettings()
        {
            if (!IsPinned || !NativeMethods.IsWindow(_pinnedHandle))
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            long style = NativeMethods.GetWindowLongPtr(_pinnedHandle, NativeMethods.GWL_EXSTYLE).ToInt64();
            style |= NativeMethods.WS_EX_TOPMOST;

            int opacity = Math.Clamp(settings.PinnedOpacity, 20, 100);
            if (opacity < 100 || settings.PinnedClickThrough)
            {
                style |= NativeMethods.WS_EX_LAYERED;
            }

            if (settings.PinnedClickThrough)
            {
                style |= NativeMethods.WS_EX_TRANSPARENT;
            }
            else
            {
                style &= ~(long)NativeMethods.WS_EX_TRANSPARENT;
            }

            NativeMethods.SetWindowLongPtr(_pinnedHandle, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
            byte alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255.0 / 100.0), 25, 255);
            NativeMethods.SetLayeredWindowAttributes(_pinnedHandle, 0, alpha, NativeMethods.LWA_ALPHA);

            if (settings.ShowPinnedBorder)
            {
                ShowBorder();
            }
            else if (_border != null)
            {
                _border.Close();
                _border = null;
            }
            StateChanged?.Invoke();
        }

        private void ShowBorder()
        {
            if (_border == null)
            {
                _border = new PinnedBorderWindow();
                _border.Show();
            }
            else if (!_border.IsVisible)
            {
                _border.Show();
            }

            TrackBorder();
        }

        private void TrackBorder()
        {
            if (_pinnedHandle == IntPtr.Zero || !NativeMethods.IsWindow(_pinnedHandle))
            {
                Unpin(silent: true);
                return;
            }

            if (!NativeMethods.IsWindowVisible(_pinnedHandle) || NativeMethods.IsIconic(_pinnedHandle))
            {
                if (_border != null)
                {
                    _border.Visibility = Visibility.Hidden;
                }
                return;
            }

            if (!SettingsService.Instance.Current.ShowPinnedBorder)
            {
                if (_border != null)
                {
                    _border.Close();
                    _border = null;
                }
                return;
            }

            var bounds = ScreenCaptureService.GetWindowBounds(_pinnedHandle);
            if (bounds.Width < 2 || bounds.Height < 2)
            {
                return;
            }

            if (_border == null)
            {
                ShowBorder();
            }

            if (_border != null)
            {
                _border.Visibility = Visibility.Visible;
                _border.SetBounds(bounds);
            }
        }

        public void RestoreForExit()
        {
            Unpin(silent: true);
        }

        private static void Notify(string message)
        {
            (Application.Current as App)?.ShowToast(message);
        }

        public void Dispose()
        {
            RestoreForExit();
        }
    }
}
