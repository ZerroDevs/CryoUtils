using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CryoUtils.Services;
using Microsoft.Win32;

namespace CryoUtils.Views
{
    public partial class RecordingBarWindow : Window
    {
        private readonly Int32Rect _region;
        private readonly RecorderService _recorder = new();
        private readonly RecordingFrameWindow _frame = new();
        private readonly DispatcherTimer _timer;
        private RecordingResult? _result;
        private string _format = "GIF";
        private bool _stopping;
        private bool _encoding;
        private bool _finished;

        public RecordingBarWindow(Int32Rect region)
        {
            InitializeComponent();
            _region = region;
            FlowDirection = LocalizationService.Instance.FlowDirection;

            _format = SettingsService.Instance.Current.RecorderFormat;
            SelectTag(CmbFormat, _format);
            SelectTag(CmbFps, SettingsService.Instance.Current.GifFps.ToString());

            _recorder.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateUi));
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _timer.Tick += (_, _) => UpdateUi();

            Loaded += RecordingBarWindow_Loaded;
            Closed += (_, _) =>
            {
                _timer.Stop();
                _frame.Close();
                if (_recorder.IsRecording)
                {
                    _recorder.Cancel();
                }
                _recorder.Dispose();
                (Application.Current as App)?.FinishCapture();
            };
        }

        private void RecordingBarWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PositionWindow();
            _frame.Show();
            _frame.SetBounds(_region);
            _recorder.Start(
                _region,
                SettingsService.Instance.Current.GifFps,
                SettingsService.Instance.Current.MaxRecordingSeconds);
            _timer.Start();
            UpdateUi();
            Activate();
        }

        private void PositionWindow()
        {
            var point = new NativeMethods.POINT
            {
                X = _region.X + _region.Width / 2,
                Y = _region.Y + _region.Height / 2
            };
            IntPtr monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = ""
            };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            uint dpiX = 96;
            NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out _);
            double scale = dpiX / 96.0;
            int physicalWidth = (int)Math.Round(Width * scale);
            int physicalHeight = (int)Math.Round(Height * scale);
            int left = _region.X;
            int top = _region.Y - physicalHeight - 10;
            if (top < info.rcWork.Top + 8)
            {
                top = _region.Y + _region.Height + 10;
            }
            if (top + physicalHeight > info.rcWork.Bottom - 8)
            {
                top = info.rcWork.Bottom - physicalHeight - 8;
            }
            left = Math.Clamp(left, info.rcWork.Left + 8, Math.Max(info.rcWork.Left + 8, info.rcWork.Right - physicalWidth - 8));

            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HWND_TOPMOST,
                left,
                top,
                physicalWidth,
                physicalHeight,
                NativeMethods.SWP_NOACTIVATE);
        }

        private void UpdateUi()
        {
            if (_finished)
            {
                return;
            }

            var elapsed = _recorder.Elapsed;
            TxtTimer.Text = $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            TxtFrameInfo.Text = LocalizationService.Format("Rec_FrameCount", _recorder.FrameCount);
            TxtEstimate.Text = LocalizationService.Format(
                "Rec_Estimate",
                ImageIO.FormatBytes(_recorder.EstimatedBytes));
            BtnPause.Content = _recorder.IsPaused
                ? LocalizationService.Get("Rec_Resume")
                : LocalizationService.Get("Rec_Pause");
            BtnPause.IsEnabled = _recorder.IsRecording && !_encoding;
            BtnStopSave.IsEnabled = !_encoding;
            BtnStopCopy.IsEnabled = !_encoding;

            if (_encoding)
            {
                TxtState.Text = LocalizationService.Get("Rec_Encoding");
            }
            else if (!_recorder.IsRecording && _result != null)
            {
                TxtState.Text = LocalizationService.Get("Rec_Paused");
            }
            else
            {
                TxtState.Text = "";
            }

            if (_recorder.IsRecording && !_stopping &&
                elapsed.TotalSeconds >= SettingsService.Instance.Current.MaxRecordingSeconds)
            {
                _ = AutoStopAsync();
            }
        }

        private async Task AutoStopAsync()
        {
            if (_stopping || _finished)
            {
                return;
            }

            _stopping = true;
            try
            {
                _result = await _recorder.StopAsync();
                TxtState.Text = LocalizationService.Get("Rec_Paused");
            }
            finally
            {
                _stopping = false;
            }
        }

        private async Task<RecordingResult?> GetResultAsync()
        {
            if (_result != null)
            {
                return _result;
            }

            if (_stopping)
            {
                return null;
            }

            _stopping = true;
            try
            {
                _result = await _recorder.StopAsync();
                return _result;
            }
            finally
            {
                _stopping = false;
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (_recorder.IsPaused)
            {
                _recorder.Resume();
            }
            else
            {
                _recorder.Pause();
            }
            UpdateUi();
        }

        private async void BtnStopSave_Click(object sender, RoutedEventArgs e)
        {
            if (_encoding)
            {
                return;
            }

            var result = await GetResultAsync();
            if (result == null || result.PngFrames.Count == 0)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Rec_NoFrames"));
                CloseBar();
                return;
            }

            string extension = _format == "MP4" ? ".mp4" : ".gif";
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Rec_StopSave"),
                Filter = LocalizationService.Get("Common_SaveRecordingFilter"),
                FilterIndex = _format == "MP4" ? 2 : 1,
                DefaultExt = extension,
                AddExtension = true,
                FileName = ScreenCaptureService.CreateTimestampedFileName(extension),
                InitialDirectory = SettingsService.Instance.EffectiveSaveFolder
            };
            Directory.CreateDirectory(dialog.InitialDirectory);
            if (dialog.ShowDialog(this) != true)
            {
                CloseBar();
                return;
            }

            await EncodeAndCloseAsync(result, dialog.FileName, copyToClipboard: false);
        }

        private async void BtnStopCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_encoding)
            {
                return;
            }

            var result = await GetResultAsync();
            if (result == null || result.PngFrames.Count == 0)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Rec_NoFrames"));
                CloseBar();
                return;
            }

            string extension = _format == "MP4" ? ".mp4" : ".gif";
            string folder = SettingsService.Instance.EffectiveSaveFolder;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, ScreenCaptureService.CreateTimestampedFileName(extension));
            await EncodeAndCloseAsync(result, path, copyToClipboard: true);
        }

        private async Task EncodeAndCloseAsync(RecordingResult result, string path, bool copyToClipboard)
        {
            _encoding = true;
            UpdateUi();
            try
            {
                await Task.Run(() =>
                {
                    if (_format == "MP4")
                    {
                        Mp4Writer.Write(result, path);
                    }
                    else
                    {
                        GifWriter.EncodePngFramesToGif(
                            result.PngFrames,
                            result.Width,
                            result.Height,
                            result.Fps,
                            path);
                    }
                });

                var preview = ImageIO.DecodeBytes(result.PngFrames[0]);
                ScreenCaptureService.EnforceSavedCaptureLimit();
                RecentCaptureService.Instance.Add(
                    preview,
                    path,
                    LocalizationService.Get("Rec_Title"));

                if (copyToClipboard)
                {
                    var files = new StringCollection { path };
                    Clipboard.SetFileDropList(files);
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_RecordCopied"));
                }
                else
                {
                    (Application.Current as App)?.ShowToast(
                        LocalizationService.Format("Capture_RecordSaved", path));
                }
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Error_Body", ex.Message));
            }
            finally
            {
                _encoding = false;
                CloseBar();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _recorder.Cancel();
            CloseBar();
        }

        private void CloseBar()
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
            Close();
        }

        private void CmbFps_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((CmbFps.SelectedItem as ComboBoxItem)?.Tag is string tag &&
                int.TryParse(tag, out int fps))
            {
                _recorder.SetFps(fps);
                SettingsService.Instance.Current.GifFps = fps;
                SettingsService.Instance.Save();
            }
        }

        private void CmbFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((CmbFormat.SelectedItem as ComboBoxItem)?.Tag is string tag)
            {
                _format = tag;
                SettingsService.Instance.Current.RecorderFormat = tag;
                SettingsService.Instance.Save();
            }
        }

        private static void SelectTag(ComboBox comboBox, string tag)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(comboItem.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }
            }
        }
    }
}
