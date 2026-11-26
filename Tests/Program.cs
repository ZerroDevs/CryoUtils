using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using CryoUtils.Models;
using CryoUtils.Services;
using CryoUtils.Services.Recording;

namespace CryoUtils.Tests
{
    internal static class Program
    {
        private static int _failures;
        private static int _checks;

        private static int Main()
        {
            if (Environment.GetEnvironmentVariable("CRYO_REPLAY_E2E") == "1")
            {
                return ReplayEndToEnd.Run();
            }

            if (Environment.GetEnvironmentVariable("CRYO_CAPTURE_PROFILE") == "1")
            {
                return CaptureProfile.Run();
            }

            if (Environment.GetEnvironmentVariable("CRYO_AUDIO_DEVICES") == "1")
            {
                Test("audio endpoint enumeration", () =>
                {
                    var outputs = WasapiCaptureService.GetDevices(input: false);
                    var inputs = WasapiCaptureService.GetDevices(input: true);
                    foreach (var device in outputs)
                    {
                        Console.WriteLine($"AUDIO_DEVICE output default={device.IsDefault} name={device.Name} id={device.Id}");
                    }
                    foreach (var device in inputs)
                    {
                        Console.WriteLine($"AUDIO_DEVICE input default={device.IsDefault} name={device.Name} id={device.Id}");
                    }
                    AssertTrue(outputs.Count > 0);
                    AssertTrue(inputs.Count > 0);
                    AssertTrue(outputs.Count(device => device.IsDefault) == 1);
                    AssertTrue(inputs.Count(device => device.IsDefault) == 1);
                    var defaultOutput = outputs.First(device => device.IsDefault);
                    using var capture = new WasapiCaptureService(loopback: true, defaultOutput.Id);
                    AssertTrue(capture.Start());
                    capture.Stop();
                });
                Console.WriteLine($"CHECKS={_checks} FAILURES={_failures}");
                return _failures == 0 ? 0 : 1;
            }

            Test("contrast black/white is 21:1", () =>
            {
                double ratio = ColorContrastService.CalculateRatio(Colors.Black, Colors.White);
                AssertNear(21.0, ratio, 0.01);
                var evaluation = ColorContrastService.Evaluate(ratio);
                AssertTrue(evaluation.NormalTextAa && evaluation.NormalTextAaa);
            });

            Test("clipboard text transforms", () =>
            {
                AssertEqual("HELLO WORLD", ClipboardService.TransformText("hello world", TextTransformType.Uppercase));
                AssertEqual("hello_world", ClipboardService.TransformText("Hello World", TextTransformType.SnakeCase));
                AssertEqual("hello-world", ClipboardService.TransformText("Hello World", TextTransformType.KebabCase));
                AssertEqual("helloWorld", ClipboardService.TransformText("hello world", TextTransformType.CamelCase));
                AssertEqual("a b", ClipboardService.TransformText("a%20b", TextTransformType.UrlDecode));
                AssertEqual("Q3J5b1V0aWxz", ClipboardService.TransformText("CryoUtils", TextTransformType.Base64Encode));
                AssertEqual("CryoUtils", ClipboardService.TransformText("Q3J5b1V0aWxz", TextTransformType.Base64Decode));
                string formattedJson = ClipboardService.TransformText("{\"a\":1}", TextTransformType.JsonFormat);
                AssertTrue(formattedJson.Contains("\"a\": 1") && formattedJson.Contains("\n"));
                AssertEqual("{\"a\":1}", ClipboardService.TransformText("{\n  \"a\": 1\n}", TextTransformType.JsonMinify));
            });

            Test("known SHA-256 hash", () =>
            {
                string path = TempFile("hash.txt", Encoding.UTF8.GetBytes("CryoUtils"));
                try
                {
                    string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("CryoUtils")))
                        .ToLowerInvariant();
                    AssertEqual(expected, FileToolsService.ComputeHash(path, HashAlgorithmName.SHA256));
                }
                finally
                {
                    File.Delete(path);
                }
            });

            Test("JSON and XML formatting", () =>
            {
                string jsonInput = TempFile("input.json", Encoding.UTF8.GetBytes("{\"a\":1,\"b\":[2,3]}"));
                string jsonOutput = jsonInput + ".formatted.json";
                string xmlInput = TempFile("input.xml", Encoding.UTF8.GetBytes("<root><item>1</item></root>"));
                string xmlOutput = xmlInput + ".formatted.xml";
                try
                {
                    FileToolsService.FormatJson(jsonInput, jsonOutput, indent: true);
                    AssertTrue(File.ReadAllText(jsonOutput).Contains(Environment.NewLine));
                    FileToolsService.FormatXml(xmlInput, xmlOutput);
                    AssertTrue(File.ReadAllText(xmlOutput).Contains("<root>"));
                }
                finally
                {
                    File.Delete(jsonInput);
                    File.Delete(jsonOutput);
                    File.Delete(xmlInput);
                    File.Delete(xmlOutput);
                }
            });

