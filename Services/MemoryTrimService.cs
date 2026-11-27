using System;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace CryoUtils.Services
{
    /// <summary>
    /// Reclaims unused managed heap and OS working-set pages after UI windows close or heavy capture jobs end.
    /// </summary>
    public static class MemoryTrimService
    {
        private static readonly object Gate = new();
        private static Timer? _timer;
        private static int _running;

        public static void ScheduleTrim(int delayMs = 600)
        {
            lock (Gate)
            {
                _timer?.Dispose();
                _timer = new Timer(_ => _ = Task.Run(TrimNow), null, Math.Max(50, delayMs), Timeout.Infinite);
            }
        }

        public static void Cancel()
        {
            lock (Gate)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        public static void TrimNow()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                return;
            }

            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                NativeMethods.EmptyWorkingSet(NativeMethods.GetCurrentProcess());
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }
    }
}
