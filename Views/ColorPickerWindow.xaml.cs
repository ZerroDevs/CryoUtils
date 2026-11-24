using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class ColorPickerWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly List<Window> _backdrops = new();
        private NativeMethods.RECT _virtualBounds;
        private BitmapSource? _desktopBitmap;
        private byte[]? _desktopPixels;
        private int _desktopStride;
        private Color _currentColor = Colors.Black;
        private Color? _lockedColor;
        private string _hex = "#000000";
        private IntPtr _handle;
        private bool _isSampling;
        private bool _closing;
        private bool _positionedInitially;

        public ColorPickerWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            SelectFormat(SettingsService.Instance.Current.ColorFormat);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(24) };
            _timer.Tick += (_, _) => PollCursorUnderSampling();

            Loaded += async (_, _) =>
            {
                _handle = new WindowInteropHelper(this).EnsureHandle();
                PositionNearWorkAreaEdge();
                Opacity = 0;
                await Task.Delay(110);
                await StartSamplingAsync(waitForHide: false);
                Opacity = 1;
                Activate();
            };

            Closing += (_, _) =>
            {
                _closing = true;
                StopSampling();
            };
        }

        private void SelectFormat(string? format)
        {
            foreach (var item in CmbFormat.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(comboItem.Tag as string, format, StringComparison.OrdinalIgnoreCase))
                {
                    CmbFormat.SelectedItem = comboItem;
                    return;
                }
            }
            CmbFormat.SelectedIndex = 0;
        }

        private void PositionNearWorkAreaEdge()
        {
            if (_positionedInitially || _handle == IntPtr.Zero)
            {
                return;
            }
            _positionedInitially = true;

            NativeMethods.GetCursorPos(out var cursor);
            var point = new NativeMethods.POINT { X = cursor.X, Y = cursor.Y };
            IntPtr monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = ""
            };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
            double scale = Math.Max(1.0, (dpiX > 0 ? dpiX : 96) / 96.0);

            int physW = (int)Math.Round(ActualWidth * scale);
            int physH = (int)Math.Round(ActualHeight * scale);
            int margin = (int)Math.Round(24 * scale);

            int workRight = info.rcWork.Right;
            int workLeft = info.rcWork.Left;
            int workTop = info.rcWork.Top;
            int workBottom = info.rcWork.Bottom;

            // Anchor away from the cursor half of the monitor so the user immediately sees their target pixels.
            int midX = (workLeft + workRight) / 2;
            int left = cursor.X < midX ? workRight - physW - margin : workLeft + margin;
            int top = workTop + margin;

            left = Math.Clamp(left, workLeft + 8, Math.Max(workLeft + 8, workRight - physW - 8));
            top = Math.Clamp(top, workTop + 8, Math.Max(workTop + 8, workBottom - physH - 8));

            NativeMethods.SetWindowPos(
                _handle,
                NativeMethods.HWND_TOPMOST,
                left,
                top,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        private async Task StartSamplingAsync(bool waitForHide)
        {
            if (_closing)
            {
                return;
            }

            StopSampling();

            if (waitForHide)
            {
                Visibility = Visibility.Hidden;
                await Task.Delay(90);
            }

            if (_closing)
            {
                return;
            }

            try
            {
                _virtualBounds = ScreenCaptureService.GetVirtualBounds();
                var captured = ScreenCaptureService.CaptureRegion(_virtualBounds);
                _desktopBitmap = captured;

                int width = captured.PixelWidth;
                int height = captured.PixelHeight;
                _desktopStride = width * 4;
                _desktopPixels = new byte[_desktopStride * height];
                captured.CopyPixels(_desktopPixels, _desktopStride, 0);

                var monitors = ScreenCaptureService.GetMonitors();
                foreach (var monitor in monitors)
                {
                    int relX = monitor.Bounds.Left - _virtualBounds.Left;
                    int relY = monitor.Bounds.Top - _virtualBounds.Top;
                    var cropped = ScreenCaptureService.Crop(
                        captured,
                        new Int32Rect(relX, relY, monitor.Bounds.Width, monitor.Bounds.Height));

                    double monScale = Math.Max(0.1, monitor.Scale);
                    var backdrop = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = false,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false,
                        Topmost = true,
                        ShowActivated = false,
                        Cursor = Cursors.Cross,
                        Width = monitor.Bounds.Width / monScale,
                        Height = monitor.Bounds.Height / monScale,
                        Left = 0,
                        Top = 0,
                        Content = new Image
                        {
                            Source = cropped,
                            Stretch = Stretch.Fill
                        }
                    };

                    var targetMonitor = monitor;
                    backdrop.SourceInitialized += (_, _) =>
                    {
                        var hwnd = new WindowInteropHelper(backdrop).Handle;
                        NativeMethods.SetWindowPos(
                            hwnd,
                            NativeMethods.HWND_TOPMOST,
                            targetMonitor.Bounds.Left,
                            targetMonitor.Bounds.Top,
                            targetMonitor.Bounds.Width,
                            targetMonitor.Bounds.Height,
                            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);
                    };

                    backdrop.MouseMove += Backdrop_MouseMove;
                    backdrop.MouseLeftButtonDown += Backdrop_MouseLeftButtonDown;
                    backdrop.MouseRightButtonDown += Backdrop_MouseRightButtonDown;
                    backdrop.PreviewKeyDown += Window_PreviewKeyDown;

                    _backdrops.Add(backdrop);
                    backdrop.Show();
                }

                _isSampling = true;
                Visibility = Visibility.Visible;
                BringInspectorToFront();
                UpdateSamplingStateUi();
                _timer.Start();
                PollCursorUnderSampling();
            }
            catch
            {
                Visibility = Visibility.Visible;
                StopSampling();
            }
        }

        private void BringInspectorToFront()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.SetWindowPos(
                _handle,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
            Activate();
        }

        private void StopSampling()
        {
            _timer.Stop();
            _isSampling = false;
            foreach (var backdrop in _backdrops)
            {
                try
                {
                    backdrop.Close();
                }
                catch
                {
                }
            }
            _backdrops.Clear();
            if (!_closing)
            {
                UpdateSamplingStateUi();
            }
        }

        private void UpdateSamplingStateUi()
        {
            if (TxtStatusBadge != null)
            {
                TxtStatusBadge.Text = _isSampling
                    ? LocalizationService.Get("Color_PickingStatus")
                    : LocalizationService.Get("Color_PickedStatus");
                TxtStatusBadge.Foreground = ThemeService.GetBrush(_isSampling ? "AccentTextBrush" : "SuccessBrush");
            }
            if (BtnPick != null)
            {
                BtnPick.IsEnabled = !_isSampling;
            }
        }

        private void PollCursorUnderSampling()
        {
            if (!_isSampling || !IsVisible || CmbFormat.IsDropDownOpen)
            {
                return;
            }

            NativeMethods.GetCursorPos(out var cursor);
            if (IsCursorOverInspector(cursor))
            {
                // Pause overwriting the sampled pixel when the user moves onto the inspector itself.
                return;
            }

            SamplePhysicalPoint(cursor.X, cursor.Y);
        }

        private bool IsCursorOverInspector(NativeMethods.POINT cursor)
        {
            if (_handle == IntPtr.Zero)
            {
                return false;
            }

            if (NativeMethods.GetWindowRect(_handle, out var rect))
            {
                return cursor.X >= rect.Left && cursor.X < rect.Right &&
                       cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
            }

            return false;
        }

        private void Backdrop_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSampling || CmbFormat.IsDropDownOpen)
            {
                return;
            }

            NativeMethods.GetCursorPos(out var cursor);
            if (!IsCursorOverInspector(cursor))
            {
                SamplePhysicalPoint(cursor.X, cursor.Y);
            }
        }

        private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isSampling)
            {
                return;
            }

            NativeMethods.GetCursorPos(out var cursor);
            SamplePhysicalPoint(cursor.X, cursor.Y);
            CopyActiveFormatToClipboard();
            StopSampling();
            Activate();
            e.Handled = true;
        }

        private void Backdrop_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            StopSampling();
            e.Handled = true;
        }

        private void SamplePhysicalPoint(int screenX, int screenY)
        {
            if (_desktopBitmap == null || _desktopPixels == null)
            {
                return;
            }

            int px = Math.Clamp(screenX - _virtualBounds.Left, 0, Math.Max(0, _desktopBitmap.PixelWidth - 1));
            int py = Math.Clamp(screenY - _virtualBounds.Top, 0, Math.Max(0, _desktopBitmap.PixelHeight - 1));

            int offset = py * _desktopStride + px * 4;
            if (offset + 2 < _desktopPixels.Length)
            {
                byte b = _desktopPixels[offset];
                byte g = _desktopPixels[offset + 1];
                byte r = _desktopPixels[offset + 2];
                _currentColor = Color.FromRgb(r, g, b);
            }

            const int radius = 7;
            int cropW = Math.Min(radius * 2 + 1, _desktopBitmap.PixelWidth);
            int cropH = Math.Min(radius * 2 + 1, _desktopBitmap.PixelHeight);
            int cropX = Math.Clamp(px - radius, 0, Math.Max(0, _desktopBitmap.PixelWidth - cropW));
            int cropY = Math.Clamp(py - radius, 0, Math.Max(0, _desktopBitmap.PixelHeight - cropH));
            MagnifierImage.Source = ScreenCaptureService.Crop(_desktopBitmap, new Int32Rect(cropX, cropY, cropW, cropH));
            TxtCoords.Text = $"{screenX}, {screenY}";

            UpdateText();
        }

        private void UpdateText()
        {
            string hex = $"#{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}";
            _hex = hex;
            TxtHex.Text = hex;
            TxtHexRow.Text = hex;
            TxtRgb.Text = $"rgb({_currentColor.R}, {_currentColor.G}, {_currentColor.B})";
            var (h, s, l) = ToHsl(_currentColor);
            TxtHsl.Text = $"hsl({Math.Round(h)}, {Math.Round(s)}%, {Math.Round(l)}%)";
            ColorPreview.Background = new SolidColorBrush(_currentColor);
            UpdateContrastDisplay();
        }

        private void ToggleContrastLock()
        {
            if (_lockedColor == null)
            {
                _lockedColor = _currentColor;
                ContrastPanel.Visibility = Visibility.Visible;
                BaseColorSwatch.Background = new SolidColorBrush(_lockedColor.Value);
            }
            else
            {
                _lockedColor = null;
                ContrastPanel.Visibility = Visibility.Collapsed;
            }
            UpdateContrastDisplay();
        }

        private void UpdateContrastDisplay()
        {
            if (_lockedColor == null || ContrastPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            CurrentColorSwatch.Background = new SolidColorBrush(_currentColor);
            double ratio = ColorContrastService.CalculateRatio(_lockedColor.Value, _currentColor);
            TxtContrastRatio.Text = ColorContrastService.FormatRatio(ratio);
            var eval = ColorContrastService.Evaluate(ratio);

            BadgeAa.Background = eval.AaNormal
                ? ThemeService.GetBrush("SuccessMutedBrush")
                : ThemeService.GetBrush("InsetBackgroundBrush");
            TxtBadgeAa.Foreground = eval.AaNormal
                ? ThemeService.GetBrush("SuccessBrush")
                : ThemeService.GetBrush("TextMutedBrush");

            BadgeAaa.Background = eval.AaaNormal
                ? ThemeService.GetBrush("SuccessMutedBrush")
                : ThemeService.GetBrush("InsetBackgroundBrush");
            TxtBadgeAaa.Foreground = eval.AaaNormal
                ? ThemeService.GetBrush("SuccessBrush")
                : ThemeService.GetBrush("TextMutedBrush");
        }

        private static (double H, double S, double L) ToHsl(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2;
            double h = 0;
            double s = 0;
            if (Math.Abs(max - min) > 0.0001)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (Math.Abs(max - r) < 0.0001)
                {
                    h = (g - b) / d + (g < b ? 6 : 0);
                }
                else if (Math.Abs(max - g) < 0.0001)
                {
                    h = (b - r) / d + 2;
                }
                else
                {
                    h = (r - g) / d + 4;
                }
                h *= 60;
            }
            return (h, s * 100, l * 100);
        }

        private string FormatColorString(string format)
        {
            var (h, s, l) = ToHsl(_currentColor);
            return format switch
            {
                "RGB" => $"rgb({_currentColor.R}, {_currentColor.G}, {_currentColor.B})",
                "HSL" => $"hsl({Math.Round(h)}, {Math.Round(s)}%, {Math.Round(l)}%)",
                _ => _hex
            };
        }

        private void CopyActiveFormatToClipboard()
        {
            string format = (CmbFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "HEX";
            string text = FormatColorString(format);

            try
            {
                if (_lockedColor != null)
                {
                    string baseHex = $"#{_lockedColor.Value.R:X2}{_lockedColor.Value.G:X2}{_lockedColor.Value.B:X2}";
                    double ratio = ColorContrastService.CalculateRatio(_lockedColor.Value, _currentColor);
                    string summary = $"{baseHex} vs {_hex} ({ColorContrastService.FormatRatio(ratio)})";
                    Clipboard.SetText(summary);
                    SettingsService.Instance.RegisterRecentColor(baseHex);
                    SettingsService.Instance.RegisterRecentColor(_hex);
                    (Application.Current as App)?.ShowToast(LocalizationService.Format("Color_Copied", summary));
                }
                else
                {
                    Clipboard.SetText(text);
                    SettingsService.Instance.RegisterRecentColor(_hex);
                    (Application.Current as App)?.ShowToast(LocalizationService.Format("Color_Copied", text));
                }
            }
            catch
            {
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private async void BtnPick_Click(object sender, RoutedEventArgs e)
        {
            await StartSamplingAsync(waitForHide: true);
        }

        private void BtnCopySelected_Click(object sender, RoutedEventArgs e)
        {
            CopyActiveFormatToClipboard();
        }

        private void BtnCopyRow_Click(object sender, RoutedEventArgs e)
        {
            string format = (sender as FrameworkElement)?.Tag as string ?? "HEX";
            string text = FormatColorString(format);
            try
            {
                Clipboard.SetText(text);
                SettingsService.Instance.RegisterRecentColor(_hex);
                (Application.Current as App)?.ShowToast(LocalizationService.Format("Color_Copied", text));
            }
            catch
            {
            }
        }

        private void BtnLockContrast_Click(object sender, RoutedEventArgs e)
        {
            ToggleContrastLock();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_isSampling)
                {
                    StopSampling();
                }
                else
                {
                    Close();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                ToggleContrastLock();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                CopyActiveFormatToClipboard();
                e.Handled = true;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CmbFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            string format = (CmbFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "HEX";
            SettingsService.Instance.Current.ColorFormat = format;
            SettingsService.Instance.Save();
        }
    }
}