            Test("naming collisions preserve extensions", () =>
            {
                string first = TempFile("capture.png", Array.Empty<byte>());
                string second = first.Replace("capture.png", "capture_2.png");
                File.WriteAllBytes(second, Array.Empty<byte>());
                try
                {
                    string unique = FileToolsService.CreateUniquePath(first);
                    AssertEqual(second.Replace("capture_2.png", "capture_3.png"), unique);
                }
                finally
                {
                    File.Delete(first);
                    File.Delete(second);
                }
            });

            Test("PDF page range parser", () =>
            {
                var pages = PdfToolsService.ParsePageRange("2-4,7", 8);
                AssertEqual("1,2,3,6", string.Join(",", pages));
                AssertEqual(0, PdfToolsService.ParsePageRange("", 0).Count);
            });

            Test("color blindness simulation changes color", () =>
            {
                string simulated = ColorStudioService.Simulate("#E5534B", ColorBlindnessType.Deuteranopia);
                AssertTrue(simulated.StartsWith("#") && simulated.Length == 7);
                AssertTrue(!string.Equals(simulated, "#E5534B", StringComparison.OrdinalIgnoreCase));
            });

            Test("capture control bar stays inside work area", () =>
            {
                var bar = new Size(200, 100);
                var work = new Rect(0, 0, 800, 600);
                var below = ScrollingCaptureService.PlaceControlBar(new Rect(10, 10, 100, 100), bar, work);
                AssertNear(118, below.Y, 0.01);
                var full = ScrollingCaptureService.PlaceControlBar(new Rect(0, 0, 800, 600), bar, work);
                AssertTrue(full.X >= 0 && full.Y >= 0);
                AssertTrue(full.X + bar.Width <= work.Right && full.Y + bar.Height <= work.Bottom);
            });

