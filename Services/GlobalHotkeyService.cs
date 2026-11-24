using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;

namespace CryoUtils.Services
{
    public sealed class GlobalHotkeyService : IDisposable
    {
        private static GlobalHotkeyService? _instance;
        public static GlobalHotkeyService Instance => _instance ??= new GlobalHotkeyService();

        public const int CaptureHotkeyId = 9201;
        public const int ColorPickerHotkeyId = 9202;
        public const int WindowPinnerHotkeyId = 9203;
        public const int PlainPasteHotkeyId = 9204;
        public const int ClipboardHistoryHotkeyId = 9205;

        private readonly Dictionary<int, Registration> _registrations = new();
        private HwndSource? _source;
        private bool _disposed;

        public event Action<int>? HotkeyPressed;

        private sealed class Registration
        {
            public string Modifiers = "";
            public string Key = "";
            public bool Registered;
            public string Error = "";
        }

        public void Initialize()
        {
            if (_source != null)
            {
                return;
            }

            var parameters = new HwndSourceParameters("CryoUtilsHotkeys")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = 0,
                ParentWindow = new IntPtr(-3)
            };

            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        public bool Register(int id, string? modifiers, string? key, out string error)
        {
            Initialize();
            Unregister(id);
            error = "";
            if (_source == null)
            {
                error = "No message window.";
                return false;
            }

            string safeModifiers = modifiers ?? "";
            string safeKey = key ?? "";
            if (string.IsNullOrWhiteSpace(safeKey))
            {
                error = "No key.";
                return false;
            }

            uint fsModifiers = 0;
            if (safeModifiers.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) fsModifiers |= NativeMethods.MOD_CONTROL;
            if (safeModifiers.Contains("Shift", StringComparison.OrdinalIgnoreCase)) fsModifiers |= NativeMethods.MOD_SHIFT;
            if (safeModifiers.Contains("Alt", StringComparison.OrdinalIgnoreCase)) fsModifiers |= NativeMethods.MOD_ALT;
            if (safeModifiers.Contains("Win", StringComparison.OrdinalIgnoreCase)) fsModifiers |= NativeMethods.MOD_WIN;
            fsModifiers |= NativeMethods.MOD_NOREPEAT;

            uint vk = ResolveVirtualKey(safeKey);
            if (vk == 0)
            {
                error = "Unknown key.";
                return false;
            }

            bool ok = NativeMethods.RegisterHotKey(_source.Handle, id, fsModifiers, vk);
            int lastError = ok ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            _registrations[id] = new Registration
            {
                Modifiers = safeModifiers,
                Key = safeKey,
                Registered = ok,
                Error = ok ? "" : (lastError == 1409 ? "conflict" : $"win32:{lastError}")
            };

            Debug.WriteLine($"RegisterHotKey {id} {safeModifiers}+{safeKey}: {ok}");
            error = _registrations[id].Error;
            return ok;
        }

        public void Unregister(int id)
        {
            if (_source == null)
            {
                return;
            }

            if (_registrations.TryGetValue(id, out var registration) && registration.Registered)
            {
                NativeMethods.UnregisterHotKey(_source.Handle, id);
            }
            _registrations.Remove(id);
        }

        public void UnregisterAll()
        {
            foreach (int id in new List<int>(_registrations.Keys))
            {
                Unregister(id);
            }
        }

        public bool IsRegistered(int id) =>
            _registrations.TryGetValue(id, out var registration) && registration.Registered;

        public string GetStatus(int id)
        {
            if (!_registrations.TryGetValue(id, out var registration))
            {
                return "";
            }

            return registration.Registered ? "" : registration.Error;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                HotkeyPressed?.Invoke(id);
                handled = true;
            }

            return IntPtr.Zero;
        }

        public static bool TryCaptureShortcut(
            Key rawKey, Key systemKey, ModifierKeys modifiers,
            out string modifiersToken, out string keyToken)
        {
            modifiersToken = "";
            keyToken = "";

            Key actualKey = rawKey == Key.System ? systemKey : rawKey;
            if (IsModifierOnlyKey(actualKey))
            {
                return false;
            }

            bool isFunctionKey = actualKey >= Key.F1 && actualKey <= Key.F12;
            bool isLetterKey = actualKey >= Key.A && actualKey <= Key.Z;
            bool isDigitKey = actualKey >= Key.D0 && actualKey <= Key.D9;
            bool isPrintScreen = actualKey == Key.PrintScreen;
            bool isPause = actualKey == Key.Pause;
            bool isInsert = actualKey == Key.Insert;
            bool isHome = actualKey == Key.Home;
            bool isEnd = actualKey == Key.End;
            bool isPageUp = actualKey == Key.PageUp;
            bool isPageDown = actualKey == Key.PageDown;

            if (!isFunctionKey && !isLetterKey && !isDigitKey && !isPrintScreen &&
                !isPause && !isInsert && !isHome && !isEnd && !isPageUp && !isPageDown)
            {
                return false;
            }

            var parts = new List<string>(4);
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            if (!isFunctionKey && !isPrintScreen && !isHome && parts.Count == 0)
            {
                return false;
            }

            modifiersToken = string.Join("+", parts);
            keyToken = actualKey.ToString();
            return true;
        }

        public static string FormatShortcutDisplay(string? modifiers, string? key)
        {
            var parts = new List<string>(5);
            string safeMods = modifiers ?? "";
            if (safeMods.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) parts.Add("Ctrl");
            if (safeMods.Contains("Alt", StringComparison.OrdinalIgnoreCase)) parts.Add("Alt");
            if (safeMods.Contains("Shift", StringComparison.OrdinalIgnoreCase)) parts.Add("Shift");
            if (safeMods.Contains("Win", StringComparison.OrdinalIgnoreCase)) parts.Add("Win");

            string cleanKey = NormalizeKeyLabel(key);
            if (!string.IsNullOrEmpty(cleanKey))
            {
                parts.Add(cleanKey);
            }

            return string.Join(" + ", parts);
        }

        private static string NormalizeKeyLabel(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            string trimmed = key.Trim();
            if (trimmed.Length == 2 && trimmed[0] == 'D' && char.IsDigit(trimmed[1]))
            {
                return trimmed[1].ToString();
            }
            if (string.Equals(trimmed, "PrintScreen", StringComparison.OrdinalIgnoreCase))
            {
                return "PrintScreen";
            }

            return trimmed.ToUpperInvariant();
        }

        public static uint ResolveVirtualKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return 0;
            }

            string trimmed = key.Trim();
            if (trimmed.Length == 1 && char.IsDigit(trimmed[0]))
            {
                return (uint)('0' + (trimmed[0] - '0'));
            }

            if (trimmed.Length == 2 && trimmed[0] == 'D' && char.IsDigit(trimmed[1]))
            {
                return (uint)('0' + (trimmed[1] - '0'));
            }

            if (string.Equals(trimmed, "PrintScreen", StringComparison.OrdinalIgnoreCase))
            {
                return 0x2C;
            }

            if (Enum.TryParse<Keys>(trimmed, true, out var keyVal) && keyVal != Keys.None)
            {
                return (uint)keyVal;
            }

            if (trimmed.Length == 1 && char.IsAsciiLetter(trimmed[0]))
            {
                return (uint)char.ToUpperInvariant(trimmed[0]);
            }

            return 0;
        }

        private static bool IsModifierOnlyKey(Key key)
        {
            return key == Key.None ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin ||
                key == Key.System || key == Key.DeadCharProcessed;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            UnregisterAll();
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source.Dispose();
                _source = null;
            }
        }
    }
}
