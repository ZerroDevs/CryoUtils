using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CryoUtils.Models
{
    public enum AnnotationKind
    {
        Pen,
        Line,
        Arrow,
        Rectangle,
        Ellipse,
        Spotlight,
        Redaction,
        Highlighter,
        Text,
        Blur,
        Zoom,
        Step
    }

    public sealed class Annotation
    {
        public AnnotationKind Kind { get; set; }
        public string Color { get; set; } = "#E5534B";
        public double Thickness { get; set; } = 3;
        public double Opacity { get; set; } = 1;
        public List<Point> Points { get; set; } = new();
        public Rect Rect { get; set; }
        public string Text { get; set; } = "";
        public double FontSize { get; set; } = 18;
        public int Number { get; set; }
        public bool IsRtl { get; set; }
        public BitmapSource? PixelPreview { get; set; }

        public Annotation Clone()
        {
            return new Annotation
            {
                Kind = Kind,
                Color = Color,
                Thickness = Thickness,
                Opacity = Opacity,
                Points = new List<Point>(Points),
                Rect = Rect,
                Text = Text,
                FontSize = FontSize,
                Number = Number,
                IsRtl = IsRtl
            };
        }
    }
}
