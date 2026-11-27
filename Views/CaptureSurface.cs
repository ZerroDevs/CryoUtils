using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public sealed class CaptureSurface : FrameworkElement
    {
        private static Brush? _cachedDimBrush;
        private static Brush? _cachedBorderBrush;
        private static Color _cachedBorderColor;
        private static readonly SolidColorBrush HandleFillBrush = CreateFrozenWhite();

        static CaptureSurface()
        {
            ThemeService.ThemeChanged += (_, _) =>
            {
                _cachedDimBrush = null;
                _cachedBorderBrush = null;
            };
        }

        private static SolidColorBrush CreateFrozenWhite()
        {
            var b = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            b.Freeze();
            return b;
        }

        private BitmapSource? _monitorBitmap;
        private double _cachedPenScale = -1;
        private Pen? _selectionPen;
        private Pen? _crossPen;
        private Pen? _dashPen;
        private Pen? _handleBorderPen;

        public CaptureController? Controller { get; set; }
        public MonitorInfo? Monitor { get; set; }
        public bool IsActiveMonitor { get; set; }
        public Point? CursorPhysical { get; set; }

        private void EnsureRenderPens(double scale)
        {
            if (_cachedDimBrush == null || _cachedBorderBrush == null)
            {
                _cachedDimBrush = ThemeService.GetBrush("OverlayDimBrush");
                _cachedBorderBrush = ThemeService.GetBrush("OverlaySelectionBorderBrush");
                _cachedBorderColor = ThemeService.GetColor("OverlaySelectionBorderBrush");
                _cachedPenScale = -1;
            }

            if (Math.Abs(_cachedPenScale - scale) > 0.001)
            {
                _cachedPenScale = scale;

                var selPen = new Pen(_cachedBorderBrush, Math.Max(1, 1.25 * scale));
                selPen.Freeze();
                _selectionPen = selPen;

                var crossBrush = new SolidColorBrush(Color.FromArgb(140, _cachedBorderColor.R, _cachedBorderColor.G, _cachedBorderColor.B));
                crossBrush.Freeze();
                var cPen = new Pen(crossBrush, Math.Max(1, scale))
                {
                    DashStyle = new DashStyle(new double[] { 3, 4 }, 0)
                };
                cPen.Freeze();
                _crossPen = cPen;

                var dPen = new Pen(_cachedBorderBrush, Math.Max(1, 1.2 * scale))
                {
                    DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
                };
                dPen.Freeze();
                _dashPen = dPen;

                var hbPen = new Pen(_cachedBorderBrush, Math.Max(1, scale));
                hbPen.Freeze();
                _handleBorderPen = hbPen;
            }
        }

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
            EnsureRenderPens(scale);

            var matrix = new Matrix(1 / scale, 0, 0, 1 / scale, -bounds.Left / scale, -bounds.Top / scale);
            dc.PushTransform(new MatrixTransform(matrix));

            var monitorRect = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            dc.DrawImage(_monitorBitmap, monitorRect);

            dc.DrawRectangle(_cachedDimBrush, null, monitorRect);

            Rect? selection = Controller.Selection;
            Rect? visibleSelection = null;
            if (!selection.HasValue && Controller.HoveredWindow != null)
            {
                var hovered = Rect.Intersect(Controller.HoveredWindow.Bounds, monitorRect);
                if (!hovered.IsEmpty && hovered.Width > 0 && hovered.Height > 0)
                {
                    dc.PushClip(new RectangleGeometry(hovered));
                    dc.DrawImage(_monitorBitmap, monitorRect);
                    dc.Pop();
                    dc.DrawRectangle(null, _dashPen, hovered);
                }
            }
            if (selection.HasValue)
            {
                var selected = selection.Value;
                var intersect = Rect.Intersect(selected, monitorRect);
                if (!intersect.IsEmpty && intersect.Width > 0 && intersect.Height > 0)
                {
                    visibleSelection = intersect;
                    dc.PushClip(new RectangleGeometry(intersect));
                    dc.DrawImage(_monitorBitmap, monitorRect);
                    AnnotationRenderer.Draw(
                        dc,
                        Controller.Annotations,
                        Controller.VirtualBitmap!,
                        Controller.VirtualBounds.Left,
                        Controller.VirtualBounds.Top);
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
                dc.DrawRectangle(null, _selectionPen, visibleSelection.Value);
            }

            if (CursorPhysical.HasValue && !IsActiveMonitor)
            {
                CursorPhysical = null;
            }

            if (CursorPhysical.HasValue && Controller.ShowCrosshair)
            {
                var cursor = CursorPhysical.Value;
                dc.DrawLine(_crossPen,
                    new Point(bounds.Left, cursor.Y),
                    new Point(bounds.Right, cursor.Y));
                dc.DrawLine(_crossPen,
                    new Point(cursor.X, bounds.Top),
                    new Point(cursor.X, bounds.Bottom));
            }

            if (IsActiveMonitor && selection.HasValue)
            {
                DrawHandles(dc, selection.Value, scale);
            }

            dc.Pop();
        }

        private void DrawHandles(DrawingContext dc, Rect selection, double scale)
        {
            var points = GetRectHandlePoints(selection);
            DrawHandlePoints(dc, points, scale, circular: false);
        }

        private void DrawAnnotationSelection(
            DrawingContext dc,
            Models.Annotation annotation,
            double scale)
        {
            if (annotation.Kind is Models.AnnotationKind.Line or Models.AnnotationKind.Arrow &&
                annotation.Points.Count >= 2)
            {
                var start = annotation.Points[0];
                var end = annotation.Points[^1];
                dc.DrawLine(_dashPen, start, end);
                DrawHandlePoints(dc, new[] { start, end }, scale, circular: true);
                return;
            }

            if (annotation.Kind is Models.AnnotationKind.Rectangle or Models.AnnotationKind.Ellipse or
                Models.AnnotationKind.Spotlight or Models.AnnotationKind.Blur &&
                annotation.Rect.Width > 0 && annotation.Rect.Height > 0)
            {
                var guide = annotation.Rect;
                guide.Inflate(3 * scale, 3 * scale);
                dc.DrawRectangle(null, _dashPen, guide);
                DrawHandlePoints(dc, GetRectHandlePoints(annotation.Rect), scale, circular: false);
                return;
            }

            var bounds = AnnotationRenderer.GetAnnotationBounds(annotation);
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                bounds.Inflate(4 * scale, 4 * scale);
                dc.DrawRectangle(null, _dashPen, bounds);
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

        private void DrawHandlePoints(DrawingContext dc, Point[] points, double scale, bool circular)
        {
            double size = 7 * scale;
            double half = size / 2;

            foreach (var point in points)
            {
                if (circular)
                {
                    double radius = 4.5 * scale;
                    dc.DrawEllipse(HandleFillBrush, _handleBorderPen, point, radius, radius);
                }
                else
                {
                    dc.DrawRectangle(HandleFillBrush, _handleBorderPen, new Rect(
                        point.X - half,
                        point.Y - half,
                        size,
                        size));
                }
            }
        }
    }
}
