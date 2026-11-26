using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class ScreenRulerWindow : Window
    {
        private RulerMode _mode = RulerMode.Horizontal;
        private bool _dragging;
        private Point _start;
        private Point _end;
        private string _readout = "";

        public ScreenRulerWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            Surface.Mode = _mode;
            Loaded += (_, _) =>
            {
                Surface.DpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                Activate();
                Focus();
                Keyboard.Focus(Root);
                UpdateModeButtons();
            };
            Closed += (_, _) => Mouse.Capture(null);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var bounds = ScreenCaptureService.GetVirtualBounds();
            NativeMethods.SetWindowPos(
                new WindowInteropHelper(this).Handle,
                NativeMethods.HWND_TOPMOST,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeMethods.SWP_SHOWWINDOW);
        }

        private void Mode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag &&
                Enum.TryParse<RulerMode>(tag, out var mode))
            {
                _mode = mode;
                Surface.Mode = mode;
                Surface.ResetSelection();
                _dragging = false;
                UpdateModeButtons();
                UpdateInfo(null);
            }
        }

        private void UpdateModeButtons()
        {
            var active = ThemeService.GetBrush("AccentTextBrush");
            var normal = ThemeService.GetBrush("TextSecondaryBrush");
            foreach (var button in new[] { BtnHorizontal, BtnVertical, BtnDiagonal, BtnRegion, BtnGuides })
            {
                bool selected = string.Equals(button.Tag as string, _mode.ToString(), StringComparison.Ordinal);
                button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                button.Foreground = selected ? active : normal;
            }
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RulerToolbar.IsMouseOver)
            {
                return;
            }

            var point = e.GetPosition(Root);
            if (_mode == RulerMode.Guides)
            {
                Surface.AddGuide(point);
                UpdateInfo(point);
                return;
            }

            _dragging = true;
            _start = point;
            _end = point;
            Surface.Start = point;
            Surface.Current = point;
            Surface.InvalidateVisual();
            Mouse.Capture(Root);
            UpdateInfo(point);
            e.Handled = true;
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            var point = e.GetPosition(Root);
            _end = _mode switch
            {
                RulerMode.Horizontal => new Point(point.X, _start.Y),
                RulerMode.Vertical => new Point(_start.X, point.Y),
                _ => point
            };
            Surface.Current = _end;
            Surface.InvalidateVisual();
            UpdateInfo(_end);
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            Mouse.Capture(null);
            UpdateInfo(_end);
        }

        private void UpdateInfo(Point? point)
        {
            if (_mode == RulerMode.Guides)
            {
                TxtInfo.Text = LocalizationService.Get("Ruler_GuidesHint");
                return;
            }

            if (!_dragging && (!Surface.Start.HasValue || !Surface.Current.HasValue))
            {
                _readout = "";
                TxtInfo.Text = LocalizationService.Get("Ruler_Hint");
                return;
            }

            var start = Surface.Start ?? _start;
            var end = Surface.Current ?? _end;
            var physicalStart = PointToScreen(start);
            var physicalEnd = PointToScreen(end);
            double width = Math.Abs(physicalEnd.X - physicalStart.X);
            double height = Math.Abs(physicalEnd.Y - physicalStart.Y);
            double length = Math.Sqrt(width * width + height * height);
            double angle = Math.Atan2(physicalEnd.Y - physicalStart.Y, physicalEnd.X - physicalStart.X) * 180.0 / Math.PI;

            _readout = _mode switch
            {
                RulerMode.Region =>
                    $"X {Math.Round(physicalStart.X)}  Y {Math.Round(physicalStart.Y)}  ·  {Math.Round(width)} × {Math.Round(height)} px",
                RulerMode.Horizontal =>
                    $"X {Math.Round(physicalStart.X)}  ·  length {Math.Round(width)} px  ·  angle {Math.Round(angle)}°",
                RulerMode.Vertical =>
                    $"Y {Math.Round(physicalStart.Y)}  ·  length {Math.Round(height)} px  ·  angle {Math.Round(angle)}°",
                _ =>
                    $"X {Math.Round(physicalStart.X)}  Y {Math.Round(physicalStart.Y)}  ·  {Math.Round(length)} px  ·  {Math.Round(angle)}°"
            };
            TxtInfo.Text = _readout;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e) => CopyReadout();

        private void CopyReadout()
        {
            if (string.IsNullOrWhiteSpace(_readout))
            {
                return;
            }

            try
            {
                Clipboard.SetText(_readout);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Ruler_Copied"));
            }
            catch
            {
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            Surface.ResetSelection();
            Surface.ClearGuides();
            _dragging = false;
            _readout = "";
            TxtInfo.Text = LocalizationService.Get("Ruler_Hint");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                CopyReadout();
                e.Handled = true;
            }
        }

    }
}
