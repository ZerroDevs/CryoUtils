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

            Test("in-place annotation styling, text edit, and screenshot-only defaults behave cleanly", () =>
            {
                var defaults = new AppSettings();
                AssertTrue(!defaults.ScreenshotOnlyMode);
                AssertTrue(!defaults.LastCaptureWidth.HasValue);

                var controller = CryoUtils.Views.CaptureController.Instance;
                controller.Annotations.Clear();
                var label = new Annotation
                {
                    Kind = AnnotationKind.Text,
                    Color = "#E5534B",
                    Thickness = 3,
                    FontSize = 18,
                    Text = "Old"
                };
                controller.AddAnnotation(label);
                controller.SetSelectedAnnotation(label);

                AssertTrue(controller.UpdateSelectedAnnotationStyle(color: "#3FBF7F", thickness: 7));
                AssertEqual("#3FBF7F", label.Color);
                AssertNear(7.0, label.Thickness, 0.001);
                AssertNear(42.0, label.FontSize, 0.001);

                controller.UpdateSelectedText(label, "Updated", isRtl: false);
                AssertEqual("Updated", label.Text);
                AssertEqual(1, controller.Annotations.Count);

                controller.Undo();
                AssertEqual("Old", controller.Annotations[0].Text);

                controller.UpdateSelectedText(controller.Annotations[0], "", isRtl: false);
                AssertEqual(0, controller.Annotations.Count);

                var bytes = new byte[16 * 16 * 4];
                var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                    16, 16, 96, 96, PixelFormats.Bgra32, null, bytes, 64);
                var blur = new Annotation
                {
                    Kind = AnnotationKind.Blur,
                    Thickness = 3,
                    Rect = new Rect(0, 0, 8, 8),
                    PixelPreview = bmp
                };
                controller.AddAnnotation(blur);
                controller.SetSelectedAnnotation(blur);
                AssertTrue(controller.UpdateSelectedAnnotationStyle(thickness: 5));
                AssertTrue(blur.PixelPreview == null);
            });

            Test("virtual key resolution works without WinForms", () =>
            {
                AssertEqual(0x2Cu, GlobalHotkeyService.ResolveVirtualKey("PrintScreen"));
                AssertEqual(0x13u, GlobalHotkeyService.ResolveVirtualKey("Pause"));
                AssertEqual(0x78u, GlobalHotkeyService.ResolveVirtualKey("F9"));
                AssertEqual(0x35u, GlobalHotkeyService.ResolveVirtualKey("D5"));
                AssertEqual(0x35u, GlobalHotkeyService.ResolveVirtualKey("5"));
                AssertEqual(0x53u, GlobalHotkeyService.ResolveVirtualKey("S"));
                AssertEqual(0x21u, GlobalHotkeyService.ResolveVirtualKey("PageUp"));
                AssertEqual(0x22u, GlobalHotkeyService.ResolveVirtualKey("PageDown"));
                AssertEqual(0x24u, GlobalHotkeyService.ResolveVirtualKey("Home"));
                AssertEqual(0x2Du, GlobalHotkeyService.ResolveVirtualKey("Insert"));
                AssertEqual(0u, GlobalHotkeyService.ResolveVirtualKey(""));
            });

            Test("updater selects matching variant setup asset and orders full before lite", () =>
            {
                // Sorted alphabetically as GitHub Releases returns assets: Full ('0') precedes Setup-Lite ('L').
                var assets = new[]
                {
                    ("CryoUtils-Setup-0.0.101.exe", "https://example.com/full.exe"),
                    ("CryoUtils-Setup-Lite-0.0.101.exe", "https://example.com/lite.exe")
                };

                var lite = UpdaterService.SelectPreferredAsset(assets, isSelfContained: false);
                AssertEqual("CryoUtils-Setup-Lite-0.0.101.exe", lite.Name);

                var full = UpdaterService.SelectPreferredAsset(assets, isSelfContained: true);
                AssertEqual("CryoUtils-Setup-0.0.101.exe", full.Name);
            });

            Test("tray primary and double-click respect screenshot-only mode", () =>
            {
                AssertEqual(
                    (int)CryoUtils.Tray.TrayIconManager.TrayPrimaryOutcome.Capture,
                    (int)CryoUtils.Tray.TrayIconManager.ResolvePrimaryAction(screenshotOnlyMode: true, "Dashboard", isDoubleClick: false));
                AssertEqual(
                    (int)CryoUtils.Tray.TrayIconManager.TrayPrimaryOutcome.None,
                    (int)CryoUtils.Tray.TrayIconManager.ResolvePrimaryAction(screenshotOnlyMode: true, "Dashboard", isDoubleClick: true));
                AssertEqual(
                    (int)CryoUtils.Tray.TrayIconManager.TrayPrimaryOutcome.Capture,
                    (int)CryoUtils.Tray.TrayIconManager.ResolvePrimaryAction(screenshotOnlyMode: false, "Capture", isDoubleClick: false));
                AssertEqual(
                    (int)CryoUtils.Tray.TrayIconManager.TrayPrimaryOutcome.None,
                    (int)CryoUtils.Tray.TrayIconManager.ResolvePrimaryAction(screenshotOnlyMode: false, "Capture", isDoubleClick: true));
                AssertEqual(
                    (int)CryoUtils.Tray.TrayIconManager.TrayPrimaryOutcome.ShowDashboard,
                    (int)CryoUtils.Tray.TrayIconManager.ResolvePrimaryAction(screenshotOnlyMode: false, "Dashboard", isDoubleClick: false));
                AssertEqual(
                    (int)CryoUtils.Tray.TrayIconManager.TrayPrimaryOutcome.ShowDashboard,
                    (int)CryoUtils.Tray.TrayIconManager.ResolvePrimaryAction(screenshotOnlyMode: false, "Dashboard", isDoubleClick: true));
            });

            Test("MemoryTrimService debounces, pauses on interaction, and enforces anti-bounce cooldown", () =>
            {
                MemoryTrimService.ResetForTest();
                try
                {
                    int executions = 0;
                    using var done = new ManualResetEventSlim(false);
                    MemoryTrimService.TrimExecutorForTest = () =>
                    {
                        Interlocked.Increment(ref executions);
                        done.Set();
                    };

                    MemoryTrimService.ScheduleTrim(110);
                    MemoryTrimService.ScheduleTrim(110);
                    AssertTrue(MemoryTrimService.HasPendingTrim);
                    AssertEqual(2, MemoryTrimService.ScheduledCountForTest);
                    AssertTrue(done.Wait(3000));
                    System.Threading.Thread.Sleep(40);
                    AssertEqual(1, executions);

                    done.Reset();
                    MemoryTrimService.PauseForInteraction(TimeSpan.FromMilliseconds(220));
                    MemoryTrimService.ScheduleTrim(60);
                    System.Threading.Thread.Sleep(110);
                    AssertEqual(1, executions);
                    AssertTrue(done.Wait(3000));
                    System.Threading.Thread.Sleep(40);
                    AssertEqual(2, executions);

                    int beforeBounce = MemoryTrimService.ScheduledCountForTest;
                    MemoryTrimService.ScheduleTrim(60, skipIfRecentlyTrimmed: true);
                    AssertEqual(beforeBounce, MemoryTrimService.ScheduledCountForTest);
                    AssertTrue(!MemoryTrimService.HasPendingTrim);

                    MemoryTrimService.Cancel(shuttingDown: true);
                    MemoryTrimService.ScheduleTrim(60);
                    AssertTrue(!MemoryTrimService.HasPendingTrim);
                }
                finally
                {
                    MemoryTrimService.ResetForTest();
                }
            });

            Test("color dialog COLORREF round-trip matches #RRGGBB", () =>
            {
                var input = Color.FromRgb(0x2B, 0x88, 0xD8);
                uint colorRef = ColorDialogHelper.ColorToColorRef(input);
                AssertEqual(0x00D8882Bu, colorRef);
                AssertEqual("#2B88D8", ColorDialogHelper.ColorRefToHex(colorRef));
            });

            Test("screenshot-only overlay keeps OCR, OCR Copy, and QR while hiding Upload, Record, and Pin", () =>
            {
                AssertTrue(Views.CaptureOverlayWindow.IsOverlayActionVisible("Ocr", screenshotOnly: true));
                AssertTrue(Views.CaptureOverlayWindow.IsOverlayActionVisible("OcrCopy", screenshotOnly: true));
                AssertTrue(Views.CaptureOverlayWindow.IsOverlayActionVisible("Qr", screenshotOnly: true));
                AssertTrue(Views.CaptureOverlayWindow.IsOverlayActionVisible("Copy", screenshotOnly: true));
                AssertTrue(Views.CaptureOverlayWindow.IsOverlayActionVisible("Save", screenshotOnly: true));
                AssertTrue(!Views.CaptureOverlayWindow.IsOverlayActionVisible("Upload", screenshotOnly: true));
                AssertTrue(!Views.CaptureOverlayWindow.IsOverlayActionVisible("Record", screenshotOnly: true));
                AssertTrue(!Views.CaptureOverlayWindow.IsOverlayActionVisible("Pin", screenshotOnly: true));
                AssertTrue(Views.CaptureOverlayWindow.IsOverlayActionVisible("Upload", screenshotOnly: false));
                AssertTrue(Views.CaptureOverlayWindow.IsOverlayActionVisible("Record", screenshotOnly: false));
                AssertTrue(Views.CaptureOverlayWindow.IsOverlayActionVisible("Pin", screenshotOnly: false));
            });

            Test("updater selects HTTPS .sha256 checksum asset and verifies installer hash", () =>
            {
                var assets = new[]
                {
                    ("CryoUtils-Setup-0.0.101.exe", "https://example.com/CryoUtils-Setup-0.0.101.exe"),
                    ("CryoUtils-Setup-0.0.101.exe.sha256", "https://example.com/CryoUtils-Setup-0.0.101.exe.sha256"),
                    ("CryoUtils-Setup-Lite-0.0.101.exe.sha256", "http://insecure.example.com/lite.sha256")
                };

                var (name, url) = UpdaterService.SelectChecksumAsset(assets, "CryoUtils-Setup-0.0.101.exe");
                AssertEqual("CryoUtils-Setup-0.0.101.exe.sha256", name);
                AssertTrue(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

                var (_, rejectedHttp) = UpdaterService.SelectChecksumAsset(assets, "CryoUtils-Setup-Lite-0.0.101.exe");
                AssertEqual("", rejectedHttp);

                string sampleHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
                AssertEqual(
                    sampleHash,
                    UpdaterService.ParseExpectedSha256($"{sampleHash} *CryoUtils-Setup-0.0.101.exe", "CryoUtils-Setup-0.0.101.exe") ?? "");
                AssertEqual(
                    sampleHash,
                    UpdaterService.ParseExpectedSha256(sampleHash, "CryoUtils-Setup-0.0.101.exe") ?? "");
                AssertTrue(
                    UpdaterService.ParseExpectedSha256($"{sampleHash}  other.exe", "CryoUtils-Setup-0.0.101.exe") == null);

                string tmp = Path.Combine(Path.GetTempPath(), $"cryoutils-sha-{Guid.NewGuid():N}.bin");
                try
                {
                    byte[] payload = Encoding.UTF8.GetBytes("CryoUtils-SHA256-Test");
                    File.WriteAllBytes(tmp, payload);
                    string actualHex = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
                    AssertTrue(UpdaterService.VerifyFileSha256Async(tmp, actualHex).GetAwaiter().GetResult());
                    string wrongHex = new string('0', 64);
                    AssertTrue(!UpdaterService.VerifyFileSha256Async(tmp, wrongHex).GetAwaiter().GetResult());
                }
                finally
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
            });

            Test("pretty frame export expands bounds cleanly and freezes output", () =>
            {
                Exception? workerError = null;
                var sta = new Thread(() =>
                {
                    try
                    {
                        var px = new byte[100 * 80 * 4];
                        var src = System.Windows.Media.Imaging.BitmapSource.Create(
                            100, 80, 96, 96, PixelFormats.Bgra32, null, px, 100 * 4);
                        src.Freeze();
                        var framed = Views.CaptureController.ApplyPrettyFrame(src);
                        AssertTrue(framed.IsFrozen);
                        AssertTrue(framed.PixelWidth > src.PixelWidth);
                        AssertTrue(framed.PixelHeight > src.PixelHeight);
                    }
                    catch (Exception ex)
                    {
                        workerError = ex;
                    }
                });
                sta.SetApartmentState(ApartmentState.STA);
                sta.Start();
                sta.Join();
                if (workerError != null) throw workerError;
            });

            Test("native WebP encoder produces valid RIFF WEBP VP8L container", () =>
            {
                var px = new byte[16 * 16 * 4];
                for (int i = 0; i < px.Length; i += 4)
                {
                    px[i] = 0x33;
                    px[i + 1] = 0x66;
                    px[i + 2] = 0xCC;
                    px[i + 3] = 0xFF;
                }

                var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                    16, 16, 96, 96, PixelFormats.Bgra32, null, px, 16 * 4);
                bmp.Freeze();

                byte[] webp = SimpleWebpEncoder.Encode(bmp);
                AssertTrue(webp.Length > 24);
                AssertEqual("RIFF", Encoding.ASCII.GetString(webp, 0, 4));
                AssertEqual("WEBP", Encoding.ASCII.GetString(webp, 8, 4));
                AssertEqual("VP8L", Encoding.ASCII.GetString(webp, 12, 4));
            });

            Test("WIC image processing, compression, and WinRT PDF round-trip work without SkiaSharp or Windows.SDK.NET", () =>
            {
                string folder = Path.Combine(Path.GetTempPath(), "CryoUtils.Tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                try
                {
                    var px = new byte[64 * 48 * 4];
                    for (int y = 0; y < 48; y++)
                    {
                        for (int x = 0; x < 64; x++)
                        {
                            int idx = (y * 64 + x) * 4;
                            px[idx] = (byte)(x * 3);
                            px[idx + 1] = (byte)(y * 5);
                            px[idx + 2] = 180;
                            px[idx + 3] = (byte)(x < 8 ? 0 : 255);
                        }
                    }

                    var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                        64, 48, 96, 96, PixelFormats.Bgra32, null, px, 64 * 4);
                    bmp.Freeze();

                    string pngPath = Path.Combine(folder, "sample.png");
                    File.WriteAllBytes(pngPath, ImageIO.EncodePng(bmp));

                    string jpgPath = Path.Combine(folder, "sample.jpg");
                    ImageProcessingService.ConvertFormat(pngPath, jpgPath, "JPG");
                    AssertTrue(File.Exists(jpgPath) && new FileInfo(jpgPath).Length > 0);

                    string icoPath = Path.Combine(folder, "sample.ico");
                    ImageProcessingService.ConvertFormat(pngPath, icoPath, "ICO");
                    byte[] icoBytes = File.ReadAllBytes(icoPath);
                    AssertTrue(icoBytes.Length > 22 && icoBytes[2] == 1 && icoBytes[3] == 0);

                    string flatPath = Path.Combine(folder, "flat.png");
                    ImageProcessingService.FlattenWhite(pngPath, flatPath);
                    AssertTrue(File.Exists(flatPath) && new FileInfo(flatPath).Length > 0);

                    var compressed = CompressionService.CompressAsync(pngPath, "Custom", 75, 100).GetAwaiter().GetResult();
                    AssertTrue(compressed.Data.Length > 0);

                    string pdfPath = Path.Combine(folder, "doc.pdf");
                    int added = PdfToolsService.ImagesToPdf(new[] { pngPath, jpgPath }, pdfPath);
                    AssertEqual(2, added);

                    uint pages = WinRtBridge.GetPdfPageCountAsync(pdfPath).GetAwaiter().GetResult();
                    AssertEqual(2u, pages);

                    string compressedPdfPath = Path.Combine(folder, "doc_compressed.pdf");
                    PdfToolsService.CompressPdfAsync(pdfPath, compressedPdfPath, 65).GetAwaiter().GetResult();
                    AssertTrue(File.Exists(compressedPdfPath) && new FileInfo(compressedPdfPath).Length > 0);
                    AssertEqual(2u, WinRtBridge.GetPdfPageCountAsync(compressedPdfPath).GetAwaiter().GetResult());
                }
                finally
                {
                    try { Directory.Delete(folder, true); } catch { }
                }
            });

            Test("DashboardSearchService filters commands and enforces Lite exclusions", () =>
            {
                var fullHits = DashboardSearchService.Search("privacy", screenshotOnly: false);
                AssertTrue(fullHits.Any(hit => hit.Tab == "Workspace" && hit.SubTab == "Privacy"));

                var liteHits = DashboardSearchService.Search("privacy", screenshotOnly: true);
                AssertEqual(0, liteHits.Count);

                var snapHits = DashboardSearchService.Search("window snap", screenshotOnly: true);
                AssertTrue(snapHits.Any(hit => hit.ActionId == "snap"));

                var defaultHits = DashboardSearchService.Search("", screenshotOnly: true);
                AssertTrue(defaultHits.Count > 0 && defaultHits.All(hit => hit.Tab is not "Tools" and not "Workspace"));
            });

            Test("ThemeService CryoTray Dark palette tokens match expected colors", () =>
            {
                AssertEqual("#2B88D8", ThemeService.GetPaletteHex("ToggleOnTrackBrush") ?? "");
                AssertEqual("#255D8E", ThemeService.GetPaletteHex("HeroGradientStartBrush") ?? "");
                AssertEqual("#1E9FD0", ThemeService.GetPaletteHex("HeroGradientEndBrush") ?? "");
                AssertEqual("#141414", ThemeService.GetPaletteHex("WindowBackgroundBrush") ?? "");
                AssertEqual("#4A2A1E", ThemeService.GetPaletteHex("ToggleOffTrackBrush") ?? "");
            });

            Test("WindowPlacementHelper anchors dashboard above tray for all taskbar edges", () =>
            {
                AssertTrue(new AppSettings().AnchorToTray);

                var monitor = new Rect(0, 0, 1920, 1080);
                var bottomTray = WindowPlacementHelper.ComputeTrayAnchoredPosition(
                    monitor, new Rect(0, 0, 1920, 1032), 680, 700, margin: 12);
                AssertNear(1228, bottomTray.Left, 0.1);
                AssertNear(320, bottomTray.Top, 0.1);

                var topTray = WindowPlacementHelper.ComputeTrayAnchoredPosition(
                    monitor, new Rect(0, 48, 1920, 1032), 680, 700, margin: 12);
                AssertNear(1228, topTray.Left, 0.1);
                AssertNear(60, topTray.Top, 0.1);

                var leftTray = WindowPlacementHelper.ComputeTrayAnchoredPosition(
                    monitor, new Rect(64, 0, 1856, 1080), 680, 700, margin: 12);
                AssertNear(76, leftTray.Left, 0.1);
                AssertNear(368, leftTray.Top, 0.1);

                var tightClamp = WindowPlacementHelper.ComputeTrayAnchoredPosition(
                    new Rect(0, 0, 800, 640), new Rect(0, 0, 800, 600), 680, 700, margin: 12);
                AssertNear(108, tightClamp.Left, 0.1);
                AssertNear(8, tightClamp.Top, 0.1);
            });

            Test("AppSettings defaults and notification/startup toggles behave properly", () =>
            {
                var s = new AppSettings();
                AssertTrue(s.LaunchInBackground);
                AssertTrue(!s.StartWithWindows);
                AssertTrue(s.NotificationsEnabled);
                AssertTrue(s.HoverWindowHighlight);
                AssertTrue(s.ShowCursorCoordinates);
                AssertTrue(!s.InstantCaptureCopy);
            });

            Test("QrResultWindow.TryParseWifiPayload parses WPA and escaped Wi-Fi QR strings", () =>
            {
                AssertTrue(Views.QrResultWindow.TryParseWifiPayload(
                    "WIFI:T:WPA;S:Cryo\\;Net;P:Pass\\:123;;",
                    out string ssid,
                    out string pwd,
                    out string auth));
                AssertEqual("Cryo;Net", ssid);
                AssertEqual("Pass:123", pwd);
                AssertEqual("WPA", auth);
                AssertTrue(!Views.QrResultWindow.TryParseWifiPayload("https://example.com", out _, out _, out _));
            });

            Test("CaptureController.FindWindowCandidateAt picks front-most z-order window", () =>
            {
                var candidates = new[]
                {
                    new Views.WindowCandidate { Bounds = new Rect(100, 100, 400, 300), Title = "Front" },
                    new Views.WindowCandidate { Bounds = new Rect(0, 0, 1000, 800), Title = "Back" }
                };
                var front = Views.CaptureController.FindWindowCandidateAt(candidates, new Point(150, 150));
                AssertEqual("Front", front?.Title ?? "");
                var back = Views.CaptureController.FindWindowCandidateAt(candidates, new Point(20, 20));
                AssertEqual("Back", back?.Title ?? "");
                AssertTrue(Views.CaptureController.FindWindowCandidateAt(candidates, new Point(1500, 1500)) == null);
            });

            Test("CaptureLibraryService.MatchesQuery matches indexed OCR text and metadata", () =>
            {
                var item = new CaptureLibraryItem
                {
                    FileName = "shot_01.png",
                    SourceApp = "Code",
                    OcrText = "ConnectionString Timeout Exception"
                };
                AssertTrue(CaptureLibraryService.MatchesQuery(item, "Timeout"));
                AssertTrue(!CaptureLibraryService.MatchesQuery(item, "NonExistentKeyword"));
            });

            Test("CursorEffectTracker.FormatKeyLabel formats shortcuts and skips bare modifiers", () =>
            {
                AssertEqual("Ctrl + C", Services.Recording.CursorEffectTracker.FormatKeyLabel(0x43, ctrl: true, shift: false, alt: false, win: false));
                AssertEqual("F10", Services.Recording.CursorEffectTracker.FormatKeyLabel(0x79, ctrl: false, shift: false, alt: false, win: false));
                AssertEqual("", Services.Recording.CursorEffectTracker.FormatKeyLabel(0x11, ctrl: true, shift: false, alt: false, win: false));
            });

            Test("ClipItem stores compressed PNG bytes and decodes exact full image", () =>
            {
                const int w = 320;
                const int h = 200;
                var pixels = new byte[w * h * 4];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = 30;
                    pixels[i + 1] = 140;
                    pixels[i + 2] = 220;
                    pixels[i + 3] = 255;
                }
                var source = System.Windows.Media.Imaging.BitmapSource.Create(
                    w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
                source.Freeze();

                byte[] pngBytes = ImageIO.EncodePng(source);
                var item = new ClipItem
                {
                    Thumbnail = source,
                    CompressedPng = pngBytes,
                    ImageWidth = w,
                    ImageHeight = h
                };
                AssertTrue(item.HasImage);
                var decoded = item.ResolveFullImage();
                AssertTrue(decoded != null);
                AssertEqual(w, decoded!.PixelWidth);
                AssertEqual(h, decoded.PixelHeight);
            });

            Test("ThemeService AccentPresets override AccentBrush and frozen HeroBackgroundGradientBrush", () =>
            {
                AssertEqual("CryoBlue", ThemeService.NormalizeAccent("unknown-accent"));
                AssertEqual("Emerald", ThemeService.NormalizeAccent("emerald"));

                var dict = new ResourceDictionary();
                ThemeService.ApplyToResources(dict, "Dark", "Emerald");
                var accent = dict["AccentBrush"] as SolidColorBrush;
                AssertTrue(accent != null && accent.IsFrozen);
                AssertEqual(ThemeService.GetAccentHex("Emerald", light: false) ?? "", $"#{accent!.Color.R:X2}{accent.Color.G:X2}{accent.Color.B:X2}");

                var hero = dict["HeroBackgroundGradientBrush"] as LinearGradientBrush;
                AssertTrue(hero != null && hero.IsFrozen);
                AssertEqual(2, hero!.GradientStops.Count);
            });

            Test("GifWriter.EncodePngFilesToGif creates valid GIF from spooled PNG files", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "CryoUtils.Tests", "gif_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    const int w = 16;
                    const int h = 16;
                    var framePaths = new List<string>();
                    for (int idx = 0; idx < 2; idx++)
                    {
                        var pixels = new byte[w * h * 4];
                        for (int p = 0; p < pixels.Length; p += 4)
                        {
                            pixels[p] = (byte)(idx * 100);
                            pixels[p + 1] = 80;
                            pixels[p + 2] = 200;
                            pixels[p + 3] = 255;
                        }
                        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                            w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
                        bmp.Freeze();
                        string framePath = Path.Combine(dir, $"f_{idx:D6}.png");
                        File.WriteAllBytes(framePath, ImageIO.EncodePng(bmp));
                        framePaths.Add(framePath);
                    }

                    string outGif = Path.Combine(dir, "out.gif");
                    GifWriter.EncodePngFilesToGif(framePaths, w, h, 10, outGif);
                    AssertTrue(File.Exists(outGif));
                    byte[] header = File.ReadAllBytes(outGif);
                    AssertTrue(header.Length > 20);
                    AssertEqual("GIF89a", Encoding.ASCII.GetString(header, 0, 6));
                }
                finally
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                }
            });

            Test("AnnotationRenderer renders AnnotationKind.Zoom safely at edges and reports bounds", () =>
            {
                var pixels = new byte[100 * 80 * 4];
                var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                    100, 80, 96, 96, PixelFormats.Bgra32, null, pixels, 100 * 4);
                bmp.Freeze();

                var zoom = new Annotation
                {
                    Kind = AnnotationKind.Zoom,
                    Color = "#2B88D8",
                    Thickness = 3,
                    Rect = new Rect(75, 55, 40, 40)
                };
                var bounds = AnnotationRenderer.GetAnnotationBounds(zoom);
                AssertNear(40, bounds.Width, 0.1);
                AssertNear(40, bounds.Height, 0.1);

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    AnnotationRenderer.Draw(dc, new[] { zoom }, bmp);
                }
            });

            Test("OcrService.FindSensitiveBounds detects emails, tokens, cards, and phones while ignoring prose", () =>
            {
                var words = new List<WinRtBridge.OcrWordBox>
                {
                    new("Hello", new Rect(10, 10, 40, 14)),
                    new("1920x1080", new Rect(55, 10, 60, 14)),
                    new("alice@example.com", new Rect(10, 30, 120, 14)),
                    new("sk-proj-abc123def456ghi789", new Rect(10, 50, 150, 14)),
                    new("Bearer", new Rect(10, 70, 45, 14)),
                    new("tok_secret_value_99", new Rect(60, 70, 130, 14)),
                    new("4111", new Rect(10, 90, 30, 14)),
                    new("2222", new Rect(44, 90, 30, 14)),
                    new("3333", new Rect(78, 90, 30, 14)),
                    new("4444", new Rect(112, 90, 30, 14)),
                    new("+1", new Rect(10, 110, 18, 14)),
                    new("555", new Rect(32, 110, 26, 14)),
                    new("234-5678", new Rect(62, 110, 60, 14))
                };

                var hits = OcrService.FindSensitiveBounds(words);
                AssertEqual(5, hits.Count);
            });

            Test("CaptureController ResolveCaptureProfile and CycleCaptureProfile behave deterministically", () =>
            {
                var s = new AppSettings { ActiveCaptureProfile = "Default" };
                var def = Views.CaptureController.ResolveCaptureProfile(s);
                AssertEqual("Default", def.Id);
                AssertTrue(def.InstantCopy == null && def.PrettyFrame == null && !def.DiscordOptimize);

                s.ActiveCaptureProfile = Views.CaptureController.CycleCaptureProfile(s.ActiveCaptureProfile);
                var discord = Views.CaptureController.ResolveCaptureProfile(s);
                AssertEqual("Discord", discord.Id);
                AssertTrue(discord.InstantCopy == true && discord.DiscordOptimize && discord.PrettyFrame == false);

                s.ActiveCaptureProfile = Views.CaptureController.CycleCaptureProfile(s.ActiveCaptureProfile);
                var bug = Views.CaptureController.ResolveCaptureProfile(s);
                AssertEqual("BugReport", bug.Id);
                AssertTrue(bug.PrettyFrame == true && bug.CursorCoords == true && bug.ForcePng);

                s.ActiveCaptureProfile = Views.CaptureController.CycleCaptureProfile(s.ActiveCaptureProfile);
                var ocr = Views.CaptureController.ResolveCaptureProfile(s);
                AssertEqual("OcrOnly", ocr.Id);
                AssertTrue(ocr.InstantCopy == true && ocr.AutoOcrCopy);

                AssertEqual("Default", Views.CaptureController.CycleCaptureProfile(s.ActiveCaptureProfile));
            });

            Test("RecentCapture compresses unsaved images cleanly and decodes via ResolveFullImage", () =>
            {
                const int w = 120;
                const int h = 80;
                var pixels = new byte[w * h * 4];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = 20;
                    pixels[i + 1] = 160;
                    pixels[i + 2] = 240;
                    pixels[i + 3] = 255;
                }
                var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                    w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
                bmp.Freeze();

                RecentCaptureService.Instance.Items.Clear();
                RecentCaptureService.Instance.Add(bmp, null, "Test");
                RecentCaptureService.Instance.Add(bmp, null, "Test");
                RecentCaptureService.Instance.Add(bmp, null, "Test");

                AssertEqual(3, RecentCaptureService.Instance.Items.Count);
                AssertTrue(RecentCaptureService.Instance.Items[0].CompressedPng != null);
                AssertTrue(RecentCaptureService.Instance.Items[1].CompressedPng != null);
                AssertTrue(RecentCaptureService.Instance.Items[2].CompressedPng == null);

                var decoded = RecentCaptureService.Instance.Items[0].ResolveFullImage();
                AssertTrue(decoded != null);
                AssertEqual(w, decoded!.PixelWidth);
                AssertEqual(h, decoded.PixelHeight);
                RecentCaptureService.Instance.Items.Clear();
            });

            Test("ImageProcessingService.ToBase64DataUri and MainWindow.FormatRecentMarkdown produce valid snippets", () =>
            {
                string dataUri = ImageProcessingService.ToBase64DataUri(new byte[] { 1, 2, 3 }, "image/png");
                AssertEqual("data:image/png;base64,AQID", dataUri);

                string? diskMd = Views.MainWindow.FormatRecentMarkdown(@"C:\Captures\shot.png", "shot.png");
                AssertEqual("![shot.png](file:///C:/Captures/shot.png)", diskMd ?? "");

                string? memMd = Views.MainWindow.FormatRecentMarkdown(null, "clip.png", () => dataUri);
                AssertEqual($"![clip.png]({dataUri})", memMd ?? "");
            });

            Test("BugReport pretty frame bezel stamp formats cleanly and expands bottom bezel", () =>
            {
                string stamp = Views.CaptureController.FormatBugReportBezelStamp("Code.exe", new DateTime(2026, 11, 27, 19, 45, 0));
                AssertEqual("Code.exe · 2026-11-27 19:45", stamp);
                string dateOnly = Views.CaptureController.FormatBugReportBezelStamp(null, new DateTime(2026, 11, 27, 19, 45, 0));
                AssertEqual("2026-11-27 19:45", dateOnly);

                var pixels = new byte[100 * 60 * 4];
                var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                    100, 60, 96, 96, PixelFormats.Bgra32, null, pixels, 100 * 4);
                bmp.Freeze();

                var plainFramed = Views.CaptureController.ApplyPrettyFrame(bmp);
                var stampedFramed = Views.CaptureController.ApplyPrettyFrame(bmp, stamp);
                AssertTrue(stampedFramed.IsFrozen);
                AssertTrue(stampedFramed.PixelHeight >= plainFramed.PixelHeight);
            });

            Test("AnnotationRenderer.BuildPixelation offsets negative multi-monitor origin cleanly", () =>
            {
                const int w = 100;
                const int h = 80;
                var pixels = new byte[w * h * 4];
                var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                    w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
                bmp.Freeze();

                var pix = AnnotationRenderer.BuildPixelation(
                    bmp,
                    new Rect(-1900, -100, 40, 30),
                    sourceOriginX: -1920,
                    sourceOriginY: -120);
                AssertTrue(pix != null && pix.IsFrozen);
                AssertEqual(40, pix!.PixelWidth);
                AssertEqual(30, pix.PixelHeight);
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

                        string trimmed = Path.Combine(folder, "trimmed.mp4");
                        MediaConversionService.TrimVideoAsync(output, trimmed, 0.2, 1.5).GetAwaiter().GetResult();
                        AssertTrue(new FileInfo(trimmed).Length > 1000);
                        AssertMediaTracks(trimmed, video: true, audio: true);

                        string muted = Path.Combine(folder, "muted.mp4");
                        MediaConversionService.MuteVideoAsync(output, muted).GetAwaiter().GetResult();
                        AssertTrue(new FileInfo(muted).Length > 1000);
                        AssertMediaTracks(muted, video: true, audio: false);

                        string wav = Path.Combine(folder, "audio.wav");
                        MediaConversionService.ConvertAudioAsync(output, wav, "WAV").GetAwaiter().GetResult();
                        AssertTrue(new FileInfo(wav).Length > 44);

                        string thumb = Path.Combine(folder, "thumb.png");
                        MediaConversionService.SaveThumbnailAsync(output, thumb).GetAwaiter().GetResult();
                        AssertTrue(new FileInfo(thumb).Length > 100);
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
                Console.WriteLine($"FAIL {name}: {ex}");
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
