using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Models;

namespace CryoUtils.Services
{
    public static class AnnotationRenderer
    {
        private static readonly Typeface RegularTypeface = new(
            new FontFamily("Segoe UI Variable Text, Segoe UI, Tahoma, Arial"),
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);

        public static void Draw(
            DrawingContext dc,
            IReadOnlyList<Annotation> annotations,
            BitmapSource sourceBitmap)
        {
            foreach (var annotation in annotations)
            {
                DrawAnnotation(dc, annotation, sourceBitmap);
            }
        }

        private static void DrawAnnotation(
            DrawingContext dc,
            Annotation annotation,
            BitmapSource sourceBitmap)
        {
            var color = ParseColor(annotation.Color);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            double opacity = annotation.Kind == AnnotationKind.Highlighter
                ? 0.32
                : annotation.Opacity;
            if (opacity < 1)
            {
                dc.PushOpacity(opacity);
            }

            switch (annotation.Kind)
            {
                case AnnotationKind.Pen:
                case AnnotationKind.Highlighter:
                    DrawPen(dc, annotation, brush);
                    break;
                case AnnotationKind.Line:
                case AnnotationKind.Arrow:
                    DrawLine(dc, annotation, brush);
                    break;
                case AnnotationKind.Rectangle:
                    DrawRectangle(dc, annotation, brush);
                    break;
                case AnnotationKind.Text:
                    DrawText(dc, annotation, brush);
                    break;
                case AnnotationKind.Blur:
                    DrawPixelation(dc, annotation, sourceBitmap);
                    break;
                case AnnotationKind.Step:
                    DrawStep(dc, annotation, brush);
                    break;
            }

            if (opacity < 1)
            {
                dc.Pop();
            }
        }

