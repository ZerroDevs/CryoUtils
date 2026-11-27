using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CryoUtils.Services
{
    public static class WindowPlacementHelper
    {
        public static Rect GetMonitorWorkAreaDips(Window window)
        {
            var (_, workArea) = GetMonitorAndWorkAreaDips(window, preferTrayMonitor: false);
            return workArea;
        }

        public static (Rect MonitorBounds, Rect WorkArea) GetMonitorAndWorkAreaForTrayDips(Window? window)
        {
            return GetMonitorAndWorkAreaDips(window, preferTrayMonitor: true);
        }

        public static Rect GetWorkAreaForSavedPositionDips(
            double leftDip,
            double topDip,
            double widthDip,
            double heightDip,
            double dpiScaleX,
            double dpiScaleY)
        {
            double scaleX = dpiScaleX > 0 ? dpiScaleX : 1.0;
            double scaleY = dpiScaleY > 0 ? dpiScaleY : 1.0;

            try
            {
                var monitors = ScreenCaptureService.GetMonitors();
                if (monitors.Count > 0)
                {
                    var workAreas = new List<Rect>(monitors.Count);
                    foreach (var monitor in monitors)
                    {
                        workAreas.Add(ToDipRect(monitor.WorkArea, scaleX, scaleY));
                    }

                    var bounds = new Rect(leftDip, topDip, Math.Max(1, widthDip), Math.Max(1, heightDip));
                    return FindTargetWorkArea(workAreas, bounds, SystemParameters.WorkArea);
                }
            }
            catch
            {
            }

            return SystemParameters.WorkArea;
        }

        public static (double Left, double Top) ComputeTrayAnchoredPosition(
            Rect monitorBounds,
            Rect workArea,
            double windowWidth,
            double windowHeight,
            double margin = 12)
        {
            double topGap = Math.Max(0, workArea.Top - monitorBounds.Top);
            double bottomGap = Math.Max(0, monitorBounds.Bottom - workArea.Bottom);
            double leftGap = Math.Max(0, workArea.Left - monitorBounds.Left);
            double rightGap = Math.Max(0, monitorBounds.Right - workArea.Right);

            double targetLeft;
            double targetTop;

            if (topGap > bottomGap && topGap >= leftGap && topGap >= rightGap && topGap > 0)
            {
                // Taskbar is docked at the top edge: tray sits at top-right.
                targetLeft = workArea.Right - windowWidth - margin;
                targetTop = workArea.Top + margin;
            }
            else if (leftGap > rightGap && leftGap > bottomGap && leftGap > topGap && leftGap > 0)
            {
                // Vertical taskbar on the left edge: notification area sits at the bottom of the bar.
                targetLeft = workArea.Left + margin;
                targetTop = workArea.Bottom - windowHeight - margin;
            }
            else
            {
                // Standard bottom taskbar (or right taskbar / auto-hide): notification area sits at bottom-right.
                targetLeft = workArea.Right - windowWidth - margin;
                targetTop = workArea.Bottom - windowHeight - margin;
            }

            return ClampToWorkArea(
                workArea,
                targetLeft,
                targetTop,
                windowWidth,
                windowHeight,
                margin: Math.Min(margin, 8));
        }

        public static (double Left, double Top) ClampToWorkArea(
            Rect workArea,
            double left,
            double top,
            double width,
            double height,
            double margin = 8)
        {
            double minLeft = workArea.Left + margin;
            double maxLeft = Math.Max(minLeft, workArea.Right - width - margin);
            double minTop = workArea.Top + margin;
            double maxTop = Math.Max(minTop, workArea.Bottom - height - margin);

            double clampedLeft = double.IsFinite(left) ? Math.Clamp(left, minLeft, maxLeft) : maxLeft;
            double clampedTop = double.IsFinite(top) ? Math.Clamp(top, minTop, maxTop) : maxTop;
            return (clampedLeft, clampedTop);
        }

        public static Rect FindTargetWorkArea(
            IReadOnlyList<Rect> workAreas,
            Rect windowBounds,
            Rect fallbackWorkArea)
        {
            if (workAreas == null || workAreas.Count == 0)
            {
                return fallbackWorkArea;
            }

            double bestOverlapArea = 0;
            Rect bestArea = fallbackWorkArea;

            foreach (var area in workAreas)
            {
                double overlapLeft = Math.Max(area.Left, windowBounds.Left);
                double overlapTop = Math.Max(area.Top, windowBounds.Top);
                double overlapRight = Math.Min(area.Right, windowBounds.Right);
                double overlapBottom = Math.Min(area.Bottom, windowBounds.Bottom);
                double overlapW = overlapRight - overlapLeft;
                double overlapH = overlapBottom - overlapTop;

                if (overlapW > 0 && overlapH > 0)
                {
                    double overlapArea = overlapW * overlapH;
                    if (overlapArea > bestOverlapArea)
                    {
                        bestOverlapArea = overlapArea;
                        bestArea = area;
                    }
                }
            }

            return bestOverlapArea > 0 ? bestArea : fallbackWorkArea;
        }

        private static (Rect MonitorBounds, Rect WorkArea) GetMonitorAndWorkAreaDips(Window? window, bool preferTrayMonitor)
        {
            try
            {
                double scaleX = 1.0;
                double scaleY = 1.0;
                if (window != null)
                {
                    var dpi = VisualTreeHelper.GetDpi(window);
                    if (dpi.DpiScaleX > 0) scaleX = dpi.DpiScaleX;
                    if (dpi.DpiScaleY > 0) scaleY = dpi.DpiScaleY;
                }

                var monitors = ScreenCaptureService.GetMonitors();
                if (monitors.Count > 0)
                {
                    MonitorInfo? target = null;

                    if (preferTrayMonitor)
                    {
                        // If the user just clicked the tray icon on a monitor that hosts a taskbar, anchor above that tray.
                        if (NativeMethods.GetCursorPos(out var cursorPt))
                        {
                            IntPtr cursorMonitor = NativeMethods.MonitorFromPoint(cursorPt, NativeMethods.MONITOR_DEFAULTTONEAREST);
                            var candidate = FindMonitorByHandle(monitors, cursorMonitor);
                            if (candidate != null && HasTaskbarInset(candidate))
                            {
                                target = candidate;
                            }
                        }

                        target ??= FindPrimaryMonitor(monitors) ?? FindFirstTaskbarMonitor(monitors) ?? monitors[0];
                    }
                    else if (window != null)
                    {
                        IntPtr handle = new WindowInteropHelper(window).Handle;
                        if (handle != IntPtr.Zero)
                        {
                            IntPtr winMonitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
                            target = FindMonitorByHandle(monitors, winMonitor);
                        }
                    }

                    target ??= FindPrimaryMonitor(monitors) ?? monitors[0];

                    // Before HWND creation, use the target monitor's own DPI scale so DIP coords align cleanly.
                    if (window == null || new WindowInteropHelper(window).Handle == IntPtr.Zero)
                    {
                        double targetScale = target.Scale > 0 ? target.Scale : scaleX;
                        scaleX = targetScale;
                        scaleY = targetScale;
                    }

                    return (
                        ToDipRect(target.Bounds, scaleX, scaleY),
                        ToDipRect(target.WorkArea, scaleX, scaleY));
                }
            }
            catch
            {
            }

            Rect fallback = SystemParameters.WorkArea;
            return (fallback, fallback);
        }

        private static MonitorInfo? FindMonitorByHandle(IReadOnlyList<MonitorInfo> monitors, IntPtr handle)
        {
            if (handle == IntPtr.Zero) return null;
            foreach (var monitor in monitors)
            {
                if (monitor.Handle == handle)
                {
                    return monitor;
                }
            }
            return null;
        }

        private static MonitorInfo? FindPrimaryMonitor(IReadOnlyList<MonitorInfo> monitors)
        {
            foreach (var monitor in monitors)
            {
                if (monitor.Bounds.Left == 0 && monitor.Bounds.Top == 0)
                {
                    return monitor;
                }
            }
            return null;
        }

        private static MonitorInfo? FindFirstTaskbarMonitor(IReadOnlyList<MonitorInfo> monitors)
        {
            foreach (var monitor in monitors)
            {
                if (HasTaskbarInset(monitor))
                {
                    return monitor;
                }
            }
            return null;
        }

        private static bool HasTaskbarInset(MonitorInfo monitor)
        {
            return monitor.WorkArea.Left != monitor.Bounds.Left ||
                   monitor.WorkArea.Top != monitor.Bounds.Top ||
                   monitor.WorkArea.Right != monitor.Bounds.Right ||
                   monitor.WorkArea.Bottom != monitor.Bounds.Bottom;
        }

        private static Rect ToDipRect(NativeMethods.RECT pixelRect, double scaleX, double scaleY)
        {
            return new Rect(
                pixelRect.Left / scaleX,
                pixelRect.Top / scaleY,
                (pixelRect.Right - pixelRect.Left) / scaleX,
                (pixelRect.Bottom - pixelRect.Top) / scaleY);
        }
    }
}
