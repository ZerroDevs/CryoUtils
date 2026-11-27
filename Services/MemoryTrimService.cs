using System;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace CryoUtils.Services
{
    /// <summary>
    /// Coalesces working-set trims into a single off-UI compacting GC + EmptyWorkingSet pass
    /// while deferring during active UI interactions.
    /// </summary>
    public static class MemoryTrimService
    {
        public const int DefaultDelayMs = 1500;
        public const int LoadedDelayMs = 2500;
        public static readonly TimeSpan InteractionCooldown = TimeSpan.FromMilliseconds(1200);
        public static readonly TimeSpan MinDeactivatedInterval = TimeSpan.FromSeconds(5);

        private static readonly object Gate = new();
        private static Timer? _timer;
        private static int _running;
        private static int _pendingGeneration;
        private static long _lastTrimCompletedUtcTicks;
        private static long _suppressUntilUtcTicks;
        private static bool _shuttingDown;

        internal static Action? TrimExecutorForTest { get; set; }
        internal static int ScheduledCountForTest { get; private set; }
        internal static int ExecutedCountForTest { get; private set; }

        public static bool HasPendingTrim
        {
            get
            {
                lock (Gate)
                {
                    return _timer != null;
                }
            }
        }

        /// <summary>
        /// Temporarily defers working-set trims during brief UI bursts (such as tab switches or menu navigation).
        /// </summary>
        public static void PauseForInteraction(TimeSpan? duration = null)
        {
            TimeSpan effective = duration ?? InteractionCooldown;
            if (effective <= TimeSpan.Zero)
            {
                return;
            }

            long untilTicks = DateTime.UtcNow.Add(effective).Ticks;
            while (true)
            {
                long current = Interlocked.Read(ref _suppressUntilUtcTicks);
                if (untilTicks <= current)
                {
                    break;
                }

                if (Interlocked.CompareExchange(ref _suppressUntilUtcTicks, untilTicks, current) == current)
                {
                    break;
                }
            }

            lock (Gate)
            {
                if (_shuttingDown || _timer == null)
                {
                    return;
                }

                long remainingTicks = Interlocked.Read(ref _suppressUntilUtcTicks) - DateTime.UtcNow.Ticks;
                if (remainingTicks <= 0)
                {
                    return;
                }

                int pushedDelayMs = Math.Max(50, (int)Math.Ceiling(TimeSpan.FromTicks(remainingTicks).TotalMilliseconds) + 100);
                int generation = ++_pendingGeneration;
                _timer.Dispose();
                _timer = new Timer(OnTimerElapsed, generation, pushedDelayMs, Timeout.Infinite);
            }
        }

        public static void ScheduleTrim(int delayMs = DefaultDelayMs, bool skipIfRecentlyTrimmed = false)
        {
            if (skipIfRecentlyTrimmed)
            {
                long lastTicks = Interlocked.Read(ref _lastTrimCompletedUtcTicks);
                if (lastTicks > 0 && DateTime.UtcNow.Ticks - lastTicks < MinDeactivatedInterval.Ticks)
                {
                    return;
                }
            }

            int effectiveDelay = Math.Max(50, delayMs);

            lock (Gate)
            {
                if (_shuttingDown)
                {
                    return;
                }

                ScheduledCountForTest++;
                int generation = ++_pendingGeneration;
                _timer?.Dispose();
                _timer = new Timer(OnTimerElapsed, generation, effectiveDelay, Timeout.Infinite);
            }
        }

        public static void Cancel(bool shuttingDown = false)
        {
            Timer? timer;
            lock (Gate)
            {
                if (shuttingDown)
                {
                    _shuttingDown = true;
                }

                _pendingGeneration++;
                timer = _timer;
                _timer = null;
            }

            timer?.Dispose();
        }

        internal static void ResetForTest()
        {
            Cancel(shuttingDown: false);
            Interlocked.Exchange(ref _suppressUntilUtcTicks, 0);
            Interlocked.Exchange(ref _lastTrimCompletedUtcTicks, 0);
            lock (Gate)
            {
                _shuttingDown = false;
                ScheduledCountForTest = 0;
                ExecutedCountForTest = 0;
            }
            TrimExecutorForTest = null;
        }

        private static void OnTimerElapsed(object? state)
        {
            int firedGeneration = state is int g ? g : -1;
            Timer? timerToDispose;

            lock (Gate)
            {
                if (_shuttingDown || firedGeneration != _pendingGeneration)
                {
                    return;
                }

                timerToDispose = _timer;
                _timer = null;
            }

            timerToDispose?.Dispose();
            _ = Task.Run(TrimNow);
        }

        public static void TrimNow()
        {
            if (IsShuttingDown() || Interlocked.Exchange(ref _running, 1) == 1)
            {
                return;
            }

            try
            {
                if (IsShuttingDown())
                {
                    return;
                }

                long remainingTicks = Interlocked.Read(ref _suppressUntilUtcTicks) - DateTime.UtcNow.Ticks;
                if (remainingTicks > 0)
                {
                    int deferredMs = Math.Max(50, (int)Math.Ceiling(TimeSpan.FromTicks(remainingTicks).TotalMilliseconds) + 100);
                    ScheduleTrim(deferredMs);
                    return;
                }

                ExecutedCountForTest++;
                Interlocked.Exchange(ref _lastTrimCompletedUtcTicks, DateTime.UtcNow.Ticks);

                Action? custom = TrimExecutorForTest;
                if (custom != null)
                {
                    custom();
                    return;
                }

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                if (IsShuttingDown())
                {
                    return;
                }

                NativeMethods.EmptyWorkingSet(NativeMethods.GetCurrentProcess());
                Interlocked.Exchange(ref _lastTrimCompletedUtcTicks, DateTime.UtcNow.Ticks);
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private static bool IsShuttingDown()
        {
            lock (Gate)
            {
                return _shuttingDown;
            }
        }
    }
}
