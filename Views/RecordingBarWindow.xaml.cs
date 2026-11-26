using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using CryoUtils.Services;
using CryoUtils.Services.Recording;
using Microsoft.Win32;

namespace CryoUtils.Views
{
    public partial class RecordingBarWindow : Window
    {
        private readonly Int32Rect _region;
        private readonly RecorderService _gifRecorder = new();
        private readonly RecordingFrameWindow _frame = new();
        private readonly DispatcherTimer _timer;
        private RecordingResult? _gifResult;
        private RecordingSession? _session;
        private string _format = "GIF";
        private bool _stopping;
        private bool _encoding;
        private bool _finished;
        private bool _loading = true;
        private string _sessionStatus = "";
        private int _maxSeconds = 300;

        public RecordingBarWindow(Int32Rect region)
        {
            InitializeComponent();
            _region = region;
            FlowDirection = LocalizationService.Instance.FlowDirection;

            var settings = SettingsService.Instance.Current;
            _format = settings.RecorderFormat;
            _maxSeconds = Math.Clamp(settings.MaxRecordingSeconds, 60, 3600);
            SelectTag(CmbFormat, _format);
            PopulateFpsItems();
            _loading = false;

            _gifRecorder.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateUi));
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _timer.Tick += (_, _) => UpdateUi();

