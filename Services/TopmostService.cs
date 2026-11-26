using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CryoUtils.Models;
using CryoUtils.Views;

namespace CryoUtils.Services
{
    public sealed class PinnedWindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public int Opacity { get; set; } = 100;
        public bool ClickThrough { get; set; }

        public string Display => $"{Title}  ·  {Opacity}%  ·  {ProcessName}";
    }

    public sealed class TopmostService : IDisposable
    {
        private sealed class PinnedState
        {
            public IntPtr Handle { get; init; }
            public long OriginalStyle { get; init; }
            public string Title { get; init; } = "";
            public string ProcessName { get; init; } = "";
            public int Opacity { get; set; } = 100;
            public bool ClickThrough { get; set; }
        }

        internal sealed class SessionRecord
        {
            public SessionRecord()
            {
            }

            public string Title { get; set; } = "";
            public string ProcessName { get; set; } = "";
            public int Opacity { get; set; } = 100;
            public bool ClickThrough { get; set; }
        }

        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoUtils");
        private static readonly string SessionFile = Path.Combine(DataFolder, "pinned-windows.json");

        private static TopmostService? _instance;
        public static TopmostService Instance => _instance ??= new TopmostService();

        private readonly DispatcherTimer _tracker;
        private readonly List<PinnedState> _pinned = new();
        private PinnedBorderWindow? _border;

        public event Action? StateChanged;

        public IntPtr PinnedHandle => _pinned.Count > 0 ? _pinned[0].Handle : IntPtr.Zero;
        public bool IsPinned => _pinned.Count > 0;
        public string PinnedTitle => _pinned.Count > 0 ? _pinned[0].Title : "";
        public int PinnedCount => _pinned.Count;
        public IReadOnlyList<PinnedWindowInfo> PinnedWindows =>
            _pinned.Select(item => new PinnedWindowInfo
            {
                Handle = item.Handle,
                Title = item.Title,
                ProcessName = item.ProcessName,
                Opacity = item.Opacity,
                ClickThrough = item.ClickThrough
            }).ToList();

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

            var existing = _pinned.FirstOrDefault(item => item.Handle == foreground);
            if (existing != null)
            {
                UnpinWindow(foreground);
                return;
            }

            if (!IsValidTargetWindow(foreground))
            {
                Notify(LocalizationService.Get("Pinner_NoWindow"));
                return;
            }

            PinWindow(foreground, notify: true, saveSession: true);
        }

        private bool PinWindow(IntPtr handle, bool notify, bool saveSession)
        {
            if (_pinned.Any(item => item.Handle == handle))
            {
                return false;
            }

            long originalStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            var settings = SettingsService.Instance.Current;
            var state = new PinnedState
            {
                Handle = handle,
                OriginalStyle = originalStyle,
                Title = GetWindowTitle(handle),
                ProcessName = GetProcessName(handle),
                Opacity = Math.Clamp(settings.PinnedOpacity, 20, 100),
                ClickThrough = settings.PinnedClickThrough
            };
            _pinned.Add(state);
            ApplyWindowStyle(state);

            if (_pinned.Count == 1)
            {
                if (settings.ShowPinnedBorder)
                {
                    ShowBorder();
                }
                if (settings.PinnedBorderSound && notify)
                {
                    SystemSounds.Asterisk.Play();
                }
            }

            _tracker.Start();
            if (saveSession)
            {
                SaveSession();
            }
            if (notify)
            {
                Notify(LocalizationService.Format("Pinner_Pinned", state.Title));
            }
            StateChanged?.Invoke();
            return true;
        }

        private static void ApplyWindowStyle(PinnedState state)
        {
            if (!NativeMethods.IsWindow(state.Handle))
            {
                return;
            }

            long value = state.OriginalStyle | NativeMethods.WS_EX_TOPMOST;
            if (state.Opacity < 100 || state.ClickThrough)
            {
                value |= NativeMethods.WS_EX_LAYERED;
            }
            if (state.ClickThrough)
            {
                value |= NativeMethods.WS_EX_TRANSPARENT;
            }
            else
            {
                value &= ~(long)NativeMethods.WS_EX_TRANSPARENT;
            }

            NativeMethods.SetWindowLongPtr(state.Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(value));
            if (state.Opacity < 100 || state.ClickThrough)
            {
                byte alpha = (byte)Math.Clamp((int)Math.Round(state.Opacity * 255.0 / 100.0), 25, 255);
                NativeMethods.SetLayeredWindowAttributes(state.Handle, 0, alpha, NativeMethods.LWA_ALPHA);
            }

            NativeMethods.SetWindowPos(
                state.Handle,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
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

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == Environment.ProcessId)
            {
                return false;
            }

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

            var bounds = ScreenCaptureService.GetWindowBounds(hwnd);
            if (bounds.Width < 120 || bounds.Height < 80)
            {
                return false;
            }

            string title = ScreenCaptureService.GetWindowTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            string processName = GetProcessName(hwnd);
            bool excluded = SettingsService.Instance.Current.PinnerExclusions.Any(filter =>
                MatchesFilter(filter, title) || MatchesFilter(filter, processName));
            return !excluded;
        }

        private static bool MatchesFilter(string filter, string value)
        {
            return !string.IsNullOrWhiteSpace(filter) &&
                   value.Contains(filter.Trim(), StringComparison.CurrentCultureIgnoreCase);
        }

        private static string GetProcessName(IntPtr hwnd)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                using var process = Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch
            {
                return "";
            }
        }

        public void Unpin(bool silent = false)
        {
            if (_pinned.Count == 0)
            {
                return;
            }

            foreach (var state in _pinned.ToList())
            {
                RestoreWindow(state);
            }
            _pinned.Clear();
            CloseBorder();
            _tracker.Stop();
            SaveSession();
            if (!silent)
            {
                Notify(LocalizationService.Get("Pinner_Unpinned"));
            }
            StateChanged?.Invoke();
        }

        public void UnpinWindow(IntPtr handle, bool silent = false)
        {
            var state = _pinned.FirstOrDefault(item => item.Handle == handle);
            if (state == null)
            {
                return;
            }

            RestoreWindow(state);
            _pinned.Remove(state);
            if (_pinned.Count == 0)
            {
                CloseBorder();
                _tracker.Stop();
            }
            else
            {
                ShowBorder();
            }
            SaveSession();
            if (!silent)
            {
                Notify(LocalizationService.Get("Pinner_Unpinned"));
            }
            StateChanged?.Invoke();
        }

        private static void RestoreWindow(PinnedState state)
        {
            if (!NativeMethods.IsWindow(state.Handle))
            {
                return;
            }

            NativeMethods.SetLayeredWindowAttributes(state.Handle, 0, 255, NativeMethods.LWA_ALPHA);
            long currentStyle = NativeMethods.GetWindowLongPtr(state.Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            long restoreStyle = state.OriginalStyle != 0 ? state.OriginalStyle : currentStyle;
            restoreStyle &= ~(long)NativeMethods.WS_EX_TOPMOST;
            restoreStyle &= ~(long)NativeMethods.WS_EX_TRANSPARENT;
            if ((state.OriginalStyle & NativeMethods.WS_EX_LAYERED) == 0)
            {
                restoreStyle &= ~(long)NativeMethods.WS_EX_LAYERED;
            }

            NativeMethods.SetWindowLongPtr(state.Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(restoreStyle));
            NativeMethods.SetWindowPos(
                state.Handle,
                NativeMethods.HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
            NativeMethods.RedrawWindow(
                state.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_FRAME | NativeMethods.RDW_ALLCHILDREN);
        }

        public void ForceClearAll()
        {
            Unpin(silent: true);
            CloseBorder();
            NativeMethods.RedrawWindow(
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_ERASE | NativeMethods.RDW_ALLCHILDREN | NativeMethods.RDW_UPDATENOW);
            Notify(LocalizationService.Get("Pinner_ForceCleared"));
            StateChanged?.Invoke();
        }

        public void ApplySettings()
        {
            var settings = SettingsService.Instance.Current;
            foreach (var state in _pinned)
            {
                state.Opacity = Math.Clamp(settings.PinnedOpacity, 20, 100);
                state.ClickThrough = settings.PinnedClickThrough;
                ApplyWindowStyle(state);
            }
            if (_pinned.Count == 0)
            {
                return;
            }

            if (settings.ShowPinnedBorder)
            {
                ShowBorder();
            }
            else
            {
                CloseBorder();
            }
            SaveSession();
            StateChanged?.Invoke();
        }

        public void SetWindowAppearance(IntPtr handle, int opacity, bool clickThrough)
        {
            var state = _pinned.FirstOrDefault(item => item.Handle == handle);
            if (state == null)
            {
                return;
            }

            state.Opacity = Math.Clamp(opacity, 20, 100);
            state.ClickThrough = clickThrough;
            ApplyWindowStyle(state);
            SaveSession();
            StateChanged?.Invoke();
        }

        public void RestoreSavedPins()
        {
            if (!SettingsService.Instance.Current.PinnerRestoreEnabled || File.Exists(SessionFile) == false)
            {
                return;
            }

            List<SessionRecord> records;
            try
            {
                records = JsonSerializer.Deserialize<List<SessionRecord>>(File.ReadAllText(SessionFile))
                    ?? new List<SessionRecord>();
            }
            catch
            {
                return;
            }

            if (records.Count == 0)
            {
                return;
            }

            var found = new List<(IntPtr Handle, SessionRecord Record)>();
            NativeMethods.EnumWindows((handle, _) =>
            {
                if (found.Count >= 20 || !IsValidTargetWindow(handle))
                {
                    return true;
                }

                string title = GetWindowTitle(handle);
                string process = GetProcessName(handle);
                var match = records.FirstOrDefault(record =>
                    string.Equals(record.Title, title, StringComparison.CurrentCultureIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(record.ProcessName) ||
                     string.Equals(record.ProcessName, process, StringComparison.CurrentCultureIgnoreCase)));
                if (match != null && found.All(item => item.Handle != handle))
                {
                    found.Add((handle, match));
                }
                return true;
            }, IntPtr.Zero);

            foreach (var item in found)
            {
                var settings = SettingsService.Instance.Current;
                settings.PinnedOpacity = item.Record.Opacity;
                settings.PinnedClickThrough = item.Record.ClickThrough;
                if (PinWindow(item.Handle, notify: false, saveSession: false))
                {
                    var state = _pinned.FirstOrDefault(pin => pin.Handle == item.Handle);
                    if (state != null)
                    {
                        state.Opacity = item.Record.Opacity;
                        state.ClickThrough = item.Record.ClickThrough;
                        ApplyWindowStyle(state);
                    }
                }
            }

            if (found.Count > 0)
            {
                SaveSession();
                Notify(LocalizationService.Format("Pinner_Restored", found.Count));
            }
        }

        private void ShowBorder()
        {
            if (_pinned.Count == 0)
            {
                return;
            }

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

        private void CloseBorder()
        {
            if (_border != null)
            {
                _border.Close();
                _border = null;
            }
        }

        private void TrackBorder()
        {
            if (_pinned.Count == 0)
            {
                _tracker.Stop();
                CloseBorder();
                return;
            }

            var primary = _pinned[0];
            if (!NativeMethods.IsWindow(primary.Handle))
            {
                UnpinWindow(primary.Handle, silent: true);
                return;
            }

            if (!NativeMethods.IsWindowVisible(primary.Handle) || NativeMethods.IsIconic(primary.Handle))
            {
                if (_border != null)
                {
                    _border.Visibility = Visibility.Hidden;
                }
                return;
            }

            if (!SettingsService.Instance.Current.ShowPinnedBorder)
            {
                CloseBorder();
                return;
            }

            var bounds = ScreenCaptureService.GetWindowBounds(primary.Handle);
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

        public static void ReleaseSavedTopmosts()
        {
            try
            {
                if (!File.Exists(SessionFile))
                {
                    return;
                }

                var records = JsonSerializer.Deserialize<List<SessionRecord>>(File.ReadAllText(SessionFile))
                    ?? new List<SessionRecord>();
                if (records.Count == 0)
                {
                    return;
                }

                var handles = new List<IntPtr>();
                NativeMethods.EnumWindows((handle, _) =>
                {
                    if (!NativeMethods.IsWindowVisible(handle) || !IsValidTargetWindow(handle))
                    {
                        return true;
                    }

                    string title = GetWindowTitle(handle);
                    string process = GetProcessName(handle);
                    if (records.Any(record =>
                            string.Equals(record.Title, title, StringComparison.Ordinal) &&
                            string.Equals(record.ProcessName, process, StringComparison.OrdinalIgnoreCase)))
                    {
                        handles.Add(handle);
                    }

                    return true;
                }, IntPtr.Zero);

                foreach (var handle in handles.Distinct())
                {
                    NativeMethods.SetWindowPos(
                        handle,
                        NativeMethods.HWND_NOTOPMOST,
                        0,
                        0,
                        0,
                        0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                }

                File.Delete(SessionFile);
            }
            catch
            {
            }
        }

        private static void SaveSession()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                var records = Instance._pinned.Select(state => new SessionRecord
                {
                    Title = state.Title,
                    ProcessName = state.ProcessName,
                    Opacity = state.Opacity,
                    ClickThrough = state.ClickThrough
                }).ToList();
                string json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
                string tempPath = SessionFile + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, SessionFile, overwrite: true);
            }
            catch
            {
            }
        }

        public void RestoreForExit()
        {
            // Keep the session file so the next launch can re-pin matching windows.
            string? session = null;
            try
            {
                if (File.Exists(SessionFile))
                {
                    session = File.ReadAllText(SessionFile);
                }
            }
            catch
            {
            }

            Unpin(silent: true);

            try
            {
                if (session != null)
                {
                    File.WriteAllText(SessionFile, session);
                }
            }
            catch
            {
            }
        }

        private static string GetWindowTitle(IntPtr handle) =>
            ScreenCaptureService.GetWindowTitle(handle);

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
