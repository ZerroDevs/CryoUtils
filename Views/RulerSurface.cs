using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CryoUtils.Views
{
    public enum RulerMode
    {
        Horizontal,
        Vertical,
        Diagonal,
        Region,
        Guides
    }

    public sealed class RulerSurface : FrameworkElement
    {
        public RulerMode Mode { get; set; }
        public Point? Start { get; set; }
        public Point? Current { get; set; }
        public List<Point> Guides { get; } = new();
        public double DpiScale { get; set; } = 1.0;

        public void ResetSelection()
        {
            Start = null;
            Current = null;
            InvalidateVisual();
        }

        public void AddGuide(Point point)
        {
            Guides.Add(point);
            InvalidateVisual();
        }

        public void ClearGuides()
        {
            Guides.Clear();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(165, 0x62, 0xB0, 0xE8)), 1)
            {
                DashStyle = new DashStyle(new double[] { 5, 5 }, 0)
            };
            guidePen.Freeze();
            foreach (var guide in Guides)
            {
                dc.DrawLine(guidePen, new Point(0, guide.Y), new Point(ActualWidth, guide.Y));
                dc.DrawLine(guidePen, new Point(guide.X, 0), new Point(guide.X, ActualHeight));
            }

            if (!Start.HasValue || !Current.HasValue)
            {
                return;
            }

            var start = Start.Value;
            var end = Current.Value;
            var brush = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
            brush.Freeze();
            var pen = new Pen(brush, 2);
            pen.Freeze();

            if (Mode == RulerMode.Region)
            {
                dc.DrawRectangle(null, pen, new Rect(start, end));
                dc.DrawRectangle(brush, null, new Rect(start.X - 3, start.Y - 3, 6, 6));
                dc.DrawRectangle(brush, null, new Rect(end.X - 3, end.Y - 3, 6, 6));
                return;
            }

            dc.DrawLine(pen, start, end);
            dc.DrawEllipse(brush, null, start, 3, 3);
            dc.DrawEllipse(brush, null, end, 3, 3);

            // Small end ticks make the ruler readable over busy desktop content.
            double angle = System.Math.Atan2(end.Y - start.Y, end.X - start.X);
            double nx = -System.Math.Sin(angle) * 7;
            double ny = System.Math.Cos(angle) * 7;
            dc.DrawLine(pen, new Point(start.X - nx, start.Y - ny), new Point(start.X + nx, start.Y + ny));
            dc.DrawLine(pen, new Point(end.X - nx, end.Y - ny), new Point(end.X + nx, end.Y + ny));
        }
    }
}
