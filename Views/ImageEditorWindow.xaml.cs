using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CryoUtils.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using IOPath = System.IO.Path;

namespace CryoUtils.Views
{
    public partial class ImageEditorWindow : Window
    {
        private enum EditorTool
        {
            Pen,
            Arrow,
            Box,
            Redact,
            Text,
            Crop
        }

        private BitmapSource? _image;
        private string? _sourcePath;
        private EditorTool _tool = EditorTool.Pen;
        private bool _drawing;
        private Point _start;
        private Point _current;
        private FrameworkElement? _activeShape;
        private Polyline? _activePen;
        private Line? _activeLine;
        private Polygon? _activeArrowHead;
        private Rectangle? _cropRect;
        private int _cropStartX;
        private int _cropStartY;
        private int _cropWidth;
        private int _cropHeight;

        public ImageEditorWindow(string? initialPath = null)
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            Loaded += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(initialPath))
                {
                    LoadImage(initialPath);
                }
                UpdateToolButtons();
            };
        }

        private void LoadImage(string path)
        {
            try
            {
                var image = ImageIO.LoadThumbnail(path, int.MaxValue);
                SetImage(image, path);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
            }
        }

        private void SetImage(BitmapSource image, string? path = null)
        {
            _image = image;
            _sourcePath = path;
            PreviewImage.Source = image;
            PreviewImage.Width = image.PixelWidth;
            PreviewImage.Height = image.PixelHeight;
            CanvasHost.Width = image.PixelWidth;
            CanvasHost.Height = image.PixelHeight;
            InkCanvas.Width = image.PixelWidth;
            InkCanvas.Height = image.PixelHeight;
            InkCanvas.Children.Clear();
            _activeShape = null;
            _activePen = null;
            _activeLine = null;
            _activeArrowHead = null;
            _cropRect = null;
            TxtFile.Text = path ?? "";
            TxtDimensions.Text = $"{image.PixelWidth} × {image.PixelHeight}";
            TxtStatus.Text = LocalizationService.Get("Editor_Hint");
            UpdateToolButtons();
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Get("Editor_Open"),
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.jfif|All files|*.*"
            };
            if (dialog.ShowDialog(this) == true)
            {
                LoadImage(dialog.FileName);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveWithDialog(false);

        private void BtnConvert_Click(object sender, RoutedEventArgs e) => SaveWithDialog(true);

        private void SaveWithDialog(bool convert)
        {
            if (_image == null)
            {
                TxtStatus.Text = LocalizationService.Get("Editor_NoImage");
                return;
            }

            var flat = Flatten();
            string initial = string.IsNullOrEmpty(_sourcePath)
                ? IOPath.Combine(
                    SettingsService.Instance.EffectiveSaveFolder,
                    ScreenCaptureService.CreateTimestampedFileName(".png"))
                : _sourcePath;
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get(convert ? "Editor_Convert" : "Editor_Save"),
                Filter = convert
                    ? "PNG|*.png|JPEG|*.jpg|WebP|*.webp|BMP|*.bmp"
                    : "PNG|*.png|JPEG|*.jpg|WebP|*.webp|BMP|*.bmp|All files|*.*",
                FilterIndex = ExtensionIndex(IOPath.GetExtension(initial)),
                DefaultExt = ".png",
                AddExtension = true,
                FileName = IOPath.GetFileName(initial),
                InitialDirectory = IOPath.GetDirectoryName(initial) ?? SettingsService.Instance.EffectiveSaveFolder
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                ImageIO.SaveBitmap(flat, dialog.FileName);
                TxtStatus.Text = LocalizationService.Format("Editor_Saved", dialog.FileName);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
            }
        }

        private static int ExtensionIndex(string extension) => extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => 2,
            ".webp" => 3,
            ".bmp" => 4,
            _ => 1
        };

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_image == null)
            {
                return;
            }
            try
            {
                Clipboard.SetImage(Flatten());
                TxtStatus.Text = LocalizationService.Get("Editor_Copied");
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
            }
        }

        private void BtnRotateLeft_Click(object sender, RoutedEventArgs e) => Rotate(-90);

        private void BtnRotateRight_Click(object sender, RoutedEventArgs e) => Rotate(90);

        private void Rotate(double angle)
        {
            if (_image == null)
            {
                return;
            }
            var rotated = new TransformedBitmap(Flatten(), new RotateTransform(angle));
            rotated.Freeze();
            SetImage(rotated, _sourcePath);
            TxtStatus.Text = LocalizationService.Get("Editor_Hint");
        }

        private void BtnResize_Click(object sender, RoutedEventArgs e)
        {
            if (_image == null)
            {
                return;
            }

            var dialog = new ImageResizeWindow(_image.PixelWidth, _image.PixelHeight)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var flat = Flatten();
            var resized = new TransformedBitmap(
                flat,
                new ScaleTransform(
                    dialog.TargetWidth / (double)flat.PixelWidth,
                    dialog.TargetHeight / (double)flat.PixelHeight));
            resized.Freeze();
            SetImage(resized, _sourcePath);
        }

        private void BtnCrop_Click(object sender, RoutedEventArgs e)
        {
            _tool = EditorTool.Crop;
            if (_cropRect != null)
            {
                InkCanvas.Children.Remove(_cropRect);
            }
            _cropRect = null;
            TxtStatus.Text = LocalizationService.Get("Editor_CropReady");
            UpdateToolButtons();
        }

        private void BtnApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_image == null || _cropWidth < 2 || _cropHeight < 2)
            {
                TxtStatus.Text = LocalizationService.Get("Editor_CropReady");
                return;
            }

            var cropped = ScreenCaptureService.Crop(
                Flatten(),
                new Int32Rect(_cropStartX, _cropStartY, _cropWidth, _cropHeight));
            SetImage(cropped, _sourcePath);
            TxtStatus.Text = LocalizationService.Get("Editor_Hint");
        }

        private void Tool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string tag)
            {
                return;
            }

            _tool = tag switch
            {
                "Arrow" => EditorTool.Arrow,
                "Box" => EditorTool.Box,
                "Redact" => EditorTool.Redact,
                "Text" => EditorTool.Text,
                _ => EditorTool.Pen
            };
            if (_cropRect != null)
            {
                InkCanvas.Children.Remove(_cropRect);
            }
            _cropRect = null;
            TxtStatus.Text = LocalizationService.Get("Editor_Hint");
            UpdateToolButtons();
        }

        private void UpdateToolButtons()
        {
            var active = ThemeService.GetBrush("AccentTextBrush");
            var normal = ThemeService.GetBrush("TextSecondaryBrush");
            bool crop = _tool == EditorTool.Crop;
            BtnCrop.FontWeight = crop ? FontWeights.SemiBold : FontWeights.Normal;
            BtnCrop.Foreground = crop ? active : normal;
            foreach (var button in new[] { BtnPen, BtnArrow, BtnBox, BtnRedact, BtnText })
            {
                bool selected = !crop && string.Equals(button.Tag as string, _tool.ToString(), StringComparison.Ordinal);
                button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                button.Foreground = selected ? active : normal;
            }
        }

        private void InkCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_image == null)
            {
                return;
            }

            var point = e.GetPosition(InkCanvas);
            _start = point;
            _current = point;
            if (_tool == EditorTool.Text)
            {
                ShowTextEditor(point);
                return;
            }

            _drawing = true;
            InkCanvas.CaptureMouse();
            if (_tool == EditorTool.Crop)
            {
                _cropRect = new Rectangle
                {
                    Stroke = ThemeService.GetBrush("AccentBrush"),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    Fill = new SolidColorBrush(Color.FromArgb(35, 43, 136, 216))
                };
                InkCanvas.Children.Add(_cropRect);
                Canvas.SetLeft(_cropRect, point.X);
                Canvas.SetTop(_cropRect, point.Y);
                return;
            }

            if (_tool == EditorTool.Pen)
            {
                _activePen = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B)),
                    StrokeThickness = 3,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                _activePen.Points.Add(point);
                InkCanvas.Children.Add(_activePen);
                _activeShape = _activePen;
                return;
            }

            if (_tool == EditorTool.Arrow)
            {
                var canvas = new Canvas();
                _activeLine = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B)),
                    StrokeThickness = 3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                _activeArrowHead = new Polygon
                {
                    Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B))
                };
                canvas.Children.Add(_activeLine);
                canvas.Children.Add(_activeArrowHead);
                InkCanvas.Children.Add(canvas);
                _activeShape = canvas;
                return;
            }

            var color = _tool == EditorTool.Redact
                ? Colors.Black
                : Color.FromRgb(0xE5, 0x53, 0x4B);
            var rectangle = new Rectangle
            {
                Stroke = _tool == EditorTool.Redact ? null : new SolidColorBrush(color),
                Fill = _tool == EditorTool.Redact ? new SolidColorBrush(color) : null,
                StrokeThickness = 3
            };
            InkCanvas.Children.Add(rectangle);
            _activeShape = rectangle;
        }

        private void InkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_drawing)
            {
                return;
            }

            var point = e.GetPosition(InkCanvas);
            _current = point;
            if (_tool == EditorTool.Crop && _cropRect != null)
            {
                UpdateCropRect(point);
                return;
            }

            if (_activePen != null)
            {
                _activePen.Points.Add(point);
                return;
            }

            if (_activeShape == null)
            {
                return;
            }

            if (_tool == EditorTool.Arrow && _activeShape is Canvas arrowCanvas)
            {
                _activeLine!.X1 = _start.X;
                _activeLine.Y1 = _start.Y;
                _activeLine.X2 = point.X;
                _activeLine.Y2 = point.Y;
                double angle = Math.Atan2(point.Y - _start.Y, point.X - _start.X);
                double head = 14;
                double spread = Math.PI / 7;
                _activeArrowHead!.Points = new PointCollection
                {
                    point,
                    new Point(point.X - head * Math.Cos(angle - spread), point.Y - head * Math.Sin(angle - spread)),
                    new Point(point.X - head * Math.Cos(angle + spread), point.Y - head * Math.Sin(angle + spread))
                };
                return;
            }

            if (_activeShape is Rectangle rectangle)
            {
                Canvas.SetLeft(rectangle, Math.Min(_start.X, point.X));
                Canvas.SetTop(rectangle, Math.Min(_start.Y, point.Y));
                rectangle.Width = Math.Abs(point.X - _start.X);
                rectangle.Height = Math.Abs(point.Y - _start.Y);
            }
        }

        private void InkCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_drawing)
            {
                return;
            }

            _drawing = false;
            InkCanvas.ReleaseMouseCapture();
            if (_tool == EditorTool.Crop && _cropRect != null)
            {
                UpdateCropRect(e.GetPosition(InkCanvas));
                return;
            }

            if (_tool == EditorTool.Pen && _activePen != null && _activePen.Points.Count < 2)
            {
                InkCanvas.Children.Remove(_activePen);
            }
            else if (_activeShape is Rectangle rectangle &&
                     (rectangle.Width < 2 || rectangle.Height < 2))
            {
                InkCanvas.Children.Remove(rectangle);
            }

            _activeShape = null;
            _activePen = null;
            _activeLine = null;
            _activeArrowHead = null;
        }

        private void UpdateCropRect(Point point)
        {
            if (_cropRect == null)
            {
                return;
            }

            _cropStartX = (int)Math.Round(Math.Max(0, Math.Min(_start.X, point.X)));
            _cropStartY = (int)Math.Round(Math.Max(0, Math.Min(_start.Y, point.Y)));
            _cropWidth = (int)Math.Round(Math.Min(
                Math.Abs(point.X - _start.X),
                Math.Max(0, (_image?.PixelWidth ?? 0) - _cropStartX)));
            _cropHeight = (int)Math.Round(Math.Min(
                Math.Abs(point.Y - _start.Y),
                Math.Max(0, (_image?.PixelHeight ?? 0) - _cropStartY)));
            Canvas.SetLeft(_cropRect, _cropStartX);
            Canvas.SetTop(_cropRect, _cropStartY);
            _cropRect.Width = _cropWidth;
            _cropRect.Height = _cropHeight;
        }

        private void ShowTextEditor(Point point)
        {
            var editor = new TextBox
            {
                Width = 260,
                MinHeight = 30,
                FontSize = 20,
                Background = ThemeService.GetBrush("InputBackgroundBrush"),
                Foreground = ThemeService.GetBrush("TextPrimaryBrush"),
                BorderBrush = ThemeService.GetBrush("AccentBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2)
            };
            Canvas.SetLeft(editor, point.X);
            Canvas.SetTop(editor, point.Y);
            InkCanvas.Children.Add(editor);
            editor.Focus();
            editor.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    CommitText(editor);
                    args.Handled = true;
                }
            };
            editor.LostKeyboardFocus += (_, _) => CommitText(editor);
        }

        private void CommitText(TextBox editor)
        {
            if (!InkCanvas.Children.Contains(editor))
            {
                return;
            }

            string text = editor.Text.Trim();
            double left = Canvas.GetLeft(editor);
            double top = Canvas.GetTop(editor);
            InkCanvas.Children.Remove(editor);
            if (text.Length > 0)
            {
                var label = new TextBlock
                {
                    Text = text,
                    FontSize = 20,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x53, 0x4B)),
                    Background = Brushes.Transparent
                };
                Canvas.SetLeft(label, left);
                Canvas.SetTop(label, top);
                InkCanvas.Children.Add(label);
            }
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (InkCanvas.Children.Count > 0)
            {
                InkCanvas.Children.RemoveAt(InkCanvas.Children.Count - 1);
            }
        }

        private BitmapSource Flatten()
        {
            if (_image == null)
            {
                throw new InvalidOperationException(LocalizationService.Get("Editor_NoImage"));
            }

            int width = _image.PixelWidth;
            int height = _image.PixelHeight;
            var ink = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            ink.Render(InkCanvas);
            ink.Freeze();

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(_image, new Rect(0, 0, width, height));
                dc.DrawImage(ink, new Rect(0, 0, width, height));
            }
            var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            result.Render(visual);
            result.Freeze();
            return result;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
