using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CryoUtils.Views
{
    public static class CryoIcons
    {
        public const string Select = "M5,3 L16,10.5 L10.7,11.8 L13,17 L10.8,18 L8.6,12.8 L5,15.6 Z";
        public const string Pen = "M3,17 L4.3,13.3 L13.4,4.2 L16,6.8 L6.9,15.9 Z M12.6,5 L15.2,7.6";
        public const string Line = "M3,17 L17,3";
        public const string Arrow = "M3,17 L17,3 M8.2,3 L17,3 L17,11.8";
        public const string Rectangle = "M3,4 L17,4 L17,16 L3,16 Z";
        public const string Highlighter = "M4,13 L9.2,3 L14,5.3 L8.8,15.3 Z M3,17 L13,17";
        public const string Text = "M3,5 L17,5 M10,5 L10,17 M7,17 L13,17";
        public const string Blur = "M4,4 L8,4 L8,8 L4,8 Z M10,4 L14,4 L14,8 L10,8 Z M4,10 L8,10 L8,14 L4,14 Z M10,10 L14,10 L14,14 L10,14 Z";
        public const string Undo = "M8,5 L4,9 L8,13 M4,9 L11.5,9 A4.2,4.2 0 0 1 11.5,17.4 L8.5,17.4";
        public const string Redo = "M12,5 L16,9 L12,13 M16,9 L8.5,9 A4.2,4.2 0 0 0 8.5,17.4 L11.5,17.4";
        public const string Copy = "M7,3 L16,3 L16,13 L9,13 M4,6 L4,18 L12,18 L12,13";
        public const string Save = "M4,3 L14.5,3 L17,5.5 L17,17 L3,17 L3,3 Z M6,3 L6,8 L13,8 L13,3 M6,13 L14,13 L14,17 L6,17 Z";
        public const string Record = "M10,3.5 A6.5,6.5 0 1 0 10,16.5 A6.5,6.5 0 1 0 10,3.5 Z M10,7 A3,3 0 1 0 10,13 A3,3 0 1 0 10,7 Z";
        public const string Ocr = "M4,7 L4,4 L7,4 M13,4 L16,4 L16,7 M16,13 L16,16 L13,16 M7,16 L4,16 L4,13 M7,10 L13,10 M7,7 L13,7 M7,13 L11,13";
        public const string Pin = "M8,3 L12,3 L11.2,8 L14,11 L6,11 L8.8,8 Z M10,11 L10,17";
        public const string Print = "M5,7 L5,3 L15,3 L15,7 M4,7 L16,7 L16,13 L13,13 M7,13 L4,13 L4,17 L16,17 L16,13 L13,13 M7,13 L13,13 L13,17 L7,17 Z";
        public const string Close = "M4,4 L16,16 M16,4 L4,16";
        public const string Droplet = "M10,3 C10,3 5,9.2 5,12 A5,5 0 0 0 15,12 C15,9.2 10,3 10,3 Z";
        public const string Compress = "M3,3 L8,3 L8,8 L3,8 Z M12,12 L17,12 L17,17 L12,17 Z M10,10 L16,4 M16,4 L16,8 M16,4 L12,4";
        public const string Clipboard = "M6,4 L14,4 L14,17 L6,17 Z M8,4 L8,2 L12,2 L12,4 M8,8 L12,8 M8,11 L12,11";
        public const string Update = "M10,3 A7,7 0 1 1 3,10 M3,4 L3,10 L9,10";
        public const string Settings = "M7,2 L9,2 L9.3,3.8 A5,5 0 0 1 10.8,4.7 L12.5,3.8 L13.9,5.2 L13,6.9 A5,5 0 0 1 13.9,8.4 L15.7,8.7 L15.7,10.7 L13.9,11 A5,5 0 0 1 13,12.5 L13.9,14.2 L12.5,15.6 L10.8,14.7 A5,5 0 0 1 9.3,15.6 L9,17.4 L7,17.4 L6.7,15.6 A5,5 0 0 1 5.2,14.7 L3.5,15.6 L2.1,14.2 L3,12.5 A5,5 0 0 1 2.1,11 L0.3,10.7 L0.3,8.7 L2.1,8.4 A5,5 0 0 1 3,6.9 L2.1,5.2 L3.5,3.8 L5.2,4.7 A5,5 0 0 1 6.7,3.8 Z M8,6 A3.5,3.5 0 1 0 8,13 A3.5,3.5 0 1 0 8,6 Z";
        public const string Repo = "M4,4 L9,4 L9,16 L4,16 Z M11,4 L16,4 L16,16 L11,16 Z M6,9 L7,9 M13,9 L14,9";
        public const string Step = "M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 Z";
        public const string QrCode = "M3,3 L8,3 L8,8 L3,8 Z M5,5 L6,5 L6,6 L5,6 Z M12,3 L17,3 L17,8 L12,8 Z M14,5 L15,5 L15,6 L14,6 Z M3,12 L8,12 L8,17 L3,17 Z M5,14 L6,14 L6,15 L5,15 Z M12,12 L14,12 L14,14 L12,14 Z M15,12 L17,12 L17,15 L15,15 Z M12,15 L15,15 L15,17 L12,17 Z M16,16 L17,16 L17,17 L16,17 Z";
        public const string Contrast = "M10,2 A8,8 0 1 0 10,18 A8,8 0 0 0 10,2 Z M10,4 L10,16 A6,6 0 0 1 10,4 Z";
        public const string Home = "M3,9.5 L10,3 L17,9.5 L17,17 L12,17 L12,12 L8,12 L8,17 L3,17 Z";
        public const string Keyboard = "M2,5 L18,5 L18,15 L2,15 Z M5,8 L7,8 M9,8 L11,8 M13,8 L15,8 M5,11 L7,11 M9,11 L15,11";
        public const string Info = "M10,2 A8,8 0 1 0 10,18 A8,8 0 0 0 10,2 Z M10,8 L10,14 M10,5.5 L10,6.5";
        public const string Folder = "M2,4 L7,4 L9,6 L18,6 L18,16 L2,16 Z";
        public const string Trash = "M4,6 L16,6 M8,6 L8,4 L12,4 L12,6 M6,6 L6,16 L14,16 L14,6 M8,9 L8,13 M12,9 L12,13";
        public const string Check = "M3.5,10 L7.5,14 L16.5,5";
        public const string Tools = "M3,3 L9,3 L9,9 L3,9 Z M13,3 L19,3 L19,9 L13,9 Z M3,13 L9,13 L9,19 L3,19 Z M13,13 L19,13 L19,19 L13,19 Z";
        public const string Menu = "M3,5 L17,5 M3,10 L17,10 M3,15 L17,15";
        public const string Power = "M10,2.5 L10,9.5 M5.8,5.5 A6.5,6.5 0 1 0 14.2,5.5";

        public static Geometry GeometryFor(string data, bool filled = false)
        {
            var geometry = System.Windows.Media.Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }

        public static Path Create(string data, double size, bool stroke, string resourceKey = "IconBrush")
        {
            var path = new Path
            {
                Data = GeometryFor(data),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                SnapsToDevicePixels = true
            };

            if (stroke)
            {
                path.SetResourceReference(Shape.StrokeProperty, resourceKey);
                path.Fill = Brushes.Transparent;
            }
            else
            {
                path.SetResourceReference(Shape.FillProperty, resourceKey);
            }

            return path;
        }
    }
}