            Loaded += RecordingBarWindow_Loaded;
            Closed += (_, _) =>
            {
                _timer.Stop();
                _frame.Close();
                if (_format == "MP4")
                {
                    _session?.Cancel();
                    _session?.Dispose();
                    _session = null;
                }
                else if (_gifRecorder.IsRecording)
                {
                    _gifRecorder.Cancel();
                }
                _gifRecorder.Dispose();
                (Application.Current as App)?.SetRecordingState(false);
                (Application.Current as App)?.FinishCapture();
            };
        }


        private void RecordingBarWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PositionWindow();
            _frame.Show();
            _frame.SetBounds(_region);
            var settings = SettingsService.Instance.Current;
            if (_format == "MP4")
            {
                var options = RecordingOptions.FromSettings(settings);
                _session = new RecordingSession(_region, options);
                _session.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateUi));
                _session.StatusChanged += message => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _sessionStatus = message;
                    UpdateUi();
                }));
                try
                {
                    _session.Start();
                    (Application.Current as App)?.SetRecordingState(true);
                }
                catch (Exception ex)
                {
                    (Application.Current as App)?.ShowToast(
                        LocalizationService.Format("Error_Body", ex.Message));
                    CloseBar();
                    return;
                }

                CmbFormat.IsEnabled = false;
                CmbFps.IsEnabled = false;
            }
            else
            {
                _maxSeconds = Math.Min(120, _maxSeconds);
                _gifRecorder.Start(
                    _region,
                    Math.Clamp(settings.GifFps, 5, 30),
                    _maxSeconds);
                CmbFormat.IsEnabled = false;
                (Application.Current as App)?.SetRecordingState(true);
            }

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

            TimeSpan elapsed;
            int frames;
            long estimate;
            if (_format == "MP4" && _session != null)
            {
                elapsed = _session.Elapsed;
                frames = (int)Math.Min(int.MaxValue, _session.Frames);
                estimate = 0;
                try
                {
                    if (_session.TemporaryPath.Length > 0 && File.Exists(_session.TemporaryPath))
                    {
                        estimate = new FileInfo(_session.TemporaryPath).Length;
                    }
                }
                catch
                {
                }
                TxtTimer.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
                TxtFrameInfo.Text = LocalizationService.Format("Rec_FrameCount", frames);
                TxtEstimate.Text = $"{_session.Width}×{_session.Height} · {_session.Fps} FPS";
                TxtWarning.Visibility = _session.LooksBlack ? Visibility.Visible : Visibility.Collapsed;
                BtnPause.Content = _session.IsPaused
                    ? LocalizationService.Get("Rec_Resume")
                    : LocalizationService.Get("Rec_Pause");
                BtnPause.IsEnabled = _session.IsRecording && !_encoding;
                BtnStopSave.IsEnabled = !_encoding;
                BtnStopCopy.IsEnabled = !_encoding;
                string state = _sessionStatus.Length > 0
                    ? _sessionStatus
                    : _session.IsPaused
                        ? LocalizationService.Get("Rec_Paused")
                        : _session.IsUsingWgc ? "WGC" : "GDI";
                if (_session.DroppedFrames > 0)
                {
                    state += " · " + LocalizationService.Format(
                        "Rec_Dropped",
                        _session.DroppedFrames);
                }
                TxtState.Text = state;
                if (_session.IsRecording && !_stopping && elapsed.TotalSeconds >= _maxSeconds)
                {
                    _ = AutoStopAsync();
                }
                return;
            }

            elapsed = _gifRecorder.Elapsed;
            frames = _gifRecorder.FrameCount;
            estimate = _gifRecorder.EstimatedBytes;
            TxtTimer.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
            TxtWarning.Visibility = Visibility.Collapsed;
            TxtFrameInfo.Text = LocalizationService.Format("Rec_FrameCount", frames);
            TxtEstimate.Text = LocalizationService.Format(
                "Rec_Estimate",
                ImageIO.FormatBytes(estimate));
            BtnPause.Content = _gifRecorder.IsPaused
                ? LocalizationService.Get("Rec_Resume")
                : LocalizationService.Get("Rec_Pause");
            BtnPause.IsEnabled = _gifRecorder.IsRecording && !_encoding;
            BtnStopSave.IsEnabled = !_encoding;
            BtnStopCopy.IsEnabled = !_encoding;
            TxtState.Text = _encoding
                ? LocalizationService.Get("Rec_Encoding")
                : _gifRecorder.IsPaused
                    ? LocalizationService.Get("Rec_Paused")
                    : "";
            if (_gifRecorder.IsRecording && !_stopping && elapsed.TotalSeconds >= _maxSeconds)
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
                if (_format == "MP4")
                {
                    await StopAndSaveAsync(copyToClipboard: SettingsService.Instance.Current.RecorderCopyOnStop, showSaveDialog: false);
                }
                else
                {
                    await StopGifAsync(copyToClipboard: SettingsService.Instance.Current.RecorderCopyOnStop, showSaveDialog: false);
                }
            }
            finally
            {
                _stopping = false;
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (_format == "MP4")
            {
                if (_session == null)
                {
                    return;
                }
                if (_session.IsPaused)
                {
                    _session.Resume();
                }
                else
                {
                    _session.Pause();
                }
            }
            else if (_gifRecorder.IsPaused)
            {
                _gifRecorder.Resume();
            }
            else
            {
                _gifRecorder.Pause();
            }
            UpdateUi();
        }

        private async void BtnStopSave_Click(object sender, RoutedEventArgs e)
        {
            if (_encoding)
            {
                return;
            }

            _stopping = true;
            try
            {
                if (_format == "MP4")
                {
                    await StopAndSaveAsync(copyToClipboard: false, showSaveDialog: true);
                }
                else
                {
                    await StopGifAsync(copyToClipboard: false, showSaveDialog: true);
                }
            }
            finally
            {
                _stopping = false;
            }
        }

        private async void BtnStopCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_encoding)
            {
                return;
            }

            _stopping = true;
            try
            {
                if (_format == "MP4")
                {
                    await StopAndSaveAsync(copyToClipboard: true, showSaveDialog: false);
                }
                else
                {
                    await StopGifAsync(copyToClipboard: true, showSaveDialog: false);
                }
            }
            finally
            {
                _stopping = false;
            }
        }

        private async Task StopAndSaveAsync(bool copyToClipboard, bool showSaveDialog)
        {
            if (_session == null)
            {
                CloseBar();
                return;
            }

            _encoding = true;
            UpdateUi();
            string temp = "";
            try
            {
                temp = await _session.StopAsync();
                if (_session.FinalizeFailed)
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Format(
                        "Error_Body",
                        _session.LastError));
                    return;
                }

                if (string.IsNullOrEmpty(temp) || !File.Exists(temp) || new FileInfo(temp).Length == 0)
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Rec_NoFrames"));
                    return;
                }

                var options = RecordingOptions.FromSettings(SettingsService.Instance.Current);
                string destination = "";
                if (options.TrimBeforeSave)
                {
                    string suggested = Path.Combine(
                        SettingsService.Instance.EffectiveSaveFolder,
                        ScreenCaptureService.CreateTimestampedFileName(".mp4"));
                    var trim = new RecordingTrimWindow(temp, suggested)
                    {
                        Owner = this
                    };
                    if (trim.ShowDialog() == true && !string.IsNullOrWhiteSpace(trim.SavedPath))
                    {
                        destination = trim.SavedPath;
                        TryDelete(temp);
                    }
                    else
                    {
                        // Cancelling trim means keep the original take. Do not open
                        // another save dialog here: a second cancel used to delete
                        // the only copy of a successfully recorded video.
                        destination = Path.Combine(
                            SettingsService.Instance.EffectiveSaveFolder,
                            ScreenCaptureService.CreateTimestampedFileName(".mp4"));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Move(temp, destination, overwrite: true);
                    }
                }
                else
                {
                    destination = showSaveDialog
                        ? AskSavePath()
                        : Path.Combine(
                            SettingsService.Instance.EffectiveSaveFolder,
                            ScreenCaptureService.CreateTimestampedFileName(".mp4"));
                    if (string.IsNullOrEmpty(destination))
                    {
                        return;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(temp, destination, overwrite: true);
                }

                await FinishSavedVideoAsync(destination, copyToClipboard);
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Error_Body", ex.Message));
            }
            finally
            {
                TryDelete(temp);
                _encoding = false;
                CloseBar();
            }
        }

        private string AskSavePath()
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Rec_StopSave"),
                Filter = LocalizationService.Get("Common_SaveRecordingFilter"),
                FilterIndex = 2,
                DefaultExt = ".mp4",
                AddExtension = true,
                FileName = ScreenCaptureService.CreateTimestampedFileName(".mp4"),
                InitialDirectory = SettingsService.Instance.EffectiveSaveFolder
            };
            Directory.CreateDirectory(dialog.InitialDirectory);
            return dialog.ShowDialog(this) == true ? dialog.FileName : "";
        }

        private async Task FinishSavedVideoAsync(string path, bool copyToClipboard)
        {
            ScreenCaptureService.EnforceSavedCaptureLimit();
            var frame = ImageIO.CreateVideoThumbnailPlaceholder(320, 180);
            string thumbPath = Path.Combine(
                Path.GetTempPath(),
                "CryoUtils",
                $"rec_thumb_{Guid.NewGuid():N}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);
            try
            {
                try
                {
                    await MediaConversionService.SaveThumbnailAsync(path, thumbPath);
                    frame = ImageIO.LoadThumbnail(thumbPath, 320);
                }
                catch
                {
                    // Keep the video discoverable even when Windows cannot extract a poster frame.
                }
            }
            finally
            {
                TryDelete(thumbPath);
            }

            RecentCaptureService.Instance.Add(frame, path, LocalizationService.Get("Rec_Title"));
            try
            {
                CaptureLibraryService.Instance.Record(
                    path,
                    "CryoUtils",
                    LocalizationService.Get("Rec_Title"),
                    _session?.Width ?? 0,
                    _session?.Height ?? 0);
            }
            catch
            {
                // Library metadata is optional; the saved video still belongs in Recent recordings.
            }

            if (copyToClipboard)
            {
                Clipboard.SetFileDropList(new StringCollection { path });
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_RecordCopied"));
            }
            else
            {
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Capture_RecordSaved", path));
            }
        }

        private async Task StopGifAsync(bool copyToClipboard, bool showSaveDialog)
        {
            var result = await GetGifResultAsync();
            if (result == null || result.PngFrames.Count == 0)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Rec_NoFrames"));
                CloseBar();
                return;
            }

            string path;
            if (showSaveDialog)
            {
                var dialog = new SaveFileDialog
                {
                    Title = LocalizationService.Get("Rec_StopSave"),
                    Filter = LocalizationService.Get("Common_SaveRecordingFilter"),
                    FilterIndex = 1,
                    DefaultExt = ".gif",
                    AddExtension = true,
                    FileName = ScreenCaptureService.CreateTimestampedFileName(".gif"),
                    InitialDirectory = SettingsService.Instance.EffectiveSaveFolder
                };
                Directory.CreateDirectory(dialog.InitialDirectory);
                if (dialog.ShowDialog(this) != true)
                {
                    CloseBar();
                    return;
                }
                path = dialog.FileName;
            }
            else
            {
                string folder = SettingsService.Instance.EffectiveSaveFolder;
                Directory.CreateDirectory(folder);
                path = Path.Combine(folder, ScreenCaptureService.CreateTimestampedFileName(".gif"));
            }

            _encoding = true;
            UpdateUi();
            try
            {
                await Task.Run(() => GifWriter.EncodePngFramesToGif(
                    result.PngFrames,
                    result.Width,
                    result.Height,
                    result.Fps,
                    path));
                var preview = ImageIO.DecodeBytes(result.PngFrames[0]);
                ScreenCaptureService.EnforceSavedCaptureLimit();
                RecentCaptureService.Instance.Add(
                    preview,
                    path,
                    LocalizationService.Get("Rec_Title"));
                try
                {
                    CaptureLibraryService.Instance.Record(
                        path,
                        "CryoUtils",
                        LocalizationService.Get("Rec_Title"),
                        result.Width,
                        result.Height);
                }
                catch
                {
                    // Library metadata is optional; the saved GIF still belongs in Recent recordings.
                }
                if (copyToClipboard)
                {
                    Clipboard.SetFileDropList(new StringCollection { path });
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

        private async Task<RecordingResult?> GetGifResultAsync()
        {
            if (_gifResult != null)
            {
                return _gifResult;
            }

            _gifResult = await _gifRecorder.StopAsync();
            return _gifResult;
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = SettingsService.Instance.EffectiveSaveFolder;
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(
                    LocalizationService.Format("Error_Body", ex.Message));
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _session?.Cancel();
            _gifRecorder.Cancel();
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
            if (_loading)
            {
                return;
            }

            if ((CmbFps.SelectedItem as ComboBoxItem)?.Tag is string tag &&
                int.TryParse(tag, out int fps))
            {
                if (_format == "MP4")
                {
                    SettingsService.Instance.Current.RecorderFps = fps >= 60 ? 60 : 30;
                }
                else
                {
                    int gifFps = Math.Clamp(fps, 5, 30);
                    _gifRecorder.SetFps(gifFps);
                    SettingsService.Instance.Current.GifFps = gifFps;
                }
                SettingsService.Instance.Save();
            }
        }

        private void CmbFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            if ((CmbFormat.SelectedItem as ComboBoxItem)?.Tag is string tag)
            {
                _format = tag;
                SettingsService.Instance.Current.RecorderFormat = tag;
                SettingsService.Instance.Save();
                PopulateFpsItems();
            }
        }

        private void PopulateFpsItems()
        {
            bool wasLoading = _loading;
            _loading = true;
            CmbFps.Items.Clear();
            int[] values = _format == "MP4"
                ? new[] { 30, 60 }
                : new[] { 5, 10, 15, 20, 24, 30 };
            foreach (int fps in values)
            {
                CmbFps.Items.Add(new ComboBoxItem
                {
                    Content = fps.ToString(),
                    Tag = fps.ToString()
                });
            }

            int selected = _format == "MP4"
                ? (SettingsService.Instance.Current.RecorderFps >= 60 ? 60 : 30)
                : Math.Clamp(SettingsService.Instance.Current.GifFps, 5, 30);
            SelectTag(CmbFps, selected.ToString());
            _loading = wasLoading;
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

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
