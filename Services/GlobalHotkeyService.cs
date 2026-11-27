using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

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
        public const int WorkbenchHotkeyId = 9206;
        public const int ReplayHotkeyId = 9207;
        public const int LiveDrawHotkeyId = 9208;
        public const int RepeatLastRegionHotkeyId = 9209;
        public const int ActiveWindowSnapHotkeyId = 9210;

        private readonly Dictionary<int, Registration> _registrations = new();
        private HotkeyMessageWindow? _window;
        private bool _disposed;

        public event Action<int>? HotkeyPressed;

        private sealed class Registration
        {
            public string Modifiers = "";
            public string Key = "";
            public bool Registered;
            public string Error = "";
        }

        private sealed class HotkeyMessageWindow : IDisposable
        {
            private readonly Action<int> _onHotkey;
            private readonly NativeMethods.WndProcDelegate _wndProc;
            private readonly string _className;
            private readonly IntPtr _hInstance;

            public IntPtr Handle { get; private set; }

            public HotkeyMessageWindow(Action<int> onHotkey)
            {
                _onHotkey = onHotkey;
                _wndProc = WndProc;
                _hInstance = NativeMethods.GetModuleHandleW(null);
                _className = $"CryoUtils_Hotkeys_{Guid.NewGuid():N}";

                var wc = new NativeMethods.WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = _hInstance,
                    lpszClassName = _className
                };

                if (NativeMethods.RegisterClassExW(ref wc) != 0)
                {
                    // HWND_MESSAGE avoids waking WPF/DirectX rendering when idle in tray.
                    Handle = NativeMethods.CreateWindowExW(
                        0,
                        _className,
                        "CryoUtilsHotkeys",
                        0,
                        0, 0, 0, 0,
                        NativeMethods.HWND_MESSAGE,
                        IntPtr.Zero,
                        _hInstance,
                        IntPtr.Zero);
                }
            }

            private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
            {
                if (msg == NativeMethods.WM_HOTKEY)
                {
                    _onHotkey(wParam.ToInt32());
                    return IntPtr.Zero;
                }

                return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
            }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                {
                    NativeMethods.DestroyWindow(Handle);
                    Handle = IntPtr.Zero;
                }

                NativeMethods.UnregisterClassW(_className, _hInstance);
            }
        }

        public void Initialize()
        {
            if (_window != null || _disposed)
            {
                return;
            }

            _window = new HotkeyMessageWindow(id => HotkeyPressed?.Invoke(id));
        }

        public bool Register(int id, string? modifiers, string? key, out string error)
        {
            Initialize();
            Unregister(id);
            error = "";
            if (_window == null || _window.Handle == IntPtr.Zero)
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

            bool ok = NativeMethods.RegisterHotKey(_window.Handle, id, fsModifiers, vk);
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
            if (_window == null || _window.Handle == IntPtr.Zero)
            {
                return;
            }

            if (_registrations.TryGetValue(id, out var registration) && registration.Registered)
            {
                NativeMethods.UnregisterHotKey(_window.Handle, id);
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

            if (string.Equals(trimmed, "PrintScreen", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Snapshot", StringComparison.OrdinalIgnoreCase))
            {
                return 0x2C;
            }

            if (string.Equals(trimmed, "Pause", StringComparison.OrdinalIgnoreCase))
            {
                return 0x13;
            }

            if (Enum.TryParse<Key>(trimmed, true, out var wpfKey) && wpfKey != Key.None)
            {
                int vk = KeyInterop.VirtualKeyFromKey(wpfKey);
                if (vk > 0)
                {
                    return (uint)vk;
                }
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
            if (_window != null)
            {
                _window.Dispose();
                _window = null;
            }
        }
    }
}
