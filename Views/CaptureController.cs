using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Models;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public sealed class CaptureController
    {
        private static CaptureController? _instance;
        public static CaptureController Instance => _instance ??= new CaptureController();

        private readonly List<CaptureOverlayWindow> _windows = new();
        private readonly Stack<List<Annotation>> _undo = new();
        private readonly Stack<List<Annotation>> _redo = new();
        private bool _starting;
        private bool _recordAfterSelection;

        public NativeMethods.RECT VirtualBounds { get; private set; }
        public BitmapSource? VirtualBitmap { get; private set; }
        public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = Array.Empty<MonitorInfo>();
        public Rect? Selection { get; private set; }
        public List<Annotation> Annotations { get; } = new();
        public CaptureTool Tool { get; private set; } = CaptureTool.Select;
        public string CurrentColor { get; private set; } = AppSettings.DefaultAnnotationColor;
        public double Thickness { get; private set; } = 3;
        public int NextStep { get; set; } = 1;
        public MonitorInfo? ActiveMonitor { get; set; }
        public CaptureOverlayWindow? ActiveOverlay { get; set; }
        public bool IsActive { get; private set; }
        public bool IsClosing { get; private set; }
        public bool ShowCrosshair { get; set; } = true;
        public bool RecordAfterSelection
        {
            get => _recordAfterSelection;
            set => _recordAfterSelection = value;
        }

        public event Action? StateChanged;

        private CaptureController()
        {
        }

        public void Start()
        {
            Start(recordMode: false);
        }

        public void StartForImage(BitmapSource image, string? originalPath = null)
        {
            if (IsActive || _starting)
            {
                return;
            }

            _recordAfterSelection = false;
            _ = StartForImageAsync(image, originalPath);
        }

        public void StartRecordingMode()
        {
            Start(recordMode: true);
        }

        private void Start(bool recordMode)
        {
            if (IsActive || _starting)
            {
                return;
            }

            _recordAfterSelection = recordMode;
            _ = StartAsync();
        }

        private async System.Threading.Tasks.Task StartForImageAsync(BitmapSource image, string? originalPath)
        {
            _starting = true;
            try
            {
                var app = Application.Current as App;
                app?.PrepareForCapture();
                await System.Threading.Tasks.Task.Delay(150);

                var settings = SettingsService.Instance.Current;
                Tool = CaptureTool.Select;
                CurrentColor = string.IsNullOrWhiteSpace(settings.AnnotationColor)
                    ? AppSettings.DefaultAnnotationColor
                    : settings.AnnotationColor;
                Thickness = Math.Clamp(settings.AnnotationThickness, 1, 24);
                NextStep = Math.Max(1, settings.StepStartNumber);
                Annotations.Clear();
                _undo.Clear();
                _redo.Clear();
                IsClosing = false;
                VirtualBounds = ScreenCaptureService.GetVirtualBounds();
                var desktopBitmap = ScreenCaptureService.CaptureRegion(VirtualBounds);
                Monitors = ScreenCaptureService.GetMonitors();

                NativeMethods.GetCursorPos(out var cursor);
                ActiveMonitor = Monitors.FirstOrDefault(monitor =>
                    cursor.X >= monitor.Bounds.Left && cursor.X < monitor.Bounds.Right &&
                    cursor.Y >= monitor.Bounds.Top && cursor.Y < monitor.Bounds.Bottom) ?? Monitors.FirstOrDefault();

                var targetMonitor = ActiveMonitor ?? Monitors.FirstOrDefault();
                int monitorLeft = targetMonitor != null ? targetMonitor.Bounds.Left : VirtualBounds.Left;
                int monitorTop = targetMonitor != null ? targetMonitor.Bounds.Top : VirtualBounds.Top;
                int monitorWidth = targetMonitor != null ? targetMonitor.Bounds.Width : VirtualBounds.Width;
                int monitorHeight = targetMonitor != null ? targetMonitor.Bounds.Height : VirtualBounds.Height;

                int imgW = image.PixelWidth;
                int imgH = image.PixelHeight;

                // Scale down if image is larger than the monitor with margins for toolbars
                double maxW = Math.Max(200, monitorWidth - 140);
                double maxH = Math.Max(200, monitorHeight - 140);
                if (imgW > maxW || imgH > maxH)
                {
                    double scale = Math.Min(maxW / imgW, maxH / imgH);
                    if (scale < 0.999)
                    {
                        image = ImageIO.Resize(image, scale);
                        imgW = image.PixelWidth;
                        imgH = image.PixelHeight;
                    }
                }

                int targetX = monitorLeft + Math.Max(0, (monitorWidth - imgW) / 2);
                int targetY = monitorTop + Math.Max(0, (monitorHeight - imgH) / 2);

                if (targetX + imgW > VirtualBounds.Right)
                {
                    targetX = Math.Max(VirtualBounds.Left, VirtualBounds.Right - imgW);
                }
                if (targetY + imgH > VirtualBounds.Bottom)
                {
                    targetY = Math.Max(VirtualBounds.Top, VirtualBounds.Bottom - imgH);
                }
                targetX = Math.Max(VirtualBounds.Left, targetX);
                targetY = Math.Max(VirtualBounds.Top, targetY);

                int clampedW = Math.Min(imgW, VirtualBounds.Right - targetX);
                int clampedH = Math.Min(imgH, VirtualBounds.Bottom - targetY);

                var visual = new DrawingVisual();
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawImage(desktopBitmap, new Rect(0, 0, desktopBitmap.PixelWidth, desktopBitmap.PixelHeight));
                    int relX = targetX - VirtualBounds.Left;
                    int relY = targetY - VirtualBounds.Top;
                    dc.DrawImage(image, new Rect(relX, relY, clampedW, clampedH));
                }

                var rtb = new RenderTargetBitmap(desktopBitmap.PixelWidth, desktopBitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();
                VirtualBitmap = rtb;

                Selection = new Rect(targetX, targetY, clampedW, clampedH);

                foreach (var monitor in Monitors)
                {
                    var overlay = new CaptureOverlayWindow(this, monitor);
                    _windows.Add(overlay);
                }

                IsActive = true;
                foreach (var overlay in _windows)
                {
                    overlay.Show();
                }

                ActiveOverlay = _windows.FirstOrDefault(w => w.Monitor == targetMonitor) ?? _windows.FirstOrDefault();
                if (ActiveOverlay != null)
                {
                    ActiveOverlay.Activate();
                    ActiveOverlay.Focus();
                }

                NotifyChanged();
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Format("Error_Body", ex.Message));
                CloseAll();
            }
            finally
            {
                _starting = false;
            }
        }

        private async System.Threading.Tasks.Task StartAsync()
        {
            _starting = true;
            try
            {
                var app = Application.Current as App;
                app?.PrepareForCapture();
                await System.Threading.Tasks.Task.Delay(150);

                var settings = SettingsService.Instance.Current;
                Tool = CaptureTool.Select;
                CurrentColor = string.IsNullOrWhiteSpace(settings.AnnotationColor)
                    ? AppSettings.DefaultAnnotationColor
                    : settings.AnnotationColor;
                Thickness = Math.Clamp(settings.AnnotationThickness, 1, 24);
                NextStep = Math.Max(1, settings.StepStartNumber);
                Selection = null;
                Annotations.Clear();
                _undo.Clear();
                _redo.Clear();
                IsClosing = false;
                VirtualBounds = ScreenCaptureService.GetVirtualBounds();
                VirtualBitmap = ScreenCaptureService.CaptureRegion(VirtualBounds);
                Monitors = ScreenCaptureService.GetMonitors();

                NativeMethods.GetCursorPos(out var cursor);
                ActiveMonitor = Monitors.FirstOrDefault(monitor =>
                    cursor.X >= monitor.Bounds.Left && cursor.X < monitor.Bounds.Right &&
                    cursor.Y >= monitor.Bounds.Top && cursor.Y < monitor.Bounds.Bottom) ?? Monitors.FirstOrDefault();

                foreach (var monitor in Monitors)
                {
                    var overlay = new CaptureOverlayWindow(this, monitor);
                    _windows.Add(overlay);
                }

                IsActive = true;
                foreach (var overlay in _windows)
                {
                    overlay.Show();
                }

                if (ActiveOverlay != null)
                {
                    ActiveOverlay.Activate();
                    ActiveOverlay.Focus();
                }

                NotifyChanged();
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Format("Error_Body", ex.Message));
                CloseAll();
            }
            finally
            {
                _starting = false;
            }
        }

        public void AttachOverlay(CaptureOverlayWindow overlay)
        {
            if (ActiveOverlay == null || overlay.Monitor == ActiveMonitor)
            {
                ActiveOverlay = overlay;
            }
        }

        public void NotifyChanged()
        {
            StateChanged?.Invoke();
        }

        public void SetSelection(Rect? selection)
        {
            if (selection.HasValue)
            {
                var bounds = new Rect(
                    VirtualBounds.Left,
                    VirtualBounds.Top,
                    VirtualBounds.Width,
                    VirtualBounds.Height);
                var clamped = Rect.Intersect(selection.Value, bounds);
                if (clamped.Width < 3 || clamped.Height < 3)
                {
                    Selection = null;
                }
                else
                {
                    Selection = clamped;
                }
            }
            else
            {
                Selection = null;
            }

            NotifyChanged();
        }

        public void SelectAll()
        {
            Selection = new Rect(
                VirtualBounds.Left,
                VirtualBounds.Top,
                VirtualBounds.Width,
                VirtualBounds.Height);
            NotifyChanged();
        }

        public void SetTool(CaptureTool tool)
        {
            Tool = tool;
            NotifyChanged();
        }

        public void SetColor(string color)
        {
            CurrentColor = color;
            NotifyChanged();
        }

        public void SetThickness(double thickness)
        {
            Thickness = Math.Clamp(thickness, 1, 32);
            NotifyChanged();
        }

        public void PushUndo()
        {
            _undo.Push(Annotations.Select(item => item.Clone()).ToList());
            _redo.Clear();
        }

        public void AddAnnotation(Annotation annotation)
        {
            PushUndo();
            Annotations.Add(annotation);
            NotifyChanged();
        }

        public void Undo()
        {
            if (_undo.Count == 0)
            {
                return;
            }

            _redo.Push(Annotations.Select(item => item.Clone()).ToList());
            var snapshot = _undo.Pop();
            Annotations.Clear();
            Annotations.AddRange(snapshot);
            NotifyChanged();
        }

        public void Redo()
        {
            if (_redo.Count == 0)
            {
                return;
            }

            _undo.Push(Annotations.Select(item => item.Clone()).ToList());
            var snapshot = _redo.Pop();
            Annotations.Clear();
            Annotations.AddRange(snapshot);
            NotifyChanged();
        }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public Int32Rect GetSelectionPhysical()
        {
            if (!Selection.HasValue)
            {
                return new Int32Rect(0, 0, 1, 1);
            }

            var selection = Selection.Value;
            int x = (int)Math.Round(selection.X);
            int y = (int)Math.Round(selection.Y);
            int width = Math.Max(1, (int)Math.Round(selection.Width));
            int height = Math.Max(1, (int)Math.Round(selection.Height));
            x = Math.Clamp(x, VirtualBounds.Left, VirtualBounds.Right - 1);
            y = Math.Clamp(y, VirtualBounds.Top, VirtualBounds.Bottom - 1);
            width = Math.Min(width, VirtualBounds.Right - x);
            height = Math.Min(height, VirtualBounds.Bottom - y);
            return new Int32Rect(x, y, Math.Max(1, width), Math.Max(1, height));
        }

        public BitmapSource? RenderSelection()
        {
            if (VirtualBitmap == null || !Selection.HasValue)
            {
                return null;
            }

            var physical = GetSelectionPhysical();
            if (physical.Width < 1 || physical.Height < 1)
            {
                return null;
            }

            int relativeX = physical.X - VirtualBounds.Left;
            int relativeY = physical.Y - VirtualBounds.Top;
            var crop = new CroppedBitmap(
                VirtualBitmap,
                new Int32Rect(relativeX, relativeY, physical.Width, physical.Height));

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(crop, new Rect(0, 0, physical.Width, physical.Height));
                dc.PushTransform(new TranslateTransform(-physical.X, -physical.Y));
                AnnotationRenderer.Draw(dc, Annotations, VirtualBitmap);
                dc.Pop();
            }

            var target = new RenderTargetBitmap(
                physical.Width,
                physical.Height,
                96,
                96,
                PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();
            return target;
        }

        public void CopySelection()
        {
            var bitmap = RenderSelection();
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

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

            RecentCaptureService.Instance.Add(bitmap, null, LocalizationService.Get("Nav_Capture"));
            if (SettingsService.Instance.Current.CaptureSound)
            {
                NativeMethods.MessageBeep(0x40);
            }
            CloseAll();
            (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_Copied"));
        }

        public void SaveSelection(bool quick)
        {
            var bitmap = RenderSelection();
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            string extension = ImageIO.ExtensionForFormat(SettingsService.Instance.Current.ImageFormat);
            string path;
            try
            {
                path = quick || SettingsService.Instance.Current.QuickSave
                    ? ScreenCaptureService.QuickSave(bitmap, extension)
                    : ScreenCaptureService.SaveWithDialog(bitmap, null, extension);
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_SaveFailed"));
                _ = ex;
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (SettingsService.Instance.Current.AutoCopyCapture)
            {
                try
                {
                    Clipboard.SetImage(bitmap);
                }
                catch
                {
                }
            }

            RecentCaptureService.Instance.Add(bitmap, path, LocalizationService.Get("Recent_OpenFolder"));
            if (SettingsService.Instance.Current.CaptureSound)
            {
                NativeMethods.MessageBeep(0x40);
            }
            CloseAll();
            (Application.Current as App)?.ShowToast(LocalizationService.Format("Capture_Saved", path));
        }

        public void PinSelection()
        {
            var bitmap = RenderSelection();
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            CloseAll();
            var pin = new PinWindow(bitmap);
            pin.Show();
            pin.Activate();
        }

        public async void OcrSelection()
        {
            var bitmap = RenderSelection();
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            var app = Application.Current as App;
            CloseAll();
            app?.ShowToast(LocalizationService.Get("Capture_OcrRunning"));
            try
            {
                string text = await OcrService.RecognizeAsync(bitmap);
                if (string.IsNullOrWhiteSpace(text))
                {
                    app?.ShowToast(LocalizationService.Get("Capture_OcrEmpty"));
                    return;
                }

                var window = new OcrResultWindow(text);
                window.Show();
                window.Activate();
            }
            catch (Exception ex)
            {
                app?.ShowToast(LocalizationService.Format("Error_Body", ex.Message));
            }
        }

        public void QrSelection()
        {
            var bitmap = RenderSelection();
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            var app = Application.Current as App;
            CloseAll();
            try
            {
                var result = QrCodeService.Decode(bitmap);
                if (result == null || string.IsNullOrWhiteSpace(result.Text))
                {
                    app?.ShowToast(LocalizationService.Get("Qr_NotFound"));
                    return;
                }

                string? savedPath = null;
                try
                {
                    savedPath = ScreenCaptureService.QuickSave(bitmap, ".png");
                }
                catch
                {
                }

                RecentCaptureService.Instance.Add(bitmap, savedPath, LocalizationService.Get("Tools_QrTitle"));

                if (SettingsService.Instance.Current.CaptureSound)
                {
                    NativeMethods.MessageBeep(0x40);
                }

                var window = new QrResultWindow(result.Text, result.FormatName, bitmap);
                window.Show();
                window.Activate();
            }
            catch (Exception ex)
            {
                app?.ShowToast(LocalizationService.Format("Error_Body", ex.Message));
            }
        }

        public void PrintSelection()
        {
            var bitmap = RenderSelection();
            if (bitmap == null)
            {
                return;
            }

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                Width = dialog.PrintableAreaWidth,
                Height = dialog.PrintableAreaHeight
            };
            image.Measure(new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight));
            image.Arrange(new Rect(0, 0, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight));
            dialog.PrintVisual(image, "CryoUtils");
            CloseAll();
        }

        public void BeginRecording()
        {
            if (!Selection.HasValue)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            var physical = GetSelectionPhysical();
            CloseAll(restoreDashboard: false);
            var window = new RecordingBarWindow(physical);
            window.Show();
            window.Activate();
        }

        public void Close()
        {
            CloseAll();
        }

        public void CloseAll(bool restoreDashboard = true)
        {
            if (IsClosing)
            {
                return;
            }

            IsClosing = true;
            foreach (var overlay in _windows.ToList())
            {
                try
                {
                    overlay.Close();
                }
                catch
                {
                }
            }
            _windows.Clear();
            IsActive = false;
            _recordAfterSelection = false;
            ActiveOverlay = null;
            Selection = null;
            Annotations.Clear();
            _undo.Clear();
            _redo.Clear();
            VirtualBitmap = null;
            IsClosing = false;

            var app = Application.Current as App;
            if (restoreDashboard)
            {
                app?.FinishCapture();
            }
            NotifyChanged();
        }

        public void RemoveOverlay(CaptureOverlayWindow overlay)
        {
            _windows.Remove(overlay);
            if (ReferenceEquals(ActiveOverlay, overlay))
            {
                ActiveOverlay = _windows.FirstOrDefault();
            }

            if (!IsClosing && _windows.Count == 0 && IsActive)
            {
                IsActive = false;
                (Application.Current as App)?.FinishCapture();
            }
        }
    }
}
