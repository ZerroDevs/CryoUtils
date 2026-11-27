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

        /// <summary>Captures a physical-pixel region of the desktop into a 96 DPI bitmap.</summary>
        public static BitmapSource CaptureRegion(NativeMethods.RECT region)
        {
            int width = Math.Max(1, region.Width);
            int height = Math.Max(1, region.Height);
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
                    PixelFormats.Bgra32,
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

        public static BitmapSource? CaptureForegroundWindow(out string sourceApp, out string sourceWindow)
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
            });
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
