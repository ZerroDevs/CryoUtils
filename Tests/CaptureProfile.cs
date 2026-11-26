using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CryoUtils.Services;
using CryoUtils.Services.Recording;

namespace CryoUtils.Tests
{
    internal static class CaptureProfile
    {
        public static int Run()
        {
            var monitor = ScreenCaptureService.GetMonitors().First();
            var region = new Int32Rect(
                monitor.Bounds.Left,
                monitor.Bounds.Top,
                monitor.Bounds.Width,
                monitor.Bounds.Height);
            Console.WriteLine(
                $"PROFILE monitor={region.Width}x{region.Height} at {monitor.Bounds.Left},{monitor.Bounds.Top}");

            using var animator = new ScreenAnimator();
            animator.Start();
            Thread.Sleep(500);
            ProfileReadback(monitor, region);
            Thread.Sleep(500);
            ProfileFullSession(region);
            return 0;
        }

        private static void ProfileReadback(MonitorInfo monitor, Int32Rect region)
        {
            const int warmSeconds = 2;
            const int seconds = 8;
            var intervals = new List<double>();
            var reads = new List<double>();
            var totals = new List<double>();
            long last = 0;
            using var engine = new WgcCaptureEngine(monitor.Handle, monitor.Bounds, region);
            engine.FrameArrived += (_, _, _) =>
            {
                long now = Stopwatch.GetTimestamp();
                if (last != 0)
                {
                    intervals.Add((now - last) * 1000.0 / Stopwatch.Frequency);
                }
                last = now;
                reads.Add(engine.LastReadTicks * 1000.0 / Stopwatch.Frequency);
                totals.Add(engine.LastTotalTicks * 1000.0 / Stopwatch.Frequency);
            };
            engine.Start();
            Thread.Sleep(warmSeconds * 1000);
            intervals.Clear();
            reads.Clear();
            totals.Clear();
            last = 0;
            Thread.Sleep(seconds * 1000);
            engine.Stop();

            double fps = (intervals.Count + (intervals.Count > 0 ? 1 : 0)) / Math.Max(0.001, seconds);
            Console.WriteLine(
                $"PROFILE wgc-readback steady frames={intervals.Count + 1} drops={engine.DroppedFrames} " +
                $"fps~{fps:F1}");
            PrintDistribution("wgc-readback interval ms", intervals);
            PrintDistribution("wgc-readback read ms", reads);
            PrintDistribution("wgc-readback total ms", totals);
        }

        private static void ProfileFullSession(Int32Rect region)
        {
            const int warmSeconds = 2;
            const int seconds = 8;
            var options = new RecordingOptions
            {
                Fps = 60,
                Resolution = "Source",
                Quality = RecorderQuality.Low,
                MaxSeconds = 60,
                TrimBeforeSave = false,
                RecordSystemAudio = false,
                RecordMicrophone = false
            };
            string? path = null;
            try
            {
                using var session = new RecordingSession(region, options);
                var watch = Stopwatch.StartNew();
                session.Start();
                bool usingWgc = session.IsUsingWgc;
                Thread.Sleep(warmSeconds * 1000);
                long warmFrames = session.Frames;
                var steady = Stopwatch.StartNew();
                Thread.Sleep(seconds * 1000);
                steady.Stop();
                var elapsed = session.Elapsed;
                long frames = session.Frames;
                long drops = session.DroppedFrames;
                path = session.StopAsync().GetAwaiter().GetResult();
                watch.Stop();
                long steadyFrames = frames - warmFrames;
                Console.WriteLine(
                    $"PROFILE session steady wgc={usingWgc} frames={steadyFrames} drops={drops} " +
                    $"elapsed={steady.Elapsed.TotalSeconds:F2}s " +
                    $"fps={steadyFrames / Math.Max(0.001, steady.Elapsed.TotalSeconds):F1} " +
                    $"totalFrames={frames} totalElapsed={elapsed.TotalSeconds:F2}s " +
                    $"wall={watch.Elapsed.TotalSeconds:F2}s error={session.LastError}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private sealed class ScreenAnimator : IDisposable
        {
            private readonly ManualResetEventSlim _ready = new(false);
            private Thread? _thread;
            private Dispatcher? _dispatcher;

            public void Start()
            {
                _thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "CryoUtils profile animator"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                _ready.Wait(3000);
            }

            private void Run()
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(20, 40, 80))
                };
                var window = new Window
                {
                    Content = border,
                    Width = 800,
                    Height = 600,
                    Left = 100,
                    Top = 100,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Topmost = true
                };
                var frame = 0;
                CompositionTarget.Rendering += (_, _) =>
                {
                    frame++;
                    byte red = (byte)((frame * 7) & 0xFF);
                    byte green = (byte)((frame * 11) & 0xFF);
                    byte blue = (byte)((frame * 13) & 0xFF);
                    ((SolidColorBrush)border.Background).Color = Color.FromRgb(red, green, blue);
                };
                window.Show();
                _dispatcher = Dispatcher.CurrentDispatcher;
                _ready.Set();
                Dispatcher.Run();
                window.Close();
            }

            public void Dispose()
            {
                _dispatcher?.InvokeShutdown();
                _thread?.Join(2000);
                _ready.Dispose();
            }
        }

        private static void PrintDistribution(string label, List<double> values)
        {
            if (values.Count == 0)
            {
                Console.WriteLine($"PROFILE {label}: no samples");
                return;
            }

            var sorted = values.OrderBy(value => value).ToArray();
            double mean = sorted.Average();
            Console.WriteLine(
                $"PROFILE {label}: mean={mean:F2} p50={sorted[sorted.Length / 2]:F2} " +
                $"p95={sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.95))]:F2} " +
                $"max={sorted[^1]:F2}");
        }
    }
}
