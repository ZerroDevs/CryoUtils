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
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private static CursorEffectTracker? _active;
        private static readonly LowLevelMouseProc HookProc = HookCallback;

        private readonly object _gate = new();
        private readonly List<ClickPulse> _clicks = new();
        private readonly ManualResetEventSlim _ready = new(false);
        private readonly CancellationTokenSource _stop = new();
        private Thread? _thread;
        private IntPtr _hook;

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
                if (_hook == IntPtr.Zero)
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
            if (_hook != IntPtr.Zero)
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

        private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

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
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

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
