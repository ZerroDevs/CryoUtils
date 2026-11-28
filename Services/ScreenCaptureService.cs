using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Models;
using Drawing = System.Drawing;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace CryoUtils.Services
{
    public sealed class MonitorInfo
    {
        public IntPtr Handle { get; init; }
        public NativeMethods.RECT Bounds { get; init; }
        public NativeMethods.RECT WorkArea { get; init; }
        public string DeviceName { get; init; } = "";
        public double Scale { get; init; } = 1.0;
    }

    public static class ScreenCaptureService
    {
        private const string CaptureFilePrefix = "CryoUtils_";

        public static IReadOnlyList<MonitorInfo> GetMonitors()
        {
            var monitors = new List<MonitorInfo>();
            NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data) =>
                {
                    var info = new NativeMethods.MONITORINFOEX
                    {
                        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                        szDevice = ""
                    };

                    if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                    {
                        uint dpiX = 96;
                        uint dpiY = 96;
                        if (NativeMethods.GetDpiForMonitor(
                                hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) != 0)
                        {
                            dpiX = 96;
                        }

                        monitors.Add(new MonitorInfo
                        {
                            Handle = hMonitor,
                            Bounds = info.rcMonitor,
                            WorkArea = info.rcWork,
                            DeviceName = info.szDevice,
                            Scale = dpiX / 96.0
                        });
                    }

                    return true;
                },
                IntPtr.Zero);

            if (monitors.Count == 0)
            {
                int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
                int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
                monitors.Add(new MonitorInfo
                {
                    Handle = NativeMethods.MonitorFromPoint(
                        new NativeMethods.POINT { X = 0, Y = 0 },
                        NativeMethods.MONITOR_DEFAULTTONEAREST),
                    Bounds = new NativeMethods.RECT { Left = 0, Top = 0, Right = w, Bottom = h },
                    WorkArea = new NativeMethods.RECT { Left = 0, Top = 0, Right = w, Bottom = h },
                    DeviceName = "DISPLAY",
                    Scale = 1.0
                });
            }

            return monitors;
        }

        public static NativeMethods.RECT GetVirtualBounds()
        {
            int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            if (w <= 0 || h <= 0)
            {
                w = 1920;
                h = 1080;
            }
            return new NativeMethods.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        }

        /// <summary>Pre-initializes WPF Imaging / InteropBitmap pipeline off the capture hot path.</summary>
        public static void WarmUpCapturePipeline()
        {
            IntPtr section = IntPtr.Zero;
            try
            {
                section = NativeMethods.CreateFileMappingW(
                    NativeMethods.INVALID_HANDLE_VALUE,
                    IntPtr.Zero,
                    NativeMethods.PAGE_READWRITE,
                    0,
                    4,
                    null);
                if (section != IntPtr.Zero)
                {
                    var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromMemorySection(
                        section,
                        1,
                        1,
                        PixelFormats.Bgr32,
                        4,
                        0);
                    bmp.Freeze();
                }
            }
            catch
            {
            }
            finally
            {
                if (section != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(section);
                }
            }
        }

        /// <summary>Captures a physical-pixel region of the desktop into a frozen 96 DPI bitmap.</summary>
        public static BitmapSource CaptureRegion(NativeMethods.RECT region, bool includeCursor = false)
        {
            int width = Math.Max(1, region.Width);
            int height = Math.Max(1, region.Height);

            if (TryCaptureRegionZeroCopy(region.Left, region.Top, width, height, includeCursor, out BitmapSource? direct) &&
                direct != null)
            {
                return direct;
            }

            using var bitmap = new Drawing.Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    region.Left,
                    region.Top,
                    0,
                    0,
                    new Drawing.Size(width, height),
                    Drawing.CopyPixelOperation.SourceCopy);

                if (includeCursor)
                {
                    IntPtr hdc = graphics.GetHdc();
                    try
                    {
                        DrawCursorOnDc(hdc, region.Left, region.Top, width, height);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }
            }

            BitmapData data = bitmap.LockBits(
                new Drawing.Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var source = BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    PixelFormats.Bgr32,
                    null,
                    data.Scan0,
                    data.Stride * height,
                    data.Stride);
                source.Freeze();
                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static bool TryCaptureRegionZeroCopy(
            int left,
            int top,
            int width,
            int height,
            bool includeCursor,
            out BitmapSource? result)
        {
            result = null;
            int stride = width * 4;
            long byteCount = (long)stride * height;
            if (byteCount <= 0 || byteCount > uint.MaxValue)
            {
                return false;
            }

            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr section = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldObj = IntPtr.Zero;

            try
            {
                screenDc = NativeMethods.GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero)
                {
                    return false;
                }

                memDc = NativeMethods.CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero)
                {
                    return false;
                }

                section = NativeMethods.CreateFileMappingW(
                    NativeMethods.INVALID_HANDLE_VALUE,
                    IntPtr.Zero,
                    NativeMethods.PAGE_READWRITE,
                    0,
                    (uint)byteCount,
                    null);
                if (section == IntPtr.Zero)
                {
                    return false;
                }

                var bmi = new NativeMethods.BITMAPINFO
                {
                    bmiHeader = new NativeMethods.BITMAPINFOHEADER
                    {
                        biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height, // top-down DIB
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = NativeMethods.BI_RGB,
                        biSizeImage = (uint)byteCount
                    }
                };

                hBitmap = NativeMethods.CreateDIBSection(
                    screenDc,
                    ref bmi,
                    NativeMethods.DIB_RGB_COLORS,
                    out _,
                    section,
                    0);
                if (hBitmap == IntPtr.Zero)
                {
                    return false;
                }

                oldObj = NativeMethods.SelectObject(memDc, hBitmap);
                if (!NativeMethods.BitBlt(memDc, 0, 0, width, height, screenDc, left, top, NativeMethods.SRCCOPY))
                {
                    return false;
                }

                if (includeCursor)
                {
                    DrawCursorOnDc(memDc, left, top, width, height);
                }

                NativeMethods.SelectObject(memDc, oldObj);
                oldObj = IntPtr.Zero;
                NativeMethods.DeleteObject(hBitmap);
                hBitmap = IntPtr.Zero;

                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromMemorySection(
                    section,
                    width,
                    height,
                    PixelFormats.Bgr32,
                    stride,
                    0);
                source.Freeze();
                result = source;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (oldObj != IntPtr.Zero && memDc != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldObj);
                }
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                }
                if (memDc != IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(memDc);
                }
                if (screenDc != IntPtr.Zero)
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                }
                if (section != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(section);
                }
            }
        }

        public static BitmapSource Crop(BitmapSource source, Int32Rect rect)
        {
            rect = ClampRect(rect, source.PixelWidth, source.PixelHeight);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return source;
            }

            var cropped = new CroppedBitmap(source, rect);
            cropped.Freeze();
            return cropped;
        }

        public static Int32Rect ClampRect(Int32Rect rect, int width, int height)
        {
            int x = Math.Clamp(rect.X, 0, Math.Max(0, width - 1));
            int y = Math.Clamp(rect.Y, 0, Math.Max(0, height - 1));
            int right = Math.Clamp(rect.X + rect.Width, x + 1, width);
            int bottom = Math.Clamp(rect.Y + rect.Height, y + 1, height);
            return new Int32Rect(x, y, right - x, bottom - y);
        }

        public static void DrawCursorOnDc(IntPtr hdc, int regionLeft, int regionTop, int width, int height)
        {
            if (hdc == IntPtr.Zero)
            {
                return;
            }

            var ci = new NativeMethods.CURSORINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.CURSORINFO>()
            };
            if (!NativeMethods.GetCursorInfo(ref ci) ||
                ci.flags != NativeMethods.CURSOR_SHOWING ||
                ci.hCursor == IntPtr.Zero)
            {
                return;
            }

            IntPtr hIcon = NativeMethods.CopyIcon(ci.hCursor);
            if (hIcon == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.ICONINFO ii = default;
            try
            {
                int hotX = 0;
                int hotY = 0;
                if (NativeMethods.GetIconInfo(hIcon, out ii))
                {
                    hotX = ii.xHotspot;
                    hotY = ii.yHotspot;
                }

                int drawX = ci.ptScreenPos.X - hotX - regionLeft;
                int drawY = ci.ptScreenPos.Y - hotY - regionTop;
                if (drawX >= -128 && drawY >= -128 && drawX < width && drawY < height)
                {
                    NativeMethods.DrawIconEx(hdc, drawX, drawY, hIcon, 0, 0, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
                }
            }
            finally
            {
                if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
                NativeMethods.DestroyIcon(hIcon);
            }
        }

        public static BitmapSource? CaptureForegroundWindow(out string sourceApp, out string sourceWindow, bool includeCursor = false)
        {
            sourceApp = "";
            sourceWindow = "";
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                sourceWindow = GetWindowTitle(hwnd);
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                {
                    using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                    sourceApp = proc.ProcessName;
                }
            }
            catch
            {
            }

            var bounds = GetWindowBounds(hwnd);
            var desktop = GetVirtualBounds();
            int left = Math.Max(bounds.Left, desktop.Left);
            int top = Math.Max(bounds.Top, desktop.Top);
            int right = Math.Min(bounds.Right, desktop.Right);
            int bottom = Math.Min(bounds.Bottom, desktop.Bottom);
            if (right <= left || bottom <= top)
            {
                return null;
            }

            return CaptureRegion(new NativeMethods.RECT
            {
                Left = left,
                Top = top,
                Right = right,
                Bottom = bottom
            }, includeCursor);
        }

        public static BitmapSource CaptureWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return CaptureRegion(GetVirtualBounds());
            }

            NativeMethods.RECT rect;
            if (NativeMethods.DwmGetWindowAttribute(
                    hwnd,
                    NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out rect,
                    System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0)
            {
                NativeMethods.GetWindowRect(hwnd, out rect);
            }

            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return CaptureRegion(GetVirtualBounds());
            }

            return CaptureRegion(rect);
        }

        public static string GetWindowTitle(IntPtr hwnd)
        {
            var buffer = new System.Text.StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        public static NativeMethods.RECT GetWindowBounds(IntPtr hwnd)
        {
            NativeMethods.RECT rect;
            if (NativeMethods.DwmGetWindowAttribute(
                    hwnd,
                    NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out rect,
                    System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) == 0 &&
                rect.Width > 0 && rect.Height > 0)
            {
                return rect;
            }

            NativeMethods.GetWindowRect(hwnd, out rect);
            return rect;
        }

        public static string CreateTimestampedFileName(string extension)
        {
            string cleanExtension = extension.StartsWith(".", StringComparison.Ordinal)
                ? extension
                : "." + extension;
            return $"CryoUtils_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{cleanExtension}";
        }

        private static readonly HashSet<string> SavedCaptureExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".mp4"
        };

        public static string SaveWithDialog(BitmapSource bitmap, Window? owner, string defaultExtension)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = LocalizationService.Get("Common_SaveAs"),
                Filter = LocalizationService.Get("Common_SaveImageFilter"),
                DefaultExt = defaultExtension,
                AddExtension = true,
                FileName = CreateTimestampedFileName(defaultExtension),
                InitialDirectory = SettingsService.Instance.EffectiveSaveFolder
            };

            if (!Directory.Exists(dialog.InitialDirectory))
            {
                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }

            if (dialog.ShowDialog(owner) != true)
            {
                return "";
            }

            ImageIO.SaveBitmap(bitmap, dialog.FileName);
            EnforceSavedCaptureLimit();
            return dialog.FileName;
        }

        public static string QuickSave(BitmapSource bitmap, string extension)
        {
            string folder = SettingsService.Instance.EffectiveSaveFolder;
            Directory.CreateDirectory(folder);
            string path = CreateUniquePath(Path.Combine(folder, CreateTimestampedFileName(extension)));
            ImageIO.SaveBitmap(bitmap, path);
            EnforceSavedCaptureLimit();
            return path;
        }

        public static List<FileInfo> GetSavedCaptureFiles()
        {
            string folder = SettingsService.Instance.EffectiveSaveFolder;
            if (!Directory.Exists(folder))
            {
                return new List<FileInfo>();
            }

            var list = new List<FileInfo>();
            try
            {
                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    if (!SavedCaptureExtensions.Contains(Path.GetExtension(file)))
                    {
                        continue;
                    }

                    try
                    {
                        list.Add(new FileInfo(file));
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            list.Sort((a, b) =>
            {
                long tb = Math.Max(b.CreationTimeUtc.Ticks, b.LastWriteTimeUtc.Ticks);
                long ta = Math.Max(a.CreationTimeUtc.Ticks, a.LastWriteTimeUtc.Ticks);
                return tb.CompareTo(ta);
            });
            return list;
        }

        public static List<FileInfo> GetManagedCaptureFiles()
        {
            var managed = new List<FileInfo>();
            foreach (var file in GetSavedCaptureFiles())
            {
                if (file.Name.StartsWith(CaptureFilePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    managed.Add(file);
                }
            }
            return managed;
        }

        public static void EnforceSavedCaptureLimit()
        {
            int limit = SettingsService.Instance.Current.SavedCapturesLimit;
            if (limit <= 0)
            {
                return;
            }

            var files = GetManagedCaptureFiles();
            if (files.Count <= limit)
            {
                return;
            }

            var deletedPaths = new List<string>();
            for (int i = limit; i < files.Count; i++)
            {
                try
                {
                    string path = files[i].FullName;
                    if (NativeMethods.MoveToRecycleBin(path))
                    {
                        deletedPaths.Add(path);
                    }
                }
                catch
                {
                }
            }

            if (deletedPaths.Count > 0)
            {
                RecentCaptureService.Instance.NotifyFilesDeleted(deletedPaths);
            }
        }

        private static string CreateUniquePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            string directory = Path.GetDirectoryName(path) ?? ".";
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int suffix = 2; suffix < 1000; suffix++)
            {
                string candidate = Path.Combine(directory, $"{name}_{suffix}{extension}");
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{extension}");
        }
    }
}
