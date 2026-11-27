using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CryoUtils.Services.Recording
{
    internal sealed class CursorEffectTracker : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private static CursorEffectTracker? _active;
        private static readonly LowLevelHookProc HookProc = HookCallback;
        private static readonly LowLevelHookProc KeyHookProc = KeyHookCallback;

        private readonly bool _trackKeys;
        private readonly object _gate = new();
        private readonly List<ClickPulse> _clicks = new();
        private readonly ManualResetEventSlim _ready = new(false);
        private readonly CancellationTokenSource _stop = new();
        private Thread? _thread;
        private IntPtr _hook;
        private IntPtr _keyHook;
        private string _lastKeyText = "";
        private long _lastKeyMs;

        public CursorEffectTracker(bool trackKeys = false)
        {
            _trackKeys = trackKeys;
        }

        public bool IsAvailable { get; private set; }

        public void Start()
        {
            if (_thread != null)
            {
                return;
            }

            _active = this;
            _thread = new Thread(HookThread)
            {
                IsBackground = true,
                Name = "CryoUtils click effects"
            };
            _thread.Start();
            _ready.Wait(800);
        }

        private void HookThread()
        {
            try
            {
                _hook = SetWindowsHookEx(WH_MOUSE_LL, HookProc, GetModuleHandle(null), 0);
                if (_trackKeys)
                {
                    _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, KeyHookProc, GetModuleHandle(null), 0);
                }
                if (_hook == IntPtr.Zero && _keyHook == IntPtr.Zero)
                {
                    return;
                }

                IsAvailable = true;
                _ready.Set();
                while (!_stop.IsCancellationRequested)
                {
                    if (GetMessage(out var message, IntPtr.Zero, 0, 0) <= 0)
                    {
                        break;
                    }

                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            catch
            {
            }
            finally
            {
                if (_hook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
                if (_keyHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_keyHook);
                    _keyHook = IntPtr.Zero;
                }
                IsAvailable = false;
                _ready.Set();
            }
        }

        private static IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && _active != null)
            {
                int message = wParam.ToInt32();
                if (message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    _active.Record(info.pt);
                }
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private static IntPtr KeyHookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && _active != null)
            {
                int message = wParam.ToInt32();
                if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                    bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                    bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                    bool win = ((GetAsyncKeyState(0x5B) | GetAsyncKeyState(0x5C)) & 0x8000) != 0;
                    string label = FormatKeyLabel(vkCode, ctrl, shift, alt, win);
                    if (!string.IsNullOrEmpty(label))
                    {
                        _active.RecordKey(label);
                    }
                }
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private void RecordKey(string label)
        {
            lock (_gate)
            {
                _lastKeyText = label;
                _lastKeyMs = Environment.TickCount64;
            }
        }

        public (string Text, double Alpha) SnapshotKeyHud()
        {
            lock (_gate)
            {
                if (string.IsNullOrEmpty(_lastKeyText))
                {
                    return ("", 0);
                }

                long age = Environment.TickCount64 - _lastKeyMs;
                if (age < 0 || age > 1100)
                {
                    _lastKeyText = "";
                    return ("", 0);
                }

                double alpha = age <= 750 ? 1.0 : Math.Max(0, 1.0 - (age - 750) / 350.0);
                return (_lastKeyText, alpha);
            }
        }

        internal static string FormatKeyLabel(int vk, bool ctrl, bool shift, bool alt, bool win)
        {
            // Ignore bare modifier key-down repeats.
            if (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)
            {
                return "";
            }

            string keyName = vk switch
            {
                0x08 => "Backspace",
                0x09 => "Tab",
                0x0D => "Enter",
                0x1B => "Esc",
                0x20 => "Space",
                0x21 => "PgUp",
                0x22 => "PgDn",
                0x23 => "End",
                0x24 => "Home",
                0x25 => "Left",
                0x26 => "Up",
                0x27 => "Right",
                0x28 => "Down",
                0x2C => "PrtSc",
                0x2D => "Ins",
                0x2E => "Del",
                >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
                >= 0x30 and <= 0x39 => ((char)vk).ToString(),
                >= 0x41 and <= 0x5A => ((char)vk).ToString(),
                _ => ""
            };

            if (string.IsNullOrEmpty(keyName))
            {
                return "";
            }

            var parts = new List<string>(5);
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Win");
            parts.Add(keyName);
            return string.Join(" + ", parts);
        }

        private void Record(NativeMethods.POINT point)
        {
            lock (_gate)
            {
                long now = Environment.TickCount64;
                _clicks.RemoveAll(click => now - click.StartedMs > 900);
                _clicks.Add(new ClickPulse
                {
                    X = point.X,
                    Y = point.Y,
                    StartedMs = now
                });
            }
        }

        public IReadOnlyList<ClickPulse> Snapshot()
        {
            lock (_gate)
            {
                long now = Environment.TickCount64;
                _clicks.RemoveAll(click => now - click.StartedMs > 900);
                return _clicks.ToArray();
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            if (_hook != IntPtr.Zero || _keyHook != IntPtr.Zero)
            {
                PostThreadMessage((uint)(_thread?.ManagedThreadId ?? 0), 0x0012, IntPtr.Zero, IntPtr.Zero);
            }
            _thread?.Join(1000);
            _active = null;
            if (_thread == null || !_thread.IsAlive)
            {
                _stop.Dispose();
                _ready.Dispose();
            }
        }

        private delegate IntPtr LowLevelHookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public NativeMethods.POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public NativeMethods.POINT pt;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG message, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG message);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? moduleName);
    }
}