        private static void DrawPen(DrawingContext dc, Annotation annotation, Brush brush)
        {
            if (annotation.Points.Count == 0)
            {
                return;
            }

            var pen = new Pen(brush, Math.Max(1, annotation.Thickness))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            if (annotation.Points.Count == 1)
            {
                var point = annotation.Points[0];
                double radius = Math.Max(0.6, annotation.Thickness / 2);
                dc.DrawEllipse(brush, null, point, radius, radius);
                return;
            }

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(annotation.Points[0], false, false);
                for (int i = 1; i < annotation.Points.Count - 1; i++)
                {
                    var current = annotation.Points[i];
                    var next = annotation.Points[i + 1];
                    var midpoint = new Point(
                        (current.X + next.X) / 2,
                        (current.Y + next.Y) / 2);
                    context.QuadraticBezierTo(current, midpoint, true, true);
                }
                context.LineTo(annotation.Points[^1], true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        private static void DrawLine(DrawingContext dc, Annotation annotation, Brush brush)
        {
            var points = annotation.Points;
            if (points.Count < 2)
            {
                return;
            }

            var start = points[0];
            var end = points[^1];
            var pen = new Pen(brush, Math.Max(1, annotation.Thickness))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawLine(pen, start, end);

            if (annotation.Kind == AnnotationKind.Arrow)
            {
                double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
                double head = Math.Max(11, annotation.Thickness * 4.2);
                double spread = Math.PI / 7;
                var p1 = new Point(
                    end.X - head * Math.Cos(angle - spread),
                    end.Y - head * Math.Sin(angle - spread));
                var p2 = new Point(
                    end.X - head * Math.Cos(angle + spread),
                    end.Y - head * Math.Sin(angle + spread));
                var headGeometry = new StreamGeometry();
                using (var context = headGeometry.Open())
                {
                    context.BeginFigure(end, true, true);
                    context.LineTo(p1, true, true);
                    context.LineTo(p2, true, true);
                }
                headGeometry.Freeze();
                dc.DrawGeometry(brush, null, headGeometry);
            }
        }

        private static void DrawRectangle(DrawingContext dc, Annotation annotation, Brush brush)
        {
            var rect = annotation.Rect;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            var pen = new Pen(brush, Math.Max(1, annotation.Thickness));
            dc.DrawRectangle(null, pen, rect);
        }

        private static void DrawText(DrawingContext dc, Annotation annotation, Brush brush)
        {
            if (string.IsNullOrEmpty(annotation.Text))
            {
                return;
            }

            var formatted = new FormattedText(
                annotation.Text,
                annotation.IsRtl ? CultureInfo.GetCultureInfo("ar-SA") : CultureInfo.GetCultureInfo("en-US"),
                annotation.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                RegularTypeface,
                Math.Max(8, annotation.FontSize),
                brush,
                1.0)
            {
                MaxTextWidth = Math.Max(40, annotation.Rect.Width > 0 ? annotation.Rect.Width : 2000)
            };

            var shadowBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            shadowBrush.Freeze();
            var shadow = new FormattedText(
                annotation.Text,
                annotation.IsRtl ? CultureInfo.GetCultureInfo("ar-SA") : CultureInfo.GetCultureInfo("en-US"),
                formatted.FlowDirection,
                RegularTypeface,
                Math.Max(8, annotation.FontSize),
                shadowBrush,
                1.0)
            {
                MaxTextWidth = formatted.MaxTextWidth
            };

            dc.DrawText(shadow, new Point(annotation.Rect.X + 1, annotation.Rect.Y + 1));
            dc.DrawText(formatted, new Point(annotation.Rect.X, annotation.Rect.Y));
        }

        private static void DrawPixelation(
            DrawingContext dc,
            Annotation annotation,
            BitmapSource sourceBitmap)
        {
            var rect = annotation.Rect;
            if (rect.Width < 2 || rect.Height < 2)
            {
                return;
            }

            annotation.PixelPreview ??= BuildPixelation(sourceBitmap, rect);
            if (annotation.PixelPreview != null)
            {
                dc.DrawImage(annotation.PixelPreview, rect);
            }
        }

        private static void DrawStep(DrawingContext dc, Annotation annotation, Brush brush)
        {
            double radius = Math.Max(12, annotation.Thickness * 5);
            var center = annotation.Rect.TopLeft;
            dc.DrawEllipse(brush, null, center, radius, radius);

            var textBrush = new SolidColorBrush(Colors.White);
            textBrush.Freeze();
            var formatted = new FormattedText(
                annotation.Number.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                RegularTypeface,
                Math.Max(10, radius * 0.95),
                textBrush,
                1.0);

            dc.DrawText(formatted, new Point(
                center.X - formatted.Width / 2,
                center.Y - formatted.Height / 2));
        }

        public static BitmapSource? BuildPixelation(BitmapSource source, Rect area)
        {
            int left = (int)Math.Floor(area.X);
            int top = (int)Math.Floor(area.Y);
            int width = (int)Math.Ceiling(area.Width);
            int height = (int)Math.Ceiling(area.Height);
            if (width < 2 || height < 2)
            {
                return null;
            }

            int x = Math.Clamp(left, 0, source.PixelWidth - 1);
            int y = Math.Clamp(top, 0, source.PixelHeight - 1);
            int right = Math.Clamp(left + width, x + 1, source.PixelWidth);
            int bottom = Math.Clamp(top + height, y + 1, source.PixelHeight);
            int w = right - x;
            int h = bottom - y;

            var converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = w * 4;
            var pixels = new byte[stride * h];
            converted.CopyPixels(new Int32Rect(x, y, w, h), pixels, stride, 0);

            int block = 9;
            for (int by = 0; by < h; by += block)
            {
                for (int bx = 0; bx < w; bx += block)
                {
                    int maxX = Math.Min(bx + block, w);
                    int maxY = Math.Min(by + block, h);
                    long b = 0, g = 0, r = 0, count = 0;
                    for (int py = by; py < maxY; py++)
                    {
                        int rowOffset = py * stride;
                        for (int px = bx; px < maxX; px++)
                        {
                            int index = rowOffset + px * 4;
                            b += pixels[index];
                            g += pixels[index + 1];
                            r += pixels[index + 2];
                            count++;
                        }
                    }

                    if (count == 0)
                    {
                        continue;
                    }

                    byte avgB = (byte)(b / count);
                    byte avgG = (byte)(g / count);
                    byte avgR = (byte)(r / count);
                    for (int py = by; py < maxY; py++)
                    {
                        int rowOffset = py * stride;
                        for (int px = bx; px < maxX; px++)
                        {
                            int index = rowOffset + px * 4;
                            pixels[index] = avgB;
                            pixels[index + 1] = avgG;
                            pixels[index + 2] = avgR;
                            pixels[index + 3] = 255;
                        }
                    }
                }
            }

            var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        }

        public static Color ParseColor(string? hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex ?? "#E5534B");
            }
            catch
            {
                return Color.FromRgb(0xE5, 0x53, 0x4B);
            }
        }
    }
}
