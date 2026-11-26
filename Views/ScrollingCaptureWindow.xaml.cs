using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class ScrollingCaptureWindow : Window
    {
        private readonly NativeMethods.RECT _region;
        private readonly string _sourceApp;
        private readonly string _sourceWindow;
        private readonly List<BitmapSource> _frames = new();
        private readonly DispatcherTimer _timer;
        private ulong _lastHash;
        private bool _finishing;

        public ScrollingCaptureWindow(NativeMethods.RECT region, string sourceApp, string sourceWindow)
        {
            InitializeComponent();
            _region = region;
            _sourceApp = sourceApp;
            _sourceWindow = sourceWindow;
            FlowDirection = LocalizationService.Instance.FlowDirection;
            PositionOutsideSelection();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _timer.Tick += (_, _) => CaptureFrame();
            Loaded += (_, _) =>
            {
                CaptureFrame();
                _timer.Start();
            };
            Closed += (_, _) => _timer.Stop();
        }

        private void PositionOutsideSelection()
        {
            var work = SystemParameters.WorkArea;
            var selection = new Rect(_region.Left, _region.Top, _region.Width, _region.Height);
            var position = ScrollingCaptureService.PlaceControlBar(
                selection,
                new Size(Width, Height),
                work);
            Left = position.X;
            Top = position.Y;
        }

        private void CaptureFrame()
        {
            if (_finishing)
            {
                return;
            }

            try
            {
                var visibility = Visibility;
                Visibility = Visibility.Hidden;
                BitmapSource frame;
                try
                {
                    frame = ScreenCaptureService.CaptureRegion(_region);
                }
                finally
                {
                    Visibility = visibility;
                }
                ulong hash = Hash(frame);
                if (_frames.Count > 0 && hash == _lastHash)
                {
                    return;
                }
                _frames.Add(frame);
                _lastHash = hash;
                TxtCount.Text = LocalizationService.Format("ScrollCapture_Frames", _frames.Count);
            }
            catch
            {
            }
        }

        private static ulong Hash(BitmapSource frame)
        {
            try
            {
                var bgra = frame.Format == PixelFormats.Bgra32
                    ? frame
                    : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                int stride = bgra.PixelWidth * 4;
                byte[] pixels = new byte[stride * bgra.PixelHeight];
                bgra.CopyPixels(pixels, stride, 0);
                ulong hash = 1469598103934665603UL;
                int step = Math.Max(1, pixels.Length / 20000);
                for (int i = 0; i < pixels.Length; i += step)
                {
                    hash ^= pixels[i];
                    hash *= 1099511628211UL;
                }
                return hash;
            }
            catch
            {
                return 0;
            }
        }

        private void BtnCapture_Click(object sender, RoutedEventArgs e) => CaptureFrame();

        private void BtnFinish_Click(object sender, RoutedEventArgs e)
        {
            if (_finishing)
            {
                return;
            }

            _finishing = true;
            _timer.Stop();
            var app = Application.Current as App;
            try
            {
                if (_frames.Count == 0)
                {
                    app?.ShowToast(LocalizationService.Get("ScrollCapture_Empty"));
                    Close();
                    return;
                }

                var stitched = ScrollingCaptureService.StitchVertical(_frames);
                string? path = ScreenCaptureService.QuickSave(stitched, ".png");
                if (string.IsNullOrEmpty(path))
                {
                    app?.ShowToast(LocalizationService.Get("Capture_SaveFailed"));
                    Close();
                    return;
                }

                CaptureLibraryService.Instance.Record(
                    path,
                    _sourceApp,
                    _sourceWindow,
                    stitched.PixelWidth,
                    stitched.PixelHeight);
                RecentCaptureService.Instance.Add(
                    stitched,
                    path,
                    LocalizationService.Get("ScrollCapture_Title"));
                if (SettingsService.Instance.Current.CaptureSound)
                {
                    NativeMethods.MessageBeep(0x40);
                }
                app?.ShowToast(LocalizationService.Format("Capture_Saved", path));
            }
            catch (Exception ex)
            {
                DiagnosticsService.Log("Scrolling capture failed", ex);
                app?.ShowToast(LocalizationService.Format("Error_Body", ex.Message));
            }
            finally
            {
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            Close();
        }
    }
}