            Test("palette extraction and token exports", () =>
            {
                var bytes = new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 };
                var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                    2, 1, 96, 96, PixelFormats.Bgra32, null, bytes, 8);
                var palette = ColorStudioService.ExtractPalette(bitmap, 3);
                AssertTrue(palette.Count > 0);
                AssertTrue(ColorStudioService.ExportCss(palette).Contains(":root"));
                AssertTrue(ColorStudioService.ExportTailwind(palette).Contains("cryo-1"));
            });

            Test("recording option normalization", () =>
            {
                var options = new RecordingOptions
                {
                    Fps = 120,
                    Resolution = "720p",
                    Quality = RecorderQuality.Medium,
                    MaxSeconds = 99999,
                    ReplaySeconds = 1,
                    SystemAudioDeviceId = "output-device",
                    MicrophoneDeviceId = "input-device"
                };
                AssertEqual("output-device", options.SystemAudioDeviceId);
                AssertEqual("input-device", options.MicrophoneDeviceId);
                var fromSettings = RecordingOptions.FromSettings(new AppSettings
                {
                    RecorderSystemAudioDeviceId = "settings-output",
                    RecorderMicrophoneDeviceId = "settings-input"
                });
                AssertEqual("settings-output", fromSettings.SystemAudioDeviceId);
                AssertEqual("settings-input", fromSettings.MicrophoneDeviceId);
                AssertEqual(60, options.NormalizedFps);
                AssertEqual(3600, options.NormalizedMaxSeconds);
                AssertEqual(15, options.NormalizedReplaySeconds);
                var size = options.ResolveSize(1920, 1080);
                AssertEqual(1280, size.Width);
                AssertEqual(720, size.Height);
                AssertTrue(options.ResolveBitrate(size.Width, size.Height) > 0);
            });

            Test("replay retention and disk estimates", () =>
            {
                AssertEqual(9, ReplayMath.RetentionCount(30, 4));
                AssertEqual(151, ReplayMath.RetentionCount(600, 4));
                AssertEqual(15_000_000L, ReplayMath.EstimatedDiskBytes(8_000_000, 15));
                AssertTrue(ReplayMath.HasEnoughFreeSpace(100, 50));
                AssertTrue(!ReplayMath.HasEnoughFreeSpace(99, 50));
                var end = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                AssertEqual(new DateTime(2026, 1, 1, 11, 59, 30, DateTimeKind.Utc), ReplayMath.ClipStart(end, 30));
            });

            Test("arrow shaft stops inside head without protruding past tip", () =>
            {
                foreach (double thickness in new[] { 1.5, 3.0, 6.0, 12.0 })
                {
                    var start = new Point(20, 40);
                    var end = new Point(220, 40);
                    AssertTrue(AnnotationRenderer.TryComputeArrowGeometry(start, end, thickness, out var arrow));
                    AssertTrue(arrow.HasShaft);
                    AssertTrue(arrow.ShaftEnd.X < arrow.Tip.X);
                    double baseX = Math.Max(arrow.LeftWing.X, arrow.RightWing.X);
                    AssertTrue(arrow.ShaftEnd.X >= baseX);
                    AssertNear(arrow.Tip.X, end.X, 0.001);
                    AssertNear(arrow.Tip.Y, end.Y, 0.001);
                }

                // Short drags keep effective thickness proportional and never extend shaft past tip.
                AssertTrue(AnnotationRenderer.TryComputeArrowGeometry(new Point(100, 100), new Point(108, 100), 10, out var shortArrow));
                AssertTrue(shortArrow.ShaftEnd.X <= shortArrow.Tip.X);
                AssertTrue(shortArrow.EffectiveThickness <= 4.0);
                AssertTrue(!AnnotationRenderer.TryComputeArrowGeometry(new Point(10, 10), new Point(10, 10), 3, out _));
            });

            Test("capture controller toggles active tool back to select", () =>
            {
                var controller = CryoUtils.Views.CaptureController.Instance;
                controller.SetTool(CaptureTool.Select);
                AssertTrue(controller.Tool == CaptureTool.Select);

                controller.ToggleTool(CaptureTool.Arrow);
                AssertTrue(controller.Tool == CaptureTool.Arrow);

                controller.ToggleTool(CaptureTool.Rectangle);
                AssertTrue(controller.Tool == CaptureTool.Rectangle);

                controller.ToggleTool(CaptureTool.Rectangle);
                AssertTrue(controller.Tool == CaptureTool.Select);
            });

            Test("annotation selection bounds and controller cleanup work cleanly", () =>
            {
                var arrow = new Annotation
                {
                    Kind = AnnotationKind.Arrow,
                    Thickness = 4,
                    Points = { new Point(10, 20), new Point(110, 60) }
                };
                var arrowBounds = AnnotationRenderer.GetAnnotationBounds(arrow);
                AssertTrue(arrowBounds.Width > 100 && arrowBounds.Height > 40);

                var text = new Annotation
                {
                    Kind = AnnotationKind.Text,
                    Text = "Sample",
                    FontSize = 18,
                    Rect = new Rect(40, 50, 0, 0)
                };
                var textBounds = AnnotationRenderer.GetAnnotationBounds(text);
                AssertTrue(textBounds.Width > 10 && textBounds.Height > 10);

                var controller = CryoUtils.Views.CaptureController.Instance;
                controller.Annotations.Clear();
                controller.AddAnnotation(arrow);
                controller.SetSelectedAnnotation(arrow);
                AssertTrue(ReferenceEquals(controller.SelectedAnnotation, arrow));

                controller.SetTool(CaptureTool.Pen);
                AssertTrue(controller.SelectedAnnotation == null);

                controller.SetTool(CaptureTool.Select);
                controller.SetSelectedAnnotation(arrow);
                controller.RemoveAnnotation(arrow);
                AssertTrue(controller.SelectedAnnotation == null);
            });

            if (Environment.GetEnvironmentVariable("CRYO_RECORDER_SMOKE") == "1")
            {
                Test("media foundation encoder and replay remux smoke", () =>
                {
                    string folder = Path.Combine(Path.GetTempPath(), "CryoUtils.Tests", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(folder);
                    try
                    {
                        const int width = 320;
                        const int height = 180;
                        const int fps = 30;
                        var frame = new byte[width * height * 4];
                        for (int i = 0; i < frame.Length; i += 4)
                        {
                            frame[i] = 40;
                            frame[i + 1] = 100;
                            frame[i + 2] = 180;
                            frame[i + 3] = 255;
                        }

                        var segments = new List<ReplaySegmentInfo>();
                        for (int segment = 0; segment < 2; segment++)
                        {
                            string segmentPath = Path.Combine(folder, $"segment_{segment}.mp4");
                            Console.WriteLine($"SMOKE encoder open {segmentPath}");
                            using (var encoder = new MfEncoder(segmentPath, width, height, fps, 2_000_000, true))
                            {
                                var audio = new short[1600 * 2];
                                for (int frameIndex = 0; frameIndex < 30; frameIndex++)
                                {
                                    long time = frameIndex * 10_000_000L / fps;
                                    encoder.WriteVideo(frame, time);
                                    encoder.WriteAudio(audio, time);
                                }
                                encoder.Finish();
                                Console.WriteLine($"SMOKE segment bytes={new FileInfo(segmentPath).Length} audio={encoder.HasAudio} frames={encoder.FramesWritten} error={encoder.LastError}");
                            }
                            AssertTrue(new FileInfo(segmentPath).Length > 1000);
                            AssertMediaTracks(segmentPath, video: true, audio: true);
                            segments.Add(new ReplaySegmentInfo
                            {
                                Path = segmentPath,
                                StartTicks = segment * TimeSpan.TicksPerSecond,
                                EndTicks = (segment + 1) * TimeSpan.TicksPerSecond,
                                StartedAtUtc = DateTime.UtcNow,
                                HasAudio = true,
                                Width = width,
                                Height = height
                            });
                        }

                        string output = Path.Combine(folder, "replay.mp4");
                        Console.WriteLine("SMOKE remux start");
                        ReplayRemuxer.Remux(segments, output);
                        Console.WriteLine($"SMOKE remux bytes={new FileInfo(output).Length}");
                        AssertTrue(new FileInfo(output).Length > 1000);
                        AssertMediaTracks(output, video: true, audio: true);
                    }
                    finally
                    {
                        Directory.Delete(folder, true);
                    }
                });

                Test("replay segment ring smoke", () =>
                {
                    string folder = Path.Combine(Path.GetTempPath(), "CryoUtils.Tests", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(folder);
                    try
                    {
                        var options = new RecordingOptions
                        {
                            Fps = 30,
                            Resolution = "Source",
                            Quality = RecorderQuality.Low,
                            ReplaySeconds = 15,
                            RecordSystemAudio = false,
                            RecordMicrophone = false
                        };
                        using var ring = new ReplaySegmentRing(folder, options, 320, 180, 1_000_000);
                        ring.Start();
                        var frame = new byte[320 * 180 * 4];
                        for (int i = 0; i < 300; i++)
                        {
                            frame[(i * 37) % frame.Length] = 180;
                            ring.WriteVideo(frame, i * 10_000_000L / 30);
                        }

                        ring.SealCurrent();
                        var snapshot = ring.Snapshot(15);
                        Console.WriteLine($"SMOKE ring segments={snapshot.Count} buffered={ring.BufferedDuration.TotalSeconds:F1}s");
                        AssertTrue(snapshot.Count > 0);
                        ring.Pin(snapshot);
                        string output = Path.Combine(folder, "ring_replay.mp4");
                        ReplayRemuxer.Remux(snapshot, output);
                        AssertTrue(new FileInfo(output).Length > 1000);
                        ring.Unpin(snapshot);
                    }
                    finally
                    {
                        Directory.Delete(folder, true);
                    }
                });

                Test("encoder throughput smoke", () =>
                {
                    string path = Path.Combine(Path.GetTempPath(), "CryoUtils.Tests", $"throughput_{Guid.NewGuid():N}.mp4");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    try
                    {
                        var frame = new byte[1920 * 1080 * 4];
                        using var encoder = new MfEncoder(path, 1920, 1080, 30, 6_000_000, false);
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 0; i < 60; i++)
                        {
                            encoder.WriteVideo(frame, i * 333333);
                        }
                        encoder.Finish();
                        watch.Stop();
                        Console.WriteLine($"SMOKE encoder 1080p30 frames=60 seconds={watch.Elapsed.TotalSeconds:F2} fps={60 / Math.Max(0.001, watch.Elapsed.TotalSeconds):F1} error={encoder.LastError}");
                    }
                    finally
                    {
                        try { File.Delete(path); } catch { }
                    }
                });

                Test("wgc cropped frame readback smoke", () =>
                {
                    var monitor = ScreenCaptureService.GetMonitors().First();
                    var region = new Int32Rect(
                        monitor.Bounds.Left + 40,
                        monitor.Bounds.Top + 40,
                        Math.Min(640, monitor.Bounds.Width - 80),
                        Math.Min(360, monitor.Bounds.Height - 80));
                    var completion = new System.Threading.Tasks.TaskCompletionSource<bool>();
                    using var engine = new WgcCaptureEngine(monitor.Handle, monitor.Bounds, region);
                    engine.FrameArrived += (pixels, width, height) =>
                    {
                        if (width == region.Width && height == region.Height &&
                            pixels.Any(value => value != 0))
                        {
                            completion.TrySetResult(true);
                        }
                    };
                    engine.Start();
                    AssertTrue(completion.Task.Wait(4000));
                    engine.Stop();
                });

                Test("capture session smoke", () =>
                {
                    var monitor = ScreenCaptureService.GetMonitors().First();
                    var region = new Int32Rect(
                        monitor.Bounds.Left,
                        monitor.Bounds.Top,
                        monitor.Bounds.Width,
                        monitor.Bounds.Height);
                    var options = new RecordingOptions
                    {
                        Fps = 60,
                        Resolution = "Source",
                        Quality = RecorderQuality.Low,
                        MaxSeconds = 60,
                        TrimBeforeSave = false
                    };
                    string? path = null;
                    try
                    {
                        using var session = new RecordingSession(region, options);
                        session.Start();
                        Console.WriteLine($"SMOKE session wgc={session.IsUsingWgc} size={session.Width}x{session.Height}");
                        Thread.Sleep(1500);
                        var beforePause = session.Elapsed;
                        session.Pause();
                        Thread.Sleep(300);
                        var duringPause = session.Elapsed;
                        AssertTrue((duringPause - beforePause).TotalMilliseconds < 120);
                        session.Resume();
                        Thread.Sleep(1500);
                        var captured = session.Elapsed;
                        path = session.StopAsync().GetAwaiter().GetResult();
                        Console.WriteLine($"SMOKE session frames={session.Frames} drops={session.DroppedFrames} elapsed={captured.TotalSeconds:F2}s fps={session.Frames / Math.Max(0.001, captured.TotalSeconds):F1} path={path} exists={File.Exists(path)}");
                        AssertTrue(File.Exists(path));
                        AssertTrue(new FileInfo(path).Length > 1000);
                    }
                    finally
                    {
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                });
            }

            Console.WriteLine($"CHECKS={_checks} FAILURES={_failures}");
            return _failures == 0 ? 0 : 1;
        }

        private static void Test(string name, Action action)
        {
            _checks++;
            try
            {
                action();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine($"FAIL {name}: {ex.Message}");
            }
        }

        private static string TempFile(string name, byte[] bytes)
        {
            string folder = Path.Combine(Path.GetTempPath(), "CryoUtils.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static void AssertMediaTracks(string path, bool video, bool audio)
        {
            int hr = MfApi.MFCreateSourceReaderFromURL(path, null, out var reader);
            if (hr < 0)
            {
                throw new InvalidOperationException($"Could not open media (0x{hr:X8}).");
            }

            try
            {
                if (video)
                {
                    hr = reader.GetNativeMediaType(
                        unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                        0,
                        out var videoType);
                    if (hr < 0)
                    {
                        throw new InvalidOperationException($"Video track missing (0x{hr:X8}).");
                    }
                    videoType.GetGUID(MfGuids.MF_MT_SUBTYPE, out Guid subtype);
                    Marshal.ReleaseComObject(videoType);
                    AssertEqual(MfGuids.MFVideoFormat_H264, subtype);
                }

                if (audio)
                {
                    hr = reader.GetNativeMediaType(
                        unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                        0,
                        out var audioType);
                    if (hr < 0)
                    {
                        throw new InvalidOperationException($"Audio track missing (0x{hr:X8}).");
                    }
                    audioType.GetGUID(MfGuids.MF_MT_SUBTYPE, out Guid subtype);
                    audioType.GetUINT32(MfGuids.MF_MT_AUDIO_SAMPLES_PER_SECOND, out uint rate);
                    audioType.GetUINT32(MfGuids.MF_MT_AUDIO_NUM_CHANNELS, out uint channels);
                    Marshal.ReleaseComObject(audioType);
                    AssertTrue(
                        subtype == MfGuids.MFAudioFormat_AAC ||
                        subtype == MfGuids.MFAudioFormat_ADTS);
                    AssertEqual(48_000u, rate);
                    AssertEqual(2u, channels);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(reader);
            }
        }

        private static void AssertTrue(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Expected true.");
            }
        }

        private static void AssertEqual(string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
            }
        }

        private static void AssertEqual(int expected, int actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException($"Expected {expected}, got {actual}.");
            }
        }

        private static void AssertEqual(uint expected, uint actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException($"Expected {expected}, got {actual}.");
            }
        }

        private static void AssertEqual(Guid expected, Guid actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException($"Expected {expected}, got {actual}.");
            }
        }

        private static void AssertEqual(long expected, long actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException($"Expected {expected}, got {actual}.");
            }
        }

        private static void AssertEqual(DateTime expected, DateTime actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException($"Expected {expected:O}, got {actual:O}.");
            }
        }

        private static void AssertNear(double expected, double actual, double tolerance)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException($"Expected {expected}, got {actual}.");
            }
        }
    }
}
