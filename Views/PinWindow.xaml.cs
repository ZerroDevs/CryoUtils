using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class PinWindow : Window
    {
        private readonly BitmapSource _image;
        private readonly double _baseWidth;
        private readonly double _baseHeight;
        private double _zoom = 1;
        private double _opacity = 1;
        private bool _clickThrough;

        public bool IsClickThrough => _clickThrough;

        public PinWindow(BitmapSource image)
        {
            InitializeComponent();
            _image = image;
            PinnedImage.Source = image;

            var work = SystemParameters.WorkArea;
            double maxWidth = Math.Max(200, work.Width - 80);
            double maxHeight = Math.Max(150, work.Height - 80);
            double scale = Math.Min(1, Math.Min(maxWidth / image.PixelWidth, maxHeight / image.PixelHeight));
            _baseWidth = Math.Max(20, image.PixelWidth * scale);
            _baseHeight = Math.Max(20, image.PixelHeight * scale);

            Left = work.Left + Math.Max(20, (work.Width - _baseWidth) / 2);
            Top = work.Top + Math.Max(20, (work.Height - _baseHeight) / 2);
            ApplyZoom();

            App.CurrentApp?.RegisterPinWindow(this);
            Closed += (_, _) => App.CurrentApp?.UnregisterPinWindow(this);
            Loaded += (_, _) => Activate();
        }

        public void SetClickThrough(bool enabled)
        {
            if (_clickThrough == enabled)
            {
                return;
            }

            _clickThrough = enabled;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
                if (enabled)
                {
                    style |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
                }
                else
                {
                    style &= ~NativeMethods.WS_EX_TRANSPARENT;
                }

                NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
                NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
            }

            if (MenuGhost != null)
            {
                MenuGhost.IsChecked = enabled;
            }

            // Visually soften opacity when ghosted so click-through pins stay distinguishable.
            Opacity = enabled ? Math.Min(_opacity, 0.72) : _opacity;
            ZoomBadge.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            App.CurrentApp?.RefreshTrayMenu();

            if (enabled)
            {
                App.CurrentApp?.ShowToast(LocalizationService.Get("Pin_GhostEnabled"));
            }
        }

        private void ApplyZoom()
        {
            Width = _baseWidth * _zoom + 2;
            Height = _baseHeight * _zoom + 2;
            PinnedImage.Width = _baseWidth * _zoom;
            PinnedImage.Height = _baseHeight * _zoom;
            TxtZoom.Text = LocalizationService.Format("Pin_Zoom", Math.Round(_zoom * 100));
            Opacity = _clickThrough ? Math.Min(_opacity, 0.72) : _opacity;
        }

        private void Frame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_clickThrough)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                Close();
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_clickThrough)
            {
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _opacity = Math.Clamp(_opacity + (e.Delta > 0 ? 0.05 : -0.05), 0.2, 1);
            }
            else
            {
                _zoom = Math.Clamp(_zoom + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 4);
            }

            ApplyZoom();
            e.Handled = true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.G)
            {
                SetClickThrough(!_clickThrough);
            }
            else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                CopyImage();
            }
            else if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                SaveImage();
            }
        }

        private void CopyImage()
        {
            try
            {
                Clipboard.SetImage(_image);
                App.CurrentApp?.ShowToast(LocalizationService.Get("Common_Copied"));
            }
            catch
            {
            }
        }

        private void SaveImage()
        {
            try
            {
                string path = ScreenCaptureService.SaveWithDialog(_image, this, ".png");
                if (!string.IsNullOrEmpty(path))
                {
                    App.CurrentApp?.ShowToast(LocalizationService.Format("Capture_Saved", path));
                }
            }
            catch
            {
                App.CurrentApp?.ShowToast(LocalizationService.Get("Capture_SaveFailed"));
            }
        }

        private void MenuCopy_Click(object sender, RoutedEventArgs e) => CopyImage();
        private void MenuSave_Click(object sender, RoutedEventArgs e) => SaveImage();
        private void MenuGhost_Click(object sender, RoutedEventArgs e) => SetClickThrough(!_clickThrough);
        private void MenuClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
