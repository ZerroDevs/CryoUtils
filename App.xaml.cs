using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CryoUtils.Services;
using CryoUtils.Tray;
using CryoUtils.Views;

namespace CryoUtils
{
    public partial class App : Application
    {
        public static App? CurrentApp { get; private set; }

        private TrayIconManager? _tray;
        private MainWindow? _dashboard;
        private ColorPickerWindow? _colorPicker;
        private ClipboardHistoryWindow? _clipboardWindow;
        private ToastWindow? _toast;
        private string? _lastLanguage;
        private bool _captureRestoresDashboard;
        private bool _pickerRestoresDashboard;
        private bool _exiting;

        public App()
        {
            CurrentApp = this;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            var settings = SettingsService.Instance.Current;
            _lastLanguage = settings.Language;
            ThemeService.Initialize(settings.Theme);
            LocalizationService.Instance.Apply(settings.Language);
            StartupService.SetEnabled(settings.StartWithWindows);

            // Handle CLI file action commands: CryoUtils.exe --action <action_id> "<file_path>"
            int actionIdx = Array.FindIndex(e.Args, a => string.Equals(a, "--action", StringComparison.OrdinalIgnoreCase));
            if (actionIdx >= 0 && actionIdx + 2 < e.Args.Length)
            {
                string action = e.Args[actionIdx + 1];
                string targetFile = e.Args[actionIdx + 2];
                _ = HandleCliActionAsync(action, targetFile);
                return;
            }

            if (settings.ContextMenuEnabled)
            {
                CryoContextMenuService.SetEnabled(true);
            }

            GlobalHotkeyService.Instance.HotkeyPressed += OnHotkeyPressed;
            SettingsService.Instance.SettingsChanged += ApplyAllSettings;

            ApplyHotkeys();
            ClipboardService.Instance.SetEnabled(settings.ClipboardHistoryEnabled);
            _tray = new TrayIconManager();

            bool background = e.Args.Any(argument =>
                string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
            if (!background && !settings.LaunchInBackground)
            {
                ShowDashboard();
            }
        }

        public bool IsDashboardVisible => _dashboard?.IsVisible == true;

        public void ShowDashboard(string tab = "Overview")
        {
            if (_exiting)
            {
                return;
            }

            if (_dashboard == null)
            {
                _dashboard = new MainWindow();
                _dashboard.Closed += (_, _) => _dashboard = null;
            }

            _dashboard.ShowTab(tab);
            if (!_dashboard.IsVisible)
            {
                _dashboard.Show();
            }
            if (_dashboard.WindowState == WindowState.Minimized)
            {
                _dashboard.WindowState = WindowState.Normal;
            }
            _dashboard.Activate();
            _dashboard.Topmost = true;
            _dashboard.Topmost = false;
            _dashboard.Focus();
        }

        public void StartCapture(int delaySeconds = -1)
        {
            if (!SettingsService.Instance.Current.CaptureEnabled)
            {
                ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }

            int delay = delaySeconds >= 0
                ? delaySeconds
                : SettingsService.Instance.Current.CaptureDelaySeconds;

            if (delay > 0)
            {
                PrepareForCapture();
                var countdown = new CountdownWindow(delay, () =>
                {
                    CaptureController.Instance.Start();
                });
                countdown.Show();
                return;
            }

            CaptureController.Instance.Start();
        }

        public void StartRecording()
        {
            if (!SettingsService.Instance.Current.CaptureEnabled)
            {
                ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }
            CaptureController.Instance.StartRecordingMode();
        }

        public void OpenColorPicker()
        {
            if (!SettingsService.Instance.Current.ColorPickerEnabled)
            {
                ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }

            if (_colorPicker != null)
            {
                _colorPicker.Activate();
                return;
            }

            _pickerRestoresDashboard = IsDashboardVisible;
            if (_pickerRestoresDashboard)
            {
                _dashboard?.Hide();
            }

            _colorPicker = new ColorPickerWindow();
            _colorPicker.Closed += (_, _) =>
            {
                _colorPicker = null;
                if (_pickerRestoresDashboard && !_exiting)
                {
                    _pickerRestoresDashboard = false;
                    ShowDashboard();
                }
            };
            _colorPicker.Show();
            _colorPicker.Activate();
        }

        public async void TogglePinnerFromDashboard()
        {
            if (!SettingsService.Instance.Current.WindowPinnerEnabled)
            {
                ShowToast(LocalizationService.Get("Module_OffNote"));
                return;
            }

            if (TopmostService.Instance.IsPinned)
            {
                TopmostService.Instance.Unpin();
                return;
            }

            bool restore = IsDashboardVisible;
            if (restore)
            {
                _dashboard?.Hide();
                await Task.Delay(180);
            }

            TopmostService.Instance.ToggleFocusedWindow();
            if (restore && !_exiting)
            {
                ShowDashboard();
            }
        }

        public void OpenClipboardHistory()
        {
            if (!SettingsService.Instance.Current.ClipboardHistoryEnabled)
            {
                ShowToast(LocalizationService.Get("Tools_ClipboardDisabled"));
                return;
            }

            if (_clipboardWindow != null)
            {
                _clipboardWindow.Activate();
                return;
            }

            _clipboardWindow = new ClipboardHistoryWindow();
            _clipboardWindow.Closed += (_, _) => _clipboardWindow = null;
            _clipboardWindow.Show();
            _clipboardWindow.Activate();
        }

        public void PrepareForCapture()
        {
            if (IsDashboardVisible)
            {
                _captureRestoresDashboard = true;
                _dashboard?.Hide();
            }

            _toast?.Close();
            _toast = null;
            if (_colorPicker != null)
            {
                _pickerRestoresDashboard = false;
                _colorPicker.Close();
            }
            if (_clipboardWindow != null)
            {
                _clipboardWindow.Close();
            }
        }

        public void FinishCapture()
        {
            if (!_captureRestoresDashboard || _exiting)
            {
                return;
            }

            _captureRestoresDashboard = false;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => ShowDashboard()));
        }

