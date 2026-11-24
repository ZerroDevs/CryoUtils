using System;
using System.Windows;
using System.Windows.Input;
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
            Loaded += (_, _) => Activate();
        }

        private void ApplyZoom()
        {
            Width = _baseWidth * _zoom + 2;
            Height = _baseHeight * _zoom + 2;
            PinnedImage.Width = _baseWidth * _zoom;
            PinnedImage.Height = _baseHeight * _zoom;
            TxtZoom.Text = LocalizationService.Format("Pin_Zoom", Math.Round(_zoom * 100));
            Opacity = _opacity;
        }

        private void Frame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
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
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
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
                    (Application.Current as App)?.ShowToast(
                        LocalizationService.Format("Capture_Saved", path));
                }
            }
            catch
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_SaveFailed"));
            }
        }

        private void MenuCopy_Click(object sender, RoutedEventArgs e)
        {
            CopyImage();
        }

        private void MenuSave_Click(object sender, RoutedEventArgs e)
        {
            SaveImage();
        }

        private void MenuClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
