using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CryoUtils.Models;
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
        private string _colorHex = AppSettings.DefaultAnnotationColor;
        private double _thickness = 3;
        private Popup? _colorPopup;
        private readonly Stack<UIElement> _redo = new();
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
            var settings = SettingsService.Instance.Current;
            _colorHex = string.IsNullOrWhiteSpace(settings.AnnotationColor)
                ? AppSettings.DefaultAnnotationColor
                : settings.AnnotationColor;
            _thickness = Math.Clamp(settings.AnnotationThickness, 1, 24);
            Loaded += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(initialPath))
                {
                    LoadImage(initialPath);
                }
                UpdateChromeState();
                Focus();
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
            _redo.Clear();
            _activeShape = null;
            _activePen = null;
            _activeLine = null;
            _activeArrowHead = null;
            _cropRect = null;
            TxtFile.Text = path ?? "";
            TxtDimensions.Text = $"{image.PixelWidth} × {image.PixelHeight}";
            TxtStatus.Text = LocalizationService.Get("Editor_Hint");
            UpdateChromeState();
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
            UpdateChromeState();
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
            UpdateChromeState();
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
                _redo.Clear();
                _activePen = new Polyline
                {
                    Stroke = CreateStrokeBrush(),
                    StrokeThickness = _thickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                _activePen.Points.Add(point);
                InkCanvas.Children.Add(_activePen);
                _activeShape = _activePen;
                UpdateChromeState();
                return;
            }

            if (_tool == EditorTool.Arrow)
            {
                _redo.Clear();
                var brush = CreateStrokeBrush();
                var canvas = new Canvas();
                _activeLine = new Line
                {
                    Stroke = brush,
                    StrokeThickness = _thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Flat
                };
                _activeArrowHead = new Polygon
                {
                    Fill = brush
                };
                canvas.Children.Add(_activeLine);
                canvas.Children.Add(_activeArrowHead);
                InkCanvas.Children.Add(canvas);
                _activeShape = canvas;
                UpdateChromeState();
                return;
            }

            _redo.Clear();
            var brushShape = _tool == EditorTool.Redact
                ? Brushes.Black
                : CreateStrokeBrush();
            var rectangle = new Rectangle
            {
                Stroke = _tool == EditorTool.Redact ? null : brushShape,
                Fill = _tool == EditorTool.Redact ? brushShape : null,
                StrokeThickness = _thickness
            };
            InkCanvas.Children.Add(rectangle);
            _activeShape = rectangle;
            UpdateChromeState();
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
                if (!AnnotationRenderer.TryComputeArrowGeometry(_start, point, _thickness, out var arrow))
                {
                    _activeLine!.Visibility = Visibility.Collapsed;
                    _activeArrowHead!.Visibility = Visibility.Collapsed;
                    return;
                }

                _activeLine!.Visibility = arrow.HasShaft ? Visibility.Visible : Visibility.Collapsed;
                _activeLine.StrokeThickness = arrow.EffectiveThickness;
                _activeLine.X1 = _start.X;
                _activeLine.Y1 = _start.Y;
                _activeLine.X2 = arrow.ShaftEnd.X;
                _activeLine.Y2 = arrow.ShaftEnd.Y;
                _activeArrowHead!.Visibility = Visibility.Visible;
                _activeArrowHead.Points = new PointCollection
                {
                    arrow.Tip,
                    arrow.LeftWing,
                    arrow.RightWing
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
            UpdateChromeState();
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
                _redo.Clear();
                var label = new TextBlock
                {
                    Text = text,
                    FontSize = Math.Max(12, _thickness * 6),
                    Foreground = CreateStrokeBrush(),
                    Background = Brushes.Transparent
                };
                Canvas.SetLeft(label, left);
                Canvas.SetTop(label, top);
                InkCanvas.Children.Add(label);
                UpdateChromeState();
            }
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (InkCanvas.Children.Count > 0)
            {
                int last = InkCanvas.Children.Count - 1;
                var removed = InkCanvas.Children[last];
                InkCanvas.Children.RemoveAt(last);
                if (!ReferenceEquals(removed, _cropRect))
                {
                    _redo.Push(removed);
                }
                else
                {
                    _cropRect = null;
                }
                UpdateChromeState();
            }
        }

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            if (_redo.Count == 0)
            {
                return;
            }

            var restored = _redo.Pop();
            InkCanvas.Children.Add(restored);
            UpdateChromeState();
        }

        private void BtnColor_Click(object sender, RoutedEventArgs e)
        {
            if (_colorPopup == null)
            {
                BuildColorPopup();
            }

            if (_colorPopup == null)
            {
                return;
            }

            _colorPopup.PlacementTarget = BtnColor;
            _colorPopup.IsOpen = true;
        }

        private void BuildColorPopup()
        {
            var panel = new WrapPanel { Width = 188 };
            string[] palette =
            {
                "#E5534B", "#F2C94C", "#3FBF7F", "#4C8DF6",
                "#FFFFFF", "#141414", "#F2994A", "#2B88D8"
            };

            foreach (string hex in palette)
            {
                var button = new Button
                {
                    Style = (Style)FindResource("SecondaryButton"),
                    Width = 38,
                    Height = 30,
                    Padding = new Thickness(0),
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(AnnotationRenderer.ParseColor(hex)),
                    BorderBrush = ThemeService.GetBrush("BorderStrongBrush"),
                    ToolTip = hex
                };
                button.Click += (_, _) =>
                {
                    _colorHex = hex;
                    UpdateChromeState();
                    if (_colorPopup != null)
                    {
                        _colorPopup.IsOpen = false;
                    }
                };
                panel.Children.Add(button);
            }

            var customButton = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Content = LocalizationService.Get("Capture_Color"),
                Margin = new Thickness(2, 6, 2, 2),
                Padding = new Thickness(8, 4, 8, 4)
            };
            customButton.Click += (_, _) =>
            {
                if (_colorPopup != null)
                {
                    _colorPopup.IsOpen = false;
                }
                PickCustomColor();
            };
            panel.Children.Add(customButton);

            var container = new Border
            {
                Background = ThemeService.GetBrush("CardBackgroundBrush"),
                BorderBrush = ThemeService.GetBrush("BorderStrongBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4),
                Child = panel
            };

            _colorPopup = new Popup
            {
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = container
            };
        }

        private void PickCustomColor()
        {
            string? hex = ColorDialogHelper.PickColorHex(this, _colorHex);
            if (string.IsNullOrEmpty(hex))
            {
                return;
            }

            _colorHex = hex;
            UpdateChromeState();
        }

        private void BtnThickDown_Click(object sender, RoutedEventArgs e) => AdjustThickness(-1);

        private void BtnThickUp_Click(object sender, RoutedEventArgs e) => AdjustThickness(1);

        private void CanvasHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                return;
            }

            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.5 : 1;
            AdjustThickness(e.Delta > 0 ? step : -step);
            e.Handled = true;
        }

        private void AdjustThickness(double delta)
        {
            _thickness = Math.Clamp(Math.Round(_thickness + delta, 1), 1, 24);
            UpdateChromeState();
        }

        private SolidColorBrush CreateStrokeBrush()
        {
            var brush = new SolidColorBrush(AnnotationRenderer.ParseColor(_colorHex));
            brush.Freeze();
            return brush;
        }

        private void UpdateChromeState()
        {
            UpdateToolButtons();
            if (ColorSwatch != null)
            {
                ColorSwatch.Background = CreateStrokeBrush();
            }
            if (TxtThickness != null)
            {
                TxtThickness.Text = LocalizationService.Format("Capture_SizePreview", Math.Round(_thickness, 1));
            }
            if (BtnUndo != null)
            {
                BtnUndo.IsEnabled = InkCanvas.Children.Count > 0;
            }
            if (BtnRedo != null)
            {
                BtnRedo.IsEnabled = _redo.Count > 0;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox)
            {
                return;
            }

            bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (control && e.Key == Key.Z)
            {
                BtnUndo_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (control && e.Key == Key.Y)
            {
                BtnRedo_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (control && e.Key == Key.S)
            {
                BtnSave_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (control && e.Key == Key.C)
            {
                BtnCopy_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (control || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                return;
            }

            if (e.Key == Key.Enter && _tool == EditorTool.Crop && _cropWidth >= 2 && _cropHeight >= 2)
            {
                BtnApplyCrop_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _cropRect != null)
            {
                InkCanvas.Children.Remove(_cropRect);
                _cropRect = null;
                _cropWidth = 0;
                _cropHeight = 0;
                UpdateChromeState();
                e.Handled = true;
                return;
            }

            EditorTool? nextTool = e.Key switch
            {
                Key.D1 or Key.NumPad1 or Key.P => EditorTool.Pen,
                Key.D2 or Key.NumPad2 or Key.A => EditorTool.Arrow,
                Key.D3 or Key.NumPad3 or Key.R => EditorTool.Box,
                Key.D4 or Key.NumPad4 or Key.K => EditorTool.Redact,
                Key.D5 or Key.NumPad5 or Key.T => EditorTool.Text,
                Key.D6 or Key.NumPad6 or Key.C => EditorTool.Crop,
                _ => null
            };
            if (nextTool.HasValue)
            {
                if (nextTool.Value == EditorTool.Crop)
                {
                    BtnCrop_Click(this, new RoutedEventArgs());
                }
                else
                {
                    _tool = nextTool.Value;
                    if (_cropRect != null)
                    {
                        InkCanvas.Children.Remove(_cropRect);
                        _cropRect = null;
                    }
                    TxtStatus.Text = LocalizationService.Get("Editor_Hint");
                    UpdateChromeState();
                }
                e.Handled = true;
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
            var previousCropVisibility = _cropRect?.Visibility;
            if (_cropRect != null)
            {
                _cropRect.Visibility = Visibility.Collapsed;
            }
            var ink = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            ink.Render(InkCanvas);
            ink.Freeze();
            if (_cropRect != null && previousCropVisibility.HasValue)
            {
                _cropRect.Visibility = previousCropVisibility.Value;
            }

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