        public void ShowToast(string message)
        {
            if (_exiting || string.IsNullOrWhiteSpace(message) ||
                !SettingsService.Instance.Current.NotificationsEnabled)
            {
                return;
            }

            try
            {
                _toast?.Close();
                _toast = new ToastWindow(message);
                _toast.Closed += (_, _) => _toast = null;
                _toast.Show();
            }
            catch
            {
            }
        }

        public void ApplyAllSettings()
        {
            if (_exiting)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            ThemeService.Apply(settings.Theme);
            LocalizationService.Instance.Apply(settings.Language);
            if (!string.Equals(_lastLanguage, settings.Language, StringComparison.Ordinal))
            {
                _lastLanguage = settings.Language;
                if (settings.ContextMenuEnabled)
                {
                    CryoContextMenuService.RegisterAll();
                }
            }
            StartupService.SetEnabled(settings.StartWithWindows);
            ClipboardService.Instance.SetEnabled(settings.ClipboardHistoryEnabled);
            if (!settings.WindowPinnerEnabled)
            {
                TopmostService.Instance.Unpin(silent: true);
            }
            TopmostService.Instance.ApplySettings();
            ApplyHotkeys();
        }

        private void ApplyHotkeys()
        {
            var settings = SettingsService.Instance.Current;
            RegisterHotkey(
                GlobalHotkeyService.CaptureHotkeyId,
                settings.CaptureEnabled,
                settings.CaptureHotkeyModifiers,
                settings.CaptureHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.ColorPickerHotkeyId,
                settings.ColorPickerEnabled,
                settings.ColorPickerHotkeyModifiers,
                settings.ColorPickerHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.WindowPinnerHotkeyId,
                settings.WindowPinnerEnabled,
                settings.WindowPinnerHotkeyModifiers,
                settings.WindowPinnerHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.PlainPasteHotkeyId,
                settings.ClipboardHistoryEnabled,
                settings.PlainPasteHotkeyModifiers,
                settings.PlainPasteHotkeyKey);
            RegisterHotkey(
                GlobalHotkeyService.ClipboardHistoryHotkeyId,
                settings.ClipboardHistoryEnabled,
                settings.ClipboardHotkeyModifiers,
                settings.ClipboardHotkeyKey);
        }

        private static void RegisterHotkey(
            int id,
            bool enabled,
            string? modifiers,
            string? key)
        {
            if (!enabled)
            {
                GlobalHotkeyService.Instance.Unregister(id);
                return;
            }

            GlobalHotkeyService.Instance.Register(id, modifiers, key, out _);
        }

