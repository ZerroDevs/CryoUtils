using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class LiveDrawWindow : Window
    {
        private readonly DispatcherTimer _fadeTimer;
        private LiveInkKind _tool = LiveInkKind.Pen;
        private bool _drawing;
        private bool _capturing;
        private Point _start;
        private List<Point> _points = new();
        private TextBox? _textEditor;

        public LiveDrawWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _fadeTimer.Tick += (_, _) =>
            {
                if (ChkFade.IsChecked == true && Ink.RemoveOlderThan(TimeSpan.FromSeconds(5)))
                {
                    Ink.InvalidateVisual();
                }
            };
            _fadeTimer.Start();
            UpdateToolButtons();
            Loaded += (_, _) =>
            {
                Activate();
                Focus();
                Keyboard.Focus(Root);
            };
            Closed += (_, _) => _fadeTimer.Stop();
        }

        private void Tool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag &&
                Enum.TryParse<LiveInkKind>(tag, out var kind))
            {
                _tool = kind;
                UpdateToolButtons();
            }
        }

        private void UpdateToolButtons()
        {
            var active = ThemeService.GetBrush("AccentTextBrush");
            var normal = ThemeService.GetBrush("TextSecondaryBrush");
            foreach (var button in new[] { BtnPen, BtnArrow, BtnRectangle, BtnText })
            {
                button.FontWeight = string.Equals(button.Tag as string, _tool.ToString(), StringComparison.Ordinal)
                    ? FontWeights.SemiBold
                    : FontWeights.Normal;
                button.Foreground = string.Equals(button.Tag as string, _tool.ToString(), StringComparison.Ordinal)
                    ? active
                    : normal;
            }
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Toolbar.IsMouseOver || _capturing || _textEditor != null)
            {
                return;
            }

            var point = e.GetPosition(Root);
            if (_tool == LiveInkKind.Text)
            {
                ShowTextEditor(point);
                return;
            }

            _drawing = true;
            _start = point;
            _points = new List<Point> { point };
            Ink.Preview = new LiveInkItem
            {
                Kind = _tool,
                Points = new List<Point>(_points),
                CreatedAt = DateTime.Now
            };
            Ink.InvalidateVisual();
            Mouse.Capture(Root);
            e.Handled = true;
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_drawing || Ink.Preview == null)
            {
                return;
            }

            var point = e.GetPosition(Root);
            if (_tool == LiveInkKind.Pen)
            {
                _points.Add(point);
                Ink.Preview = new LiveInkItem
                {
                    Kind = _tool,
                    Points = new List<Point>(_points),
                    CreatedAt = Ink.Preview.CreatedAt
                };
            }
            else
            {
                Ink.Preview = new LiveInkItem
                {
                    Kind = _tool,
                    Points = new List<Point> { _start, point },
                    CreatedAt = Ink.Preview.CreatedAt
                };
            }
            Ink.InvalidateVisual();
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_drawing)
            {
                return;
            }

            _drawing = false;
            Mouse.Capture(null);
            if (Ink.Preview != null)
            {
                var points = Ink.Preview.Points;
                bool valid = _tool == LiveInkKind.Pen
                    ? points.Count > 1
                    : Distance(points) > 4;
                if (valid)
                {
                    Ink.Items.Add(new LiveInkItem
                    {
                        Kind = _tool,
                        Points = points,
                        CreatedAt = DateTime.Now
                    });
                }
            }
            Ink.Preview = null;
            Ink.InvalidateVisual();
        }

        private static double Distance(IReadOnlyList<Point> points)
        {
            if (points.Count < 2)
            {
                return 0;
            }
            double dx = points[^1].X - points[0].X;
            double dy = points[^1].Y - points[0].Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void ShowTextEditor(Point point)
        {
            _textEditor?.Clear();
            _textEditor = new TextBox
            {
                Width = 340,
                MinHeight = 30,
                FontSize = 18,
                Padding = new Thickness(5, 3, 5, 3),
                Background = ThemeService.GetBrush("InputBackgroundBrush"),
                Foreground = ThemeService.GetBrush("TextPrimaryBrush"),
                BorderBrush = ThemeService.GetBrush("AccentBrush"),
                BorderThickness = new Thickness(1),
                Text = ""
            };
            Canvas.SetLeft(_textEditor, point.X);
            Canvas.SetTop(_textEditor, point.Y);
            TextLayer.Children.Add(_textEditor);
            _textEditor.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitText();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    CancelText();
                    e.Handled = true;
                }
            };
            _textEditor.LostKeyboardFocus += (_, _) => CommitText();
            _textEditor.Focus();
        }

        private void CommitText()
        {
            if (_textEditor == null)
            {
                return;
            }

            var editor = _textEditor;
            _textEditor = null;
            string text = editor.Text.Trim();
            if (text.Length > 0 && editor.Parent is Canvas)
            {
                Ink.Items.Add(new LiveInkItem
                {
                    Kind = LiveInkKind.Text,
                    Points = new List<Point>
                    {
                        new Point(Canvas.GetLeft(editor), Canvas.GetTop(editor))
                    },
                    Text = text,
                    CreatedAt = DateTime.Now
                });
                Ink.InvalidateVisual();
            }
            TextLayer.Children.Remove(editor);
        }

        private void CancelText()
        {
            if (_textEditor == null)
            {
                return;
            }
            var editor = _textEditor;
            _textEditor = null;
            TextLayer.Children.Remove(editor);
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e) => Ink.Undo();

        private void BtnClear_Click(object sender, RoutedEventArgs e) => Ink.Clear();

        private async void BtnCapture_Click(object sender, RoutedEventArgs e)
        {
            if (_capturing)
            {
                return;
            }

            _capturing = true;
            try
            {
                CommitText();
                Hide();
                await System.Threading.Tasks.Task.Delay(140);
                var bounds = ScreenCaptureService.GetVirtualBounds();
                var desktop = ScreenCaptureService.CaptureRegion(bounds);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(desktop, new Rect(0, 0, desktop.PixelWidth, desktop.PixelHeight));
                    double scale = desktop.PixelWidth / Math.Max(1, Ink.ActualWidth);
                    Ink.RenderInk(dc, scale);
                }

                var output = new RenderTargetBitmap(
                    desktop.PixelWidth,
                    desktop.PixelHeight,
                    96,
                    96,
                    PixelFormats.Pbgra32);
                output.Render(visual);
                output.Freeze();

                string folder = SettingsService.Instance.EffectiveSaveFolder;
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, ScreenCaptureService.CreateTimestampedFileName(".png"));
                ImageIO.SaveBitmap(output, path);
                CaptureLibraryService.Instance.Record(
                    path,
                    "Live Draw",
                    "",
                    output.PixelWidth,
                    output.PixelHeight);
                RecentCaptureService.Instance.Add(
                    output,
                    path,
                    LocalizationService.Get("LiveDraw_Title"));
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("LiveDraw_Saved", path));
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Error_Body", ex.Message));
            }
            finally
            {
                Show();
                Activate();
                _capturing = false;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Ink.Undo();
                e.Handled = true;
            }
        }
    }
}
