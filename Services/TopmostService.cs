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

        public event Action? StateChanged;

        public IntPtr PinnedHandle => _pinnedHandle;
        public bool IsPinned => _pinnedHandle != IntPtr.Zero;
        public string PinnedTitle { get; private set; } = "";

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

            if (_pinnedHandle != IntPtr.Zero)
            {
                Unpin(silent: true);
            }

            IntPtr style = NativeMethods.GetWindowLongPtr(foreground, NativeMethods.GWL_EXSTYLE);
            long value = style.ToInt64();
            value |= NativeMethods.WS_EX_TOPMOST;
            NativeMethods.SetWindowLongPtr(foreground, NativeMethods.GWL_EXSTYLE, new IntPtr(value));
            NativeMethods.SetWindowPos(
                foreground,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

            _pinnedHandle = foreground;
            PinnedTitle = ScreenCaptureService.GetWindowTitle(foreground);
            if (string.IsNullOrWhiteSpace(PinnedTitle))
            {
                PinnedTitle = LocalizationService.Get("Common_Unknown");
            }

            if (SettingsService.Instance.Current.ShowPinnedBorder)
            {
                ShowBorder();
            }
            if (SettingsService.Instance.Current.PinnedBorderSound)
            {
                SystemSounds.Asterisk.Play();
            }

            _tracker.Start();
            Notify(LocalizationService.Format("Pinner_Pinned", PinnedTitle));
            StateChanged?.Invoke();
        }

        public void Unpin(bool silent = false)
        {
            if (_pinnedHandle != IntPtr.Zero && NativeMethods.IsWindow(_pinnedHandle))
            {
                IntPtr style = NativeMethods.GetWindowLongPtr(_pinnedHandle, NativeMethods.GWL_EXSTYLE);
                long value = style.ToInt64() & ~(long)NativeMethods.WS_EX_TOPMOST;
                NativeMethods.SetWindowLongPtr(_pinnedHandle, NativeMethods.GWL_EXSTYLE, new IntPtr(value));
                NativeMethods.SetWindowPos(
                    _pinnedHandle,
                    NativeMethods.HWND_NOTOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }

            _pinnedHandle = IntPtr.Zero;
            PinnedTitle = "";
            _tracker.Stop();
            _border?.Hide();
            if (!silent)
            {
                Notify(LocalizationService.Get("Pinner_Unpinned"));
            }
            StateChanged?.Invoke();
        }

        public void ApplySettings()
        {
            if (!IsPinned)
            {
                return;
            }

            if (SettingsService.Instance.Current.ShowPinnedBorder)
            {
                ShowBorder();
            }
            else
            {
                _border?.Hide();
            }
        }

        private void ShowBorder()
        {
            if (_border == null)
            {
                _border = new PinnedBorderWindow();
                _border.Show();
            }
            else
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

            if (_border == null || !SettingsService.Instance.Current.ShowPinnedBorder)
            {
                return;
            }

            if (!NativeMethods.IsWindowVisible(_pinnedHandle) || NativeMethods.IsIconic(_pinnedHandle))
            {
                _border.Visibility = Visibility.Hidden;
                return;
            }

            _border.Visibility = Visibility.Visible;
            var bounds = ScreenCaptureService.GetWindowBounds(_pinnedHandle);
            if (bounds.Width < 2 || bounds.Height < 2)
            {
                return;
            }

            _border.SetBounds(bounds);
        }

        public void RestoreForExit()
        {
            if (_pinnedHandle != IntPtr.Zero && NativeMethods.IsWindow(_pinnedHandle))
            {
                IntPtr style = NativeMethods.GetWindowLongPtr(_pinnedHandle, NativeMethods.GWL_EXSTYLE);
                long value = style.ToInt64() & ~(long)NativeMethods.WS_EX_TOPMOST;
                NativeMethods.SetWindowLongPtr(_pinnedHandle, NativeMethods.GWL_EXSTYLE, new IntPtr(value));
            }

            _pinnedHandle = IntPtr.Zero;
            _tracker.Stop();
            if (_border != null)
            {
                _border.Close();
                _border = null;
            }
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
