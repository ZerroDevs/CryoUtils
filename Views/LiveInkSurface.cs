using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CryoUtils.Views
{
    public enum LiveInkKind
    {
        Pen,
        Arrow,
        Rectangle,
        Text
    }

    public sealed class LiveInkItem
    {
        public LiveInkKind Kind { get; init; }
        public List<Point> Points { get; init; } = new();
        public string Text { get; init; } = "";
        public DateTime CreatedAt { get; init; } = DateTime.Now;
        public Color Color { get; init; } = Color.FromRgb(0xE5, 0x53, 0x4B);
        public double Thickness { get; init; } = 3;
    }

    public sealed class LiveInkSurface : FrameworkElement
    {
        public List<LiveInkItem> Items { get; } = new();
        public LiveInkItem? Preview { get; set; }

        public void Clear()
        {
            Items.Clear();
            Preview = null;
            InvalidateVisual();
        }

        public void Undo()
        {
            if (Items.Count > 0)
            {
                Items.RemoveAt(Items.Count - 1);
                InvalidateVisual();
            }
        }

        public bool RemoveOlderThan(TimeSpan age)
        {
            DateTime cutoff = DateTime.Now - age;
            int removed = Items.RemoveAll(item => item.CreatedAt < cutoff);
            if (removed > 0)
            {
                InvalidateVisual();
            }
            return removed > 0;
        }

        public void RenderInk(DrawingContext dc, double scale)
        {
            if (Math.Abs(scale - 1) > 0.001)
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
            }

            foreach (var item in Items)
            {
                DrawItem(dc, item);
            }

            if (Preview != null)
            {
                DrawItem(dc, Preview);
            }

            if (Math.Abs(scale - 1) > 0.001)
            {
                dc.Pop();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            foreach (var item in Items)
            {
                DrawItem(dc, item);
            }
            if (Preview != null)
            {
                DrawItem(dc, Preview);
            }
        }

        private static void DrawItem(DrawingContext dc, LiveInkItem item)
        {
            var brush = new SolidColorBrush(item.Color);
            brush.Freeze();
            var pen = new Pen(brush, Math.Max(1, item.Thickness))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();

            switch (item.Kind)
            {
                case LiveInkKind.Pen:
                    for (int i = 1; i < item.Points.Count; i++)
                    {
                        dc.DrawLine(pen, item.Points[i - 1], item.Points[i]);
                    }
                    if (item.Points.Count == 1)
                    {
                        dc.DrawEllipse(brush, null, item.Points[0], 1.8, 1.8);
                    }
                    break;

                case LiveInkKind.Arrow:
                    if (item.Points.Count < 2)
                    {
                        return;
                    }
                    CryoUtils.Services.AnnotationRenderer.DrawArrow(
                        dc,
                        brush,
                        item.Points[0],
                        item.Points[^1],
                        item.Thickness);
                    break;

                case LiveInkKind.Rectangle:
                    if (item.Points.Count < 2)
                    {
                        return;
                    }
                    var a = item.Points[0];
                    var b = item.Points[^1];
                    dc.DrawRectangle(null, pen, new Rect(a, b));
                    break;

                case LiveInkKind.Text:
                    if (string.IsNullOrWhiteSpace(item.Text) || item.Points.Count == 0)
                    {
                        return;
                    }
                    var text = new FormattedText(
                        item.Text,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        20,
                        brush,
                        1.0);
                    dc.DrawText(text, item.Points[0]);
                    break;
            }
        }
    }
}
