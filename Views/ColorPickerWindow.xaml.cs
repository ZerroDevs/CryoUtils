using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private System.Windows.Media.Color _currentColor = Colors.Black;
        private System.Windows.Media.Color? _lockedColor;
        private string _hex = "#000000";
        private IntPtr _handle;
        private int _scale = 100;

        public ColorPickerWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            SelectFormat(SettingsService.Instance.Current.ColorFormat);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => UpdateSample();
            Loaded += (_, _) =>
            {
                _handle = new WindowInteropHelper(this).Handle;
                _handle = new WindowInteropHelper(this).EnsureHandle();
                _timer.Start();
                UpdateSample();
                Activate();
            };
            Closed += (_, _) => _timer.Stop();
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

        private void UpdateSample()
        {
            if (!IsVisible)
            {
                return;
            }

            NativeMethods.GetCursorPos(out var cursor);

            var point = new NativeMethods.POINT { X = cursor.X, Y = cursor.Y };
            IntPtr monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = ""
            };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            uint dpiX = 96;
            NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out _);
            _scale = (int)Math.Round((double)dpiX);

            int physicalWidth = (int)Math.Round(Width * _scale / 96.0);
            int physicalHeight = (int)Math.Round(Height * _scale / 96.0);
            int left = cursor.X + 24;
            int top = cursor.Y + 24;
            if (left + physicalWidth > info.rcWork.Right - 8)
            {
                left = cursor.X - physicalWidth - 24;
            }
            if (top + physicalHeight > info.rcWork.Bottom - 8)
            {
                top = cursor.Y - physicalHeight - 24;
            }
            left = Math.Max(info.rcWork.Left + 8, left);
            top = Math.Max(info.rcWork.Top + 8, top);
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(
                    _handle,
                    NativeMethods.HWND_TOPMOST,
                    left,
                    top,
                    physicalWidth,
                    physicalHeight,
                    NativeMethods.SWP_NOACTIVATE);
            }

            var sampleRect = new NativeMethods.RECT
            {
                Left = cursor.X - 10,
                Top = cursor.Y - 10,
                Right = cursor.X + 11,
                Bottom = cursor.Y + 11
            };
            try
            {
                MagnifierImage.Source = ScreenCaptureService.CaptureRegion(sampleRect);
            }
            catch
            {
            }

            IntPtr dc = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                uint pixel = NativeMethods.GetPixel(dc, cursor.X, cursor.Y);
                byte r = (byte)(pixel & 0xFF);
                byte g = (byte)((pixel >> 8) & 0xFF);
                byte b = (byte)((pixel >> 16) & 0xFF);
                _currentColor = System.Windows.Media.Color.FromRgb(r, g, b);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, dc);
            }

            UpdateText();
        }

        private void UpdateText()
        {
            string hex = $"#{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}";
            _hex = hex;
            TxtHex.Text = hex;
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

        private static (double H, double S, double L) ToHsl(System.Windows.Media.Color color)
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

        private void CopyAndClose()
        {
            string format = (CmbFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "HEX";
            var (h, s, l) = ToHsl(_currentColor);
            string text = format switch
            {
                "RGB" => $"rgb({_currentColor.R}, {_currentColor.G}, {_currentColor.B})",
                "HSL" => $"hsl({Math.Round(h)}, {Math.Round(s)}%, {Math.Round(l)}%)",
                _ => _hex
            };

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
                    (Application.Current as App)?.ShowToast(
                        LocalizationService.Format("Color_Copied", summary));
                }
                else
                {
                    Clipboard.SetText(text);
                    SettingsService.Instance.RegisterRecentColor(_hex);
                    (Application.Current as App)?.ShowToast(
                        LocalizationService.Format("Color_Copied", text));
                }
            }
            catch
            {
            }
            Close();
        }

        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsControl(e.OriginalSource as DependencyObject))
            {
                return;
            }

            CopyAndClose();
            e.Handled = true;
        }

        private static bool IsControl(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is ComboBox or ComboBoxItem or ButtonBase)
                {
                    return true;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.Space)
            {
                ToggleContrastLock();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter || e.Key == Key.C)
            {
                if (!(e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.None))
                {
                    CopyAndClose();
                }
            }
        }

        private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CmbFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string format = (CmbFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "HEX";
            SettingsService.Instance.Current.ColorFormat = format;
            SettingsService.Instance.Save();
        }
    }
}
