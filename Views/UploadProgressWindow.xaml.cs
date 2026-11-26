using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class UploadProgressWindow : Window
    {
        private readonly CancellationTokenSource _cts = new();
        private DispatcherTimer? _autoCloseTimer;
        private int _autoCloseRemaining;
        private string _uploadedUrl = "";
        private bool _isCompleted;
        private readonly Action<string>? _onSuccess;
        private readonly Action<Exception>? _onFailure;

        private UploadProgressWindow(
            string displayName,
            long estimatedBytes,
            Action<string>? onSuccess = null,
            Action<Exception>? onFailure = null)
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            Topmost = false;

            _onSuccess = onSuccess;
            _onFailure = onFailure;

            TxtFileName.Text = displayName;
            TxtFileSize.Text = estimatedBytes > 0 ? ImageIO.FormatBytes(estimatedBytes) : "";
            TxtUrl.Text = LocalizationService.Get("Upload_Placeholder");
            TxtProgress.Text = LocalizationService.Get("Capture_Uploading");
            TxtDestination.Text = UploadProviderService.IsLocalProvider
                ? LocalizationService.Get("Upload_DestinationLocal")
                : LocalizationService.Get("Upload_Destination");

            PositionBottomRight();
        }

        public static UploadProgressWindow StartCaptureUpload(
            BitmapSource bitmap,
            Action<string>? onSuccess = null,
            Action<Exception>? onFailure = null,
            string? fileName = null)
        {
            byte[] encoded = CloudUploadService.EncodeBitmapToPng(bitmap);
            string name = string.IsNullOrWhiteSpace(fileName)
                ? $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                : fileName;
            var win = new UploadProgressWindow(name, encoded.LongLength, onSuccess, onFailure);
            win.Show();
            _ = win.RunUploadAsync((progress, ct) =>
                UploadProviderService.UploadImageAsync(bitmap, progress, ct));
            return win;
        }

        public static UploadProgressWindow StartFileUpload(
            string filePath,
            Action<string>? onSuccess = null,
            Action<Exception>? onFailure = null)
        {
            var info = new FileInfo(filePath);
            long size = info.Exists ? info.Length : 0;
            var win = new UploadProgressWindow(info.Name, size, onSuccess, onFailure);
            win.Show();
            _ = win.RunUploadAsync((progress, ct) =>
                UploadProviderService.UploadFileAsync(filePath, progress, ct));
            return win;
        }

        private void PositionBottomRight()
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 16;
            Top = work.Bottom - 168 - 16;
        }

        private async Task RunUploadAsync(Func<IProgress<UploadProgressInfo>, CancellationToken, Task<string>> uploadFunc)
        {
            var progress = new Progress<UploadProgressInfo>(UpdateProgressUi);
            try
            {
                string url = await uploadFunc(progress, _cts.Token);
                _isCompleted = true;
                _uploadedUrl = url;

                TxtStatusTitle.Text = LocalizationService.Get("Upload_Complete");
                TxtStatusTitle.Foreground = ThemeService.GetBrush("SuccessTextBrush");
                PathHeaderIcon.Data = IconData.Check;
                PathHeaderIcon.Stroke = ThemeService.GetBrush("SuccessTextBrush");
                UploadBar.Value = 100;
                TaskbarProgress.ProgressState = TaskbarItemProgressState.None;
                TaskbarProgress.ProgressValue = 1.0;

                TxtProgress.Text = LocalizationService.Get("Upload_DoneCopied");
                TxtUrl.Text = url;
                BtnCopy.IsEnabled = true;

                CopyTextSafe(url);
                FlashCopyIcon();
                StartAutoCloseCountdown();

                if (SettingsService.Instance.Current.CaptureSound)
                {
                    NativeMethods.MessageBeep(0x40);
                }

                (Application.Current as App)?.ShowToast(LocalizationService.Format("Capture_UploadSuccess", url));
                _onSuccess?.Invoke(url);
            }
            catch (OperationCanceledException)
            {
                _isCompleted = true;
                TaskbarProgress.ProgressState = TaskbarItemProgressState.None;
                TxtStatusTitle.Text = LocalizationService.Get("Upload_Cancelled");
                TxtProgress.Text = LocalizationService.Get("Upload_Cancelled");
            }
            catch (Exception ex)
            {
                _isCompleted = true;
                TaskbarProgress.ProgressState = TaskbarItemProgressState.Error;
                TxtStatusTitle.Text = LocalizationService.Get("Upload_Failed");
                TxtStatusTitle.Foreground = ThemeService.GetBrush("DangerTextBrush");
                UploadBar.Foreground = ThemeService.GetBrush("DangerTextBrush");
                TxtUrl.Text = ex.Message;
                TxtUrl.Foreground = ThemeService.GetBrush("DangerTextBrush");
                TxtProgress.Text = LocalizationService.Format("Action_Failed", ex.Message);
                (Application.Current as App)?.ShowToast(LocalizationService.Format("Capture_UploadFailed", ex.Message));
                _onFailure?.Invoke(ex);
            }
        }

        private void UpdateProgressUi(UploadProgressInfo info)
        {
            if (_isCompleted)
            {
                return;
            }

            int pct = info.Percent;
            UploadBar.Value = pct;
            TaskbarProgress.ProgressState = TaskbarItemProgressState.Normal;
            TaskbarProgress.ProgressValue = info.Fraction;

            if (pct >= 100)
            {
                TxtProgress.Text = LocalizationService.Get("Upload_Finalizing");
            }
            else
            {
                TxtProgress.Text = LocalizationService.Format(
                    "Upload_ProgressFmt",
                    pct,
                    ImageIO.FormatBytes(info.BytesSent),
                    ImageIO.FormatBytes(info.TotalBytes));
            }
        }

        private static void CopyTextSafe(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                try
                {
                    var data = new DataObject();
                    data.SetText(text);
                    Clipboard.SetDataObject(data, true);
                }
                catch
                {
                }
            }
        }

        private void FlashCopyIcon()
        {
            PathCopyIcon.Data = IconData.Check;
            PathCopyIcon.Stroke = ThemeService.GetBrush("SuccessTextBrush");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                PathCopyIcon.Data = IconData.Copy;
                PathCopyIcon.Stroke = ThemeService.GetBrush("IconBrush");
            };
            timer.Start();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_uploadedUrl))
            {
                return;
            }

            CopyTextSafe(_uploadedUrl);
            FlashCopyIcon();
            (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
        }

        private void StartAutoCloseCountdown()
        {
            _autoCloseTimer?.Stop();
            _autoCloseRemaining = 30;
            TxtProgress.Text = LocalizationService.Format("Upload_DoneClosingFmt", _autoCloseRemaining);

            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseRemaining--;
                if (_autoCloseRemaining <= 0)
                {
                    _autoCloseTimer?.Stop();
                    Close();
                    return;
                }

                TxtProgress.Text = LocalizationService.Format("Upload_DoneClosingFmt", _autoCloseRemaining);
            };
            _autoCloseTimer.Start();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _autoCloseTimer?.Stop();
            _autoCloseTimer = null;
            if (!_isCompleted)
            {
                _cts.Cancel();
            }
        }
    }
}