        private async void OnHotkeyPressed(int id)
        {
            switch (id)
            {
                case GlobalHotkeyService.CaptureHotkeyId:
                    StartCapture();
                    break;
                case GlobalHotkeyService.ColorPickerHotkeyId:
                    OpenColorPicker();
                    break;
                case GlobalHotkeyService.WindowPinnerHotkeyId:
                    if (!SettingsService.Instance.Current.WindowPinnerEnabled)
                    {
                        return;
                    }
                    if (IsDashboardVisible)
                    {
                        _dashboard?.Hide();
                        await Task.Delay(140);
                    }
                    TopmostService.Instance.ToggleFocusedWindow();
                    break;
                case GlobalHotkeyService.PlainPasteHotkeyId:
                    if (SettingsService.Instance.Current.ClipboardHistoryEnabled)
                    {
                        ClipboardService.Instance.PlainPaste();
                    }
                    break;
                case GlobalHotkeyService.ClipboardHistoryHotkeyId:
                    OpenClipboardHistory();
                    break;
            }
        }

        public void ExitApplication()
        {
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            SettingsService.Instance.Save();
            _dashboard?.PrepareForShutdown();
            _tray?.Dispose();
            _tray = null;
            GlobalHotkeyService.Instance.Dispose();
            ClipboardService.Instance.Dispose();
            TopmostService.Instance.RestoreForExit();
            ThemeService.Shutdown();
            Shutdown();
        }

        private async Task HandleCliActionAsync(string action, string rawTargetPath)
        {
            bool keepAliveForWindow = false;
            try
            {
                string filePath = (rawTargetPath ?? "").Trim().Trim('"');
                if (string.IsNullOrEmpty(filePath) ||
                    (!System.IO.File.Exists(filePath) && !System.IO.Directory.Exists(filePath)))
                {
                    Shutdown();
                    return;
                }

                bool isDir = System.IO.Directory.Exists(filePath);
                string dir = isDir ? filePath : (System.IO.Path.GetDirectoryName(filePath) ?? ".");
                string baseName = isDir
                    ? new System.IO.DirectoryInfo(filePath).Name
                    : System.IO.Path.GetFileNameWithoutExtension(filePath);
                string ext = isDir ? "" : System.IO.Path.GetExtension(filePath);

                switch (action.ToLowerInvariant())
                {
                    // Interactive window actions (process stays alive until window closes)
                    case "upload-file":
                        keepAliveForWindow = true;
                        var uploadWin = UploadProgressWindow.StartFileUpload(filePath);
                        uploadWin.Closed += (_, _) => Shutdown();
                        return;

                    case "ocr-text":
                        var ocrBmp = ImageProcessingService.LoadWpfBitmap(filePath);
                        string recognized = await OcrService.RecognizeAsync(ocrBmp);
                        if (string.IsNullOrWhiteSpace(recognized))
                        {
                            ShowToast(LocalizationService.Get("Capture_OcrEmpty"));
                            break;
                        }
                        CopyTextSafe(recognized);
                        keepAliveForWindow = true;
                        var ocrWin = new OcrResultWindow(recognized);
                        ocrWin.Closed += (_, _) => Shutdown();
                        ocrWin.Show();
                        ocrWin.Activate();
                        return;

                    case "scan-qr":
                        var qrSrcBmp = ImageProcessingService.LoadWpfBitmap(filePath);
                        var qrResult = QrCodeService.Decode(qrSrcBmp);
                        if (qrResult == null || string.IsNullOrWhiteSpace(qrResult.Text))
                        {
                            ShowToast(LocalizationService.Get("Qr_NotFound"));
                            break;
                        }
                        CopyTextSafe(qrResult.Text);
                        keepAliveForWindow = true;
                        var qrWin = new QrResultWindow(qrResult.Text, qrResult.FormatName, qrSrcBmp);
                        qrWin.Closed += (_, _) => Shutdown();
                        qrWin.Show();
                        qrWin.Activate();
                        return;

                    case "pin-image":
                        var pinBmp = ImageProcessingService.LoadWpfBitmap(filePath);
                        keepAliveForWindow = true;
                        var pinWin = new PinWindow(pinBmp);
                        pinWin.Closed += (_, _) => Shutdown();
                        pinWin.Show();
                        pinWin.Activate();
                        return;

                    case "text-to-qr":
                        string textContent = (await System.IO.File.ReadAllTextAsync(filePath)).Trim();
                        if (string.IsNullOrWhiteSpace(textContent))
                        {
                            ShowToast(LocalizationService.Format("Action_Failed", "File is empty."));
                            break;
                        }
                        if (textContent.Length > 2900)
                        {
                            ShowToast(LocalizationService.Format("Action_Failed", "Text too long for QR (max 2900 chars)."));
                            break;
                        }
                        var encodedQr = QrCodeService.Encode(textContent, 512);
                        keepAliveForWindow = true;
                        var textQrWin = new QrResultWindow(textContent, "QR_CODE", encodedQr);
                        textQrWin.Closed += (_, _) => Shutdown();
                        textQrWin.Show();
                        textQrWin.Activate();
                        return;

                    // PDF actions
                    case "pdf-to-png":
                        string pngDir = System.IO.Path.Combine(dir, $"{baseName}_pages");
                        int pages = await PdfToolsService.PdfToImagesAsync(filePath, pngDir, "PNG");
                        ShowToast(LocalizationService.Format("Action_Success", $"{pages} pages -> {baseName}_pages"));
                        break;

                    case "pdf-to-jpg":
                        string jpgDir = System.IO.Path.Combine(dir, $"{baseName}_pages");
                        int jpgPages = await PdfToolsService.PdfToImagesAsync(filePath, jpgDir, "JPG");
                        ShowToast(LocalizationService.Format("Action_Success", $"{jpgPages} pages -> {baseName}_pages"));
                        break;

                    case "compress-pdf":
                        string outCompPdf = System.IO.Path.Combine(dir, $"{baseName}_compressed.pdf");
                        long saved = await PdfToolsService.CompressPdfAsync(filePath, outCompPdf);
                        ShowToast(LocalizationService.Format("Action_Success", $"{baseName}_compressed.pdf ({saved / 1024} KB saved)"));
                        break;

                    case "pdf-grayscale":
                        string outGrayPdf = System.IO.Path.Combine(dir, $"{baseName}_grayscale.pdf");
                        await PdfToolsService.ConvertPdfToGrayscaleAsync(filePath, outGrayPdf);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outGrayPdf)));
                        break;

