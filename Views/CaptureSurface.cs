using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public sealed class CaptureSurface : FrameworkElement
    {
        private BitmapSource? _monitorBitmap;

        public CaptureController? Controller { get; set; }
        public MonitorInfo? Monitor { get; set; }
        public bool IsActiveMonitor { get; set; }
        public Point? CursorPhysical { get; set; }

        public void SetMonitor(MonitorInfo monitor)
        {
            Monitor = monitor;
            if (Controller?.VirtualBitmap != null)
            {
                var bounds = monitor.Bounds;
                var virtualBounds = Controller.VirtualBounds;
                int x = Math.Clamp(bounds.Left - virtualBounds.Left, 0, Controller.VirtualBitmap.PixelWidth - 1);
                int y = Math.Clamp(bounds.Top - virtualBounds.Top, 0, Controller.VirtualBitmap.PixelHeight - 1);
                int right = Math.Clamp(bounds.Right - virtualBounds.Left, x + 1, Controller.VirtualBitmap.PixelWidth);
                int bottom = Math.Clamp(bounds.Bottom - virtualBounds.Top, y + 1, Controller.VirtualBitmap.PixelHeight);
                var cropped = new CroppedBitmap(
                    Controller.VirtualBitmap,
                    new Int32Rect(x, y, right - x, bottom - y));
                cropped.Freeze();
                _monitorBitmap = cropped;
            }
        }

        public void Refresh()
        {
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Controller == null || Monitor == null || _monitorBitmap == null)
            {
                return;
            }

            var bounds = Monitor.Bounds;
            double scale = Math.Max(0.1, Monitor.Scale);
            var matrix = new Matrix(1 / scale, 0, 0, 1 / scale, -bounds.Left / scale, -bounds.Top / scale);
            dc.PushTransform(new MatrixTransform(matrix));

            var monitorRect = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            dc.DrawImage(_monitorBitmap, monitorRect);

            var dim = ThemeService.GetBrush("OverlayDimBrush");
            dc.DrawRectangle(dim, null, monitorRect);

            Rect? selection = Controller.Selection;
            Rect? visibleSelection = null;
            if (selection.HasValue)
            {
                var selected = selection.Value;
                var intersect = Rect.Intersect(selected, monitorRect);
                if (!intersect.IsEmpty && intersect.Width > 0 && intersect.Height > 0)
                {
                    visibleSelection = intersect;
                    dc.PushClip(new RectangleGeometry(intersect));
                    dc.DrawImage(_monitorBitmap, monitorRect);
                    AnnotationRenderer.Draw(dc, Controller.Annotations, Controller.VirtualBitmap!);
                    dc.Pop();
                }
            }

            if (visibleSelection.HasValue)
            {
                double border = Math.Max(1, 1.25 * scale);
                var borderBrush = ThemeService.GetBrush("OverlaySelectionBorderBrush");
                var pen = new Pen(borderBrush, border);
                pen.Freeze();
                dc.DrawRectangle(null, pen, visibleSelection.Value);
            }

            if (CursorPhysical.HasValue && !IsActiveMonitor)
            {
                CursorPhysical = null;
            }

            if (CursorPhysical.HasValue && Controller.ShowCrosshair)
            {
                double cross = Math.Max(1, scale);
                var overlayColor = ThemeService.GetColor("OverlaySelectionBorderBrush");
                var crossBrush = new SolidColorBrush(Color.FromArgb(140, overlayColor.R, overlayColor.G, overlayColor.B));
                crossBrush.Freeze();
                var crossPen = new Pen(crossBrush, cross)
                {
                    DashStyle = new DashStyle(new double[] { 3, 4 }, 0)
                };
                crossPen.Freeze();
                var cursor = CursorPhysical.Value;
                dc.DrawLine(crossPen,
                    new Point(bounds.Left, cursor.Y),
                    new Point(bounds.Right, cursor.Y));
                dc.DrawLine(crossPen,
                    new Point(cursor.X, bounds.Top),
                    new Point(cursor.X, bounds.Bottom));
            }

            if (IsActiveMonitor && selection.HasValue)
            {
                DrawHandles(dc, selection.Value, scale);
            }

            dc.Pop();
        }

        private static void DrawHandles(DrawingContext dc, Rect selection, double scale)
        {
            double size = 7 * scale;
            double half = size / 2;
            var points = new[]
            {
                new Point(selection.Left, selection.Top),
                new Point(selection.Left + selection.Width / 2, selection.Top),
                new Point(selection.Right, selection.Top),
                new Point(selection.Right, selection.Top + selection.Height / 2),
                new Point(selection.Right, selection.Bottom),
                new Point(selection.Left + selection.Width / 2, selection.Bottom),
                new Point(selection.Left, selection.Bottom),
                new Point(selection.Left, selection.Top + selection.Height / 2)
            };

            var fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            fill.Freeze();
            var borderPen = new Pen(ThemeService.GetBrush("OverlaySelectionBorderBrush"), Math.Max(1, scale));
            borderPen.Freeze();
            foreach (var point in points)
            {
                dc.DrawRectangle(fill, borderPen, new Rect(
                    point.X - half,
                    point.Y - half,
                    size,
                    size));
            }
        }
    }
}
