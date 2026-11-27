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
            BitmapSource sourceBitmap,
            double sourceOriginX = 0,
            double sourceOriginY = 0)
        {
            foreach (var annotation in annotations)
            {
                DrawAnnotation(dc, annotation, sourceBitmap, sourceOriginX, sourceOriginY);
            }
        }

        private static void DrawAnnotation(
            DrawingContext dc,
            Annotation annotation,
            BitmapSource sourceBitmap,
            double sourceOriginX,
            double sourceOriginY)
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
                case AnnotationKind.Ellipse:
                    DrawEllipse(dc, annotation, brush);
                    break;
                case AnnotationKind.Spotlight:
                    DrawSpotlight(dc, annotation, sourceBitmap, sourceOriginX, sourceOriginY);
                    break;
                case AnnotationKind.Redaction:
                    DrawRedaction(dc, annotation, brush);
                    break;
                case AnnotationKind.Text:
                    DrawText(dc, annotation, brush);
                    break;
                case AnnotationKind.Blur:
                    DrawPixelation(dc, annotation, sourceBitmap, sourceOriginX, sourceOriginY);
                    break;
                case AnnotationKind.Zoom:
                    DrawZoom(dc, annotation, sourceBitmap, brush, sourceOriginX, sourceOriginY);
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

        public readonly record struct ArrowGeometry(
            Point Tip,
            Point ShaftEnd,
            Point LeftWing,
            Point RightWing,
            double EffectiveThickness,
            bool HasShaft);

        public static bool TryComputeArrowGeometry(
            Point start,
            Point end,
            double thickness,
            out ArrowGeometry geometry)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.5)
            {
                geometry = default;
                return false;
            }

            double ux = dx / length;
            double uy = dy / length;
            double angle = Math.Atan2(dy, dx);
            double spread = Math.PI / 10; // Acute 18-degree half-angle for sleek pointed tips.
            double baseThickness = Math.Max(1, thickness);
            double desiredHead = Math.Max(11, baseThickness * 3.2 + 4);
            double head = Math.Min(desiredHead, Math.Max(6, length * 0.65));
            double effectiveThickness = Math.Min(baseThickness, Math.Max(1, length * 0.45));
            double baseDepth = head * Math.Cos(spread);
            // Stop the shaft inside the arrowhead base so flat/round caps never poke through the tip or edges.
            double trim = Math.Min(length, baseDepth * 0.85);

            var shaftEnd = new Point(end.X - ux * trim, end.Y - uy * trim);
            var leftWing = new Point(
                end.X - head * Math.Cos(angle - spread),
                end.Y - head * Math.Sin(angle - spread));
            var rightWing = new Point(
                end.X - head * Math.Cos(angle + spread),
                end.Y - head * Math.Sin(angle + spread));

            geometry = new ArrowGeometry(
                end,
                shaftEnd,
                leftWing,
                rightWing,
                effectiveThickness,
                length > trim + 0.25);
            return true;
        }

        public static void DrawArrow(DrawingContext dc, Brush brush, Point start, Point end, double thickness)
        {
            if (!TryComputeArrowGeometry(start, end, thickness, out var arrow))
            {
                return;
            }

            if (arrow.HasShaft)
            {
                var pen = new Pen(brush, arrow.EffectiveThickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Flat
                };
                pen.Freeze();
                dc.DrawLine(pen, start, arrow.ShaftEnd);
            }

            var headGeometry = new StreamGeometry();
            using (var context = headGeometry.Open())
            {
                context.BeginFigure(arrow.Tip, true, true);
                context.LineTo(arrow.LeftWing, true, true);
                context.LineTo(arrow.RightWing, true, true);
            }
            headGeometry.Freeze();
            dc.DrawGeometry(brush, null, headGeometry);
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
            if (annotation.Kind == AnnotationKind.Arrow)
            {
                DrawArrow(dc, brush, start, end, annotation.Thickness);
                return;
            }

            var pen = new Pen(brush, Math.Max(1, annotation.Thickness))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            dc.DrawLine(pen, start, end);
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

        private static void DrawEllipse(DrawingContext dc, Annotation annotation, Brush brush)
        {
            if (annotation.Rect.Width <= 0 || annotation.Rect.Height <= 0)
            {
                return;
            }

            var pen = new Pen(brush, Math.Max(1, annotation.Thickness));
            dc.DrawEllipse(
                null,
                pen,
                new Point(annotation.Rect.Left + annotation.Rect.Width / 2, annotation.Rect.Top + annotation.Rect.Height / 2),
                annotation.Rect.Width / 2,
                annotation.Rect.Height / 2);
        }

        private static void DrawSpotlight(
            DrawingContext dc,
            Annotation annotation,
            BitmapSource sourceBitmap,
            double sourceOriginX = 0,
            double sourceOriginY = 0)
        {
            if (annotation.Rect.Width < 2 || annotation.Rect.Height < 2)
            {
                return;
            }

            var outer = new RectangleGeometry(new Rect(sourceOriginX, sourceOriginY, sourceBitmap.PixelWidth, sourceBitmap.PixelHeight));
            var hole = new EllipseGeometry(
                new Point(annotation.Rect.Left + annotation.Rect.Width / 2, annotation.Rect.Top + annotation.Rect.Height / 2),
                Math.Max(2, annotation.Rect.Width / 2),
                Math.Max(2, annotation.Rect.Height / 2));
            var dim = new CombinedGeometry(GeometryCombineMode.Exclude, outer, hole);
            dim.Freeze();
            var dimBrush = new SolidColorBrush(Color.FromArgb(175, 0, 0, 0));
            dimBrush.Freeze();
            dc.DrawGeometry(dimBrush, null, dim);
        }

        private static void DrawZoom(
            DrawingContext dc,
            Annotation annotation,
            BitmapSource sourceBitmap,
            Brush brush,
            double sourceOriginX = 0,
            double sourceOriginY = 0)
        {
            var rect = annotation.Rect;
            if (rect.Width < 8 || rect.Height < 8 || sourceBitmap.PixelWidth < 2 || sourceBitmap.PixelHeight < 2)
            {
                return;
            }

            double diameter = Math.Min(rect.Width, rect.Height);
            double radius = diameter / 2.0;
            var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
            var lensRect = new Rect(center.X - radius, center.Y - radius, diameter, diameter);

            const double zoomFactor = 2.0;
            int w = Math.Clamp((int)Math.Ceiling(diameter / zoomFactor), 1, sourceBitmap.PixelWidth);
            int h = Math.Clamp((int)Math.Ceiling(diameter / zoomFactor), 1, sourceBitmap.PixelHeight);
            int srcX = Math.Clamp((int)Math.Floor(center.X - sourceOriginX - w / 2.0), 0, Math.Max(0, sourceBitmap.PixelWidth - w));
            int srcY = Math.Clamp((int)Math.Floor(center.Y - sourceOriginY - h / 2.0), 0, Math.Max(0, sourceBitmap.PixelHeight - h));

            var crop = new CroppedBitmap(sourceBitmap, new Int32Rect(srcX, srcY, w, h));
            crop.Freeze();

            var clip = new EllipseGeometry(center, radius, radius);
            clip.Freeze();

            dc.PushClip(clip);
            dc.DrawImage(crop, lensRect);
            dc.Pop();

            var shadowBrush = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            shadowBrush.Freeze();
            var outerShadow = new Pen(shadowBrush, Math.Max(2, annotation.Thickness + 1.5));
            outerShadow.Freeze();
            dc.DrawEllipse(null, outerShadow, center, radius, radius);

            var borderPen = new Pen(brush, Math.Max(2, annotation.Thickness));
            borderPen.Freeze();
            dc.DrawEllipse(null, borderPen, center, radius, radius);
        }

        private static void DrawRedaction(DrawingContext dc, Annotation annotation, Brush brush)
        {
            if (annotation.Points.Count > 1)
            {
                var pen = new Pen(brush, Math.Max(8, annotation.Thickness))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                for (int index = 1; index < annotation.Points.Count; index++)
                {
                    dc.DrawLine(pen, annotation.Points[index - 1], annotation.Points[index]);
                }
                return;
            }

            if (annotation.Rect.Width < 2 || annotation.Rect.Height < 2)
            {
                return;
            }

            dc.DrawRectangle(brush, null, annotation.Rect);
        }

        public static Rect GetAnnotationBounds(Annotation annotation)
        {
            switch (annotation.Kind)
            {
                case AnnotationKind.Line:
                case AnnotationKind.Arrow:
                    if (annotation.Points.Count < 2)
                    {
                        return Rect.Empty;
                    }
                    var lineStart = annotation.Points[0];
                    var lineEnd = annotation.Points[^1];
                    var lineRect = new Rect(lineStart, lineEnd);
                    double pad = Math.Max(4, annotation.Thickness);
                    lineRect.Inflate(pad, pad);
                    return lineRect;

                case AnnotationKind.Pen:
                case AnnotationKind.Highlighter:
                case AnnotationKind.Redaction:
                    if (annotation.Points.Count == 0)
                    {
                        return annotation.Rect;
                    }
                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;
                    foreach (var p in annotation.Points)
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                    }
                    var strokeRect = new Rect(minX, minY, maxX - minX, maxY - minY);
                    double strokePad = Math.Max(4, annotation.Thickness / 2 + 2);
                    strokeRect.Inflate(strokePad, strokePad);
                    return strokeRect;

                case AnnotationKind.Step:
                    double radius = Math.Max(12, annotation.Thickness * 5);
                    var center = annotation.Rect.TopLeft;
                    return new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2);

                case AnnotationKind.Text:
                    if (string.IsNullOrEmpty(annotation.Text))
                    {
                        return annotation.Rect;
                    }
                    var formatted = new FormattedText(
                        annotation.Text,
                        annotation.IsRtl ? CultureInfo.GetCultureInfo("ar-SA") : CultureInfo.GetCultureInfo("en-US"),
                        annotation.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        RegularTypeface,
                        Math.Max(8, annotation.FontSize),
                        Brushes.White,
                        1.0)
                    {
                        MaxTextWidth = Math.Max(40, annotation.Rect.Width > 0 ? annotation.Rect.Width : 2000)
                    };
                    return new Rect(annotation.Rect.X, annotation.Rect.Y, Math.Max(12, formatted.Width), Math.Max(12, formatted.Height));

                default:
                    return annotation.Rect;
            }
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
            BitmapSource sourceBitmap,
            double sourceOriginX = 0,
            double sourceOriginY = 0)
        {
            var rect = annotation.Rect;
            if (rect.Width < 2 || rect.Height < 2)
            {
                return;
            }

            annotation.PixelPreview ??= BuildPixelation(sourceBitmap, rect, sourceOriginX, sourceOriginY);
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

        public static BitmapSource? BuildPixelation(
            BitmapSource source,
            Rect area,
            double sourceOriginX = 0,
            double sourceOriginY = 0)
        {
            int left = (int)Math.Floor(area.X - sourceOriginX);
            int top = (int)Math.Floor(area.Y - sourceOriginY);
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
