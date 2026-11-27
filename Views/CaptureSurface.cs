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

        public void ReleaseBitmap()
        {
            _monitorBitmap = null;
            Controller = null;
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
                    if (Controller.Tool == Models.CaptureTool.Select &&
                        Controller.SelectedAnnotation != null &&
                        Controller.Annotations.Contains(Controller.SelectedAnnotation))
                    {
                        DrawAnnotationSelection(dc, Controller.SelectedAnnotation, scale);
                    }
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
            var points = GetRectHandlePoints(selection);
            DrawHandlePoints(dc, points, scale, circular: false);
        }

        private static void DrawAnnotationSelection(
            DrawingContext dc,
            Models.Annotation annotation,
            double scale)
        {
            var accentBrush = ThemeService.GetBrush("OverlaySelectionBorderBrush");
            var dashPen = new Pen(accentBrush, Math.Max(1, 1.2 * scale))
            {
                DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
            };
            dashPen.Freeze();

            if (annotation.Kind is Models.AnnotationKind.Line or Models.AnnotationKind.Arrow &&
                annotation.Points.Count >= 2)
            {
                var start = annotation.Points[0];
                var end = annotation.Points[^1];
                dc.DrawLine(dashPen, start, end);
                DrawHandlePoints(dc, new[] { start, end }, scale, circular: true);
                return;
            }

            if (annotation.Kind is Models.AnnotationKind.Rectangle or Models.AnnotationKind.Ellipse or
                Models.AnnotationKind.Spotlight or Models.AnnotationKind.Blur &&
                annotation.Rect.Width > 0 && annotation.Rect.Height > 0)
            {
                var guide = annotation.Rect;
                guide.Inflate(3 * scale, 3 * scale);
                dc.DrawRectangle(null, dashPen, guide);
                DrawHandlePoints(dc, GetRectHandlePoints(annotation.Rect), scale, circular: false);
                return;
            }

            var bounds = AnnotationRenderer.GetAnnotationBounds(annotation);
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                bounds.Inflate(4 * scale, 4 * scale);
                dc.DrawRectangle(null, dashPen, bounds);
            }
        }

        private static Point[] GetRectHandlePoints(Rect rect)
        {
            return new[]
            {
                new Point(rect.Left, rect.Top),
                new Point(rect.Left + rect.Width / 2, rect.Top),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Top + rect.Height / 2),
                new Point(rect.Right, rect.Bottom),
                new Point(rect.Left + rect.Width / 2, rect.Bottom),
                new Point(rect.Left, rect.Bottom),
                new Point(rect.Left, rect.Top + rect.Height / 2)
            };
        }

        private static void DrawHandlePoints(DrawingContext dc, Point[] points, double scale, bool circular)
        {
            double size = 7 * scale;
            double half = size / 2;
            var fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            fill.Freeze();
            var borderPen = new Pen(ThemeService.GetBrush("OverlaySelectionBorderBrush"), Math.Max(1, scale));
            borderPen.Freeze();

            foreach (var point in points)
            {
                if (circular)
                {
                    double radius = 4.5 * scale;
                    dc.DrawEllipse(fill, borderPen, point, radius, radius);
                }
                else
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
}