                    case "pdf-rotate-90":
                        string outRot90Pdf = System.IO.Path.Combine(dir, $"{baseName}_rot90.pdf");
                        await PdfToolsService.RotatePdfAsync(filePath, outRot90Pdf, 90);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outRot90Pdf)));
                        break;

                    case "pdf-rotate-180":
                        string outRot180Pdf = System.IO.Path.Combine(dir, $"{baseName}_rot180.pdf");
                        await PdfToolsService.RotatePdfAsync(filePath, outRot180Pdf, 180);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outRot180Pdf)));
                        break;

                    // Image actions
                    case "img-to-pdf":
                        string outPdf = System.IO.Path.Combine(dir, $"{baseName}.pdf");
                        PdfToolsService.ImagesToPdf(new[] { filePath }, outPdf);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outPdf)));
                        break;

                    case "quick-compress":
                        string outCompImg = System.IO.Path.Combine(dir, $"{baseName}_min{ext}");
                        var res = await CompressionService.CompressAsync(filePath, "Balanced", 75, 100);
                        await System.IO.File.WriteAllBytesAsync(outCompImg, res.Data);
                        long imgSaved = res.OriginalBytes - res.CompressedBytes;
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(outCompImg)} ({imgSaved / 1024} KB saved)"));
                        break;

                    case "remove-bg":
                        string outNoBg = System.IO.Path.Combine(dir, $"{baseName}_nobg.png");
                        ImageProcessingService.RemoveBackground(filePath, outNoBg);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outNoBg)));
                        break;

                    case "trim-alpha":
                        string outTrim = System.IO.Path.Combine(dir, $"{baseName}_trimmed.png");
                        var (tw, th) = ImageProcessingService.TrimTransparentMargins(filePath, outTrim);
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(outTrim)} ({tw}x{th})"));
                        break;

                    case "flatten-white":
                        string outFlat = System.IO.Path.Combine(dir, $"{baseName}_flat.jpg");
                        ImageProcessingService.FlattenWhite(filePath, outFlat);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outFlat)));
                        break;

                    case "strip-exif":
                        string outClean = System.IO.Path.Combine(dir, $"{baseName}_clean{ext}");
                        ImageProcessingService.StripExif(filePath, outClean);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outClean)));
                        break;

                    case "resize-50":
                        string out50 = System.IO.Path.Combine(dir, $"{baseName}_50pct{ext}");
                        var (w50, h50) = ImageProcessingService.ResizePreset(filePath, out50, "50pct");
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(out50)} ({w50}x{h50})"));
                        break;

                    case "resize-1920":
                        string out1920 = System.IO.Path.Combine(dir, $"{baseName}_1920w{ext}");
                        var (w19, h19) = ImageProcessingService.ResizePreset(filePath, out1920, "w1920");
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(out1920)} ({w19}x{h19})"));
                        break;

                    case "resize-512":
                        string out512 = System.IO.Path.Combine(dir, $"{baseName}_512px{ext}");
                        var (w51, h51) = ImageProcessingService.ResizePreset(filePath, out512, "s512");
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(out512)} ({w51}x{h51})"));
                        break;

                    case "gif-frames":
                        string framesDir = System.IO.Path.Combine(dir, $"{baseName}_frames");
                        int frameCnt = ImageProcessingService.ExtractGifFrames(filePath, framesDir);
                        ShowToast(LocalizationService.Format("Action_Success", $"{frameCnt} frames -> {baseName}_frames"));
                        break;

                    case "copy-base64":
                        string dataUri = ImageProcessingService.ToBase64DataUri(filePath);
                        CopyTextSafe(dataUri);
                        ShowToast(LocalizationService.Get("Common_Copied"));
                        break;

                    case "copy-pixels":
                        var clipBmp = ImageProcessingService.LoadWpfBitmap(filePath);
                        CopyImageSafe(clipBmp);
                        ShowToast(LocalizationService.Get("Capture_Copied"));
                        break;

                    case "convert-to-png":
                        string outPng = System.IO.Path.Combine(dir, $"{baseName}.png");
                        ImageProcessingService.ConvertFormat(filePath, outPng, "PNG");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outPng)));
                        break;

                    case "convert-to-jpg":
                        string outJpg = System.IO.Path.Combine(dir, $"{baseName}.jpg");
                        ImageProcessingService.ConvertFormat(filePath, outJpg, "JPG");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outJpg)));
                        break;

                    case "convert-to-webp":
                        string outWebp = System.IO.Path.Combine(dir, $"{baseName}.webp");
                        ImageProcessingService.ConvertFormat(filePath, outWebp, "WEBP");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outWebp)));
                        break;

                    case "convert-to-ico":
                        string outIco = System.IO.Path.Combine(dir, $"{baseName}.ico");
                        ImageProcessingService.ConvertFormat(filePath, outIco, "ICO");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outIco)));
                        break;

                    // Video & Audio actions
                    case "video-compress":
                        string outVidComp = System.IO.Path.Combine(dir, $"{baseName}_compressed.mp4");
                        long vidSaved = await MediaConversionService.CompressVideoForDiscordAsync(filePath, outVidComp);
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(outVidComp)} ({vidSaved / 1024} KB saved)"));
                        break;

                    case "video-mute":
                        string outVidMute = System.IO.Path.Combine(dir, $"{baseName}_muted.mp4");
                        await MediaConversionService.MuteVideoAsync(filePath, outVidMute);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outVidMute)));
                        break;

                    case "video-thumbnail":
                        string outThumb = System.IO.Path.Combine(dir, $"{baseName}_thumb.jpg");
                        await MediaConversionService.SaveThumbnailAsync(filePath, outThumb);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outThumb)));
                        break;

                    case "video-to-mp3":
                    case "audio-to-mp3":
                        string outMp3 = SafeOutPath(dir, baseName, ext, ".mp3");
                        await MediaConversionService.ConvertAudioAsync(filePath, outMp3, "MP3");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outMp3)));
                        break;

                    case "audio-to-wav":
                        string outWav = SafeOutPath(dir, baseName, ext, ".wav");
                        await MediaConversionService.ConvertAudioAsync(filePath, outWav, "WAV");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outWav)));
                        break;

                    case "audio-to-m4a":
                        string outM4a = SafeOutPath(dir, baseName, ext, ".m4a");
                        await MediaConversionService.ConvertAudioAsync(filePath, outM4a, "M4A");
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outM4a)));
                        break;

                    case "video-to-gif":
                        string outGif = System.IO.Path.Combine(dir, $"{baseName}.gif");
                        int frames = await MediaConversionService.VideoToGifAsync(filePath, outGif);
                        ShowToast(LocalizationService.Format("Action_Success", $"{System.IO.Path.GetFileName(outGif)} ({frames} frames)"));
                        break;

                    // Developer & Checksums
                    case "json-pretty":
                        string outPrettyJson = System.IO.Path.Combine(dir, $"{baseName}.pretty.json");
                        FileToolsService.FormatJson(filePath, outPrettyJson, indent: true);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outPrettyJson)));
                        break;

                    case "json-minify":
                        string outMinJson = System.IO.Path.Combine(dir, $"{baseName}.min.json");
                        FileToolsService.FormatJson(filePath, outMinJson, indent: false);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outMinJson)));
                        break;

                    case "xml-pretty":
                        string outPrettyXml = System.IO.Path.Combine(dir, $"{baseName}.pretty.xml");
                        FileToolsService.FormatXml(filePath, outPrettyXml);
                        ShowToast(LocalizationService.Format("Action_Success", System.IO.Path.GetFileName(outPrettyXml)));
                        break;

                    case "hash-sha256":
                        string sha256 = FileToolsService.ComputeHash(filePath, System.Security.Cryptography.HashAlgorithmName.SHA256);
                        CopyTextSafe(sha256);
                        ShowToast($"SHA-256: {sha256[..Math.Min(16, sha256.Length)]}...");
                        break;

                    case "hash-md5":
                        string md5 = FileToolsService.ComputeHash(filePath, System.Security.Cryptography.HashAlgorithmName.MD5);
                        CopyTextSafe(md5);
                        ShowToast($"MD5: {md5}");
                        break;

                    // Directory batch tools
                    case "dir-imgs-pdf":
                        var dirImgs = FileToolsService.CollectSortedImages(filePath);
                        if (dirImgs.Count == 0)
                        {
                            ShowToast(LocalizationService.Format("Action_Failed", "No images in folder."));
                            break;
                        }
                        string outDirPdf = System.IO.Path.Combine(filePath, $"{baseName}_merged.pdf");
                        int mergedCount = PdfToolsService.ImagesToPdf(dirImgs, outDirPdf);
                        ShowToast(LocalizationService.Format("Action_Success", $"{mergedCount} images -> {System.IO.Path.GetFileName(outDirPdf)}"));
                        break;

                    case "dir-imgs-gif":
                        string outDirGif = System.IO.Path.Combine(filePath, $"{baseName}.gif");
                        int gifCount = FileToolsService.FolderImagesToGif(filePath, outDirGif);
                        ShowToast(gifCount > 0
                            ? LocalizationService.Format("Action_Success", $"{gifCount} frames -> {System.IO.Path.GetFileName(outDirGif)}")
                            : LocalizationService.Format("Action_Failed", "No images in folder."));
                        break;

                    case "dir-compress-imgs":
                        var (compCount, compSaved) = await FileToolsService.BatchCompressFolderAsync(filePath);
                        ShowToast(compCount > 0
                            ? LocalizationService.Format("Action_Success", $"{compCount} images -> _compressed ({compSaved / 1024} KB saved)")
                            : LocalizationService.Format("Action_Failed", "No images in folder."));
                        break;
                }

                await Task.Delay(2600);
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                string logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cli.log");
                System.IO.File.AppendAllText(logFile, $"ERROR [{action}]: {ex}\n");
                ShowToast(LocalizationService.Format("Action_Failed", ex.Message));
                if (!keepAliveForWindow)
                {
                    await Task.Delay(2600);
                }
            }
            finally
            {
                if (!keepAliveForWindow)
                {
                    Shutdown();
                }
            }
        }

        private static string SafeOutPath(string dir, string baseName, string sourceExt, string targetExt)
        {
            string name = string.Equals(sourceExt, targetExt, StringComparison.OrdinalIgnoreCase)
                ? $"{baseName}_converted{targetExt}"
                : $"{baseName}{targetExt}";
            return System.IO.Path.Combine(dir, name);
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

        private static void CopyImageSafe(System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            try
            {
                Clipboard.SetImage(bitmap);
            }
            catch
            {
                try
                {
                    var data = new DataObject();
                    data.SetImage(bitmap);
                    Clipboard.SetDataObject(data, true);
                }
                catch
                {
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            _tray = null;
            GlobalHotkeyService.Instance.Dispose();
            ClipboardService.Instance.Dispose();
            TopmostService.Instance.RestoreForExit();
            ThemeService.Shutdown();
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            ShowToast(LocalizationService.Format("Error_Body", e.Exception.Message));
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    ShowToast(LocalizationService.Format("Error_Body", exception.Message))));
            }
        }
    }
}
