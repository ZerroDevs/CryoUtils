using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private bool _oneShotExit;
        private bool? _sessionPrettyFrameOverride;
        private Annotation? _lastWheelStyledAnnotation;

        public NativeMethods.RECT VirtualBounds { get; private set; }
        public BitmapSource? VirtualBitmap { get; private set; }
        public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = Array.Empty<MonitorInfo>();
        public Rect? Selection { get; private set; }
        public List<Annotation> Annotations { get; } = new();
        public Annotation? SelectedAnnotation { get; private set; }
        public CaptureTool Tool { get; private set; } = CaptureTool.Select;
        public string CurrentColor { get; private set; } = AppSettings.DefaultAnnotationColor;
        public string SourceApp { get; private set; } = "";
        public string SourceWindow { get; private set; } = "";
        public double Thickness { get; private set; } = 3;
        public int NextStep { get; set; } = 1;
        public MonitorInfo? ActiveMonitor { get; set; }
        public CaptureOverlayWindow? ActiveOverlay { get; set; }
        public bool IsActive { get; private set; }
        public bool IsClosing { get; private set; }
        public bool ShowCrosshair { get; set; } = true;
        public bool UsePrettyFrame => _sessionPrettyFrameOverride ?? SettingsService.Instance.Current.PrettyFrameExport;
        public bool RecordAfterSelection
        {
            get => _recordAfterSelection;
            set => _recordAfterSelection = value;
        }

        public event Action? StateChanged;

        private CaptureController()
        {
        }

        public void TogglePrettyFrame()
        {
            _sessionPrettyFrameOverride = !UsePrettyFrame;
            NotifyChanged();
        }

        public void Start(bool oneShotExit = false)
        {
            _oneShotExit = oneShotExit;
            Start(recordMode: false, presetSelection: null);
        }

        public void StartLastRegion()
        {
            var s = SettingsService.Instance.Current;
            if (!s.LastCaptureX.HasValue || !s.LastCaptureY.HasValue ||
                !s.LastCaptureWidth.HasValue || !s.LastCaptureHeight.HasValue ||
                s.LastCaptureWidth.Value < 4 || s.LastCaptureHeight.Value < 4)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoLastRegion"));
                Start();
                return;
            }

            _oneShotExit = false;
            var preset = new Rect(
                s.LastCaptureX.Value,
                s.LastCaptureY.Value,
                s.LastCaptureWidth.Value,
                s.LastCaptureHeight.Value);
            Start(recordMode: false, presetSelection: preset);
        }

        public void StartForImage(BitmapSource image, string? originalPath = null)
        {
            if (IsActive || _starting)
            {
                return;
            }

            _oneShotExit = false;
            _recordAfterSelection = false;
            _sessionPrettyFrameOverride = null;
            _ = StartForImageAsync(image, originalPath);
        }

        public void StartRecordingMode()
        {
            _oneShotExit = false;
            Start(recordMode: true, presetSelection: null);
        }

        private void Start(bool recordMode, Rect? presetSelection)
        {
            if (IsActive || _starting)
            {
                return;
            }

            _recordAfterSelection = recordMode;
            _sessionPrettyFrameOverride = null;
            _ = StartAsync(presetSelection);
        }

        private async System.Threading.Tasks.Task StartForImageAsync(BitmapSource image, string? originalPath)
        {
            _starting = true;
            try
            {
                if (string.IsNullOrEmpty(SourceApp) && string.IsNullOrEmpty(SourceWindow))
                {
                    CaptureSourceInfo();
                }
                var app = Application.Current as App;
                App.EnsureUiResourcesLoaded();
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
                SelectedAnnotation = null;
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

        private async System.Threading.Tasks.Task StartAsync(Rect? presetSelection = null)
        {
            _starting = true;
            try
            {
                CaptureSourceInfo();
                var app = Application.Current as App;
                App.EnsureUiResourcesLoaded();
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
                SelectedAnnotation = null;
                _lastWheelStyledAnnotation = null;
                _undo.Clear();
                _redo.Clear();
                IsClosing = false;
                VirtualBounds = ScreenCaptureService.GetVirtualBounds();
                VirtualBitmap = ScreenCaptureService.CaptureRegion(VirtualBounds);
                Monitors = ScreenCaptureService.GetMonitors();

                if (presetSelection.HasValue)
                {
                    var virtualRect = new Rect(
                        VirtualBounds.Left,
                        VirtualBounds.Top,
                        VirtualBounds.Width,
                        VirtualBounds.Height);
                    var clampedPreset = Rect.Intersect(presetSelection.Value, virtualRect);
                    if (!clampedPreset.IsEmpty && clampedPreset.Width >= 4 && clampedPreset.Height >= 4)
                    {
                        Selection = clampedPreset;
                    }
                }

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
            if (tool != CaptureTool.Select)
            {
                SelectedAnnotation = null;
            }
            NotifyChanged();
        }

        public void ToggleTool(CaptureTool tool)
        {
            Tool = Tool == tool ? CaptureTool.Select : tool;
            if (Tool != CaptureTool.Select)
            {
                SelectedAnnotation = null;
            }
            NotifyChanged();
        }

        public void SetSelectedAnnotation(Annotation? annotation)
        {
            var next = annotation != null && Annotations.Contains(annotation) ? annotation : null;
            if (!ReferenceEquals(SelectedAnnotation, next))
            {
                SelectedAnnotation = next;
                _lastWheelStyledAnnotation = null;
                NotifyChanged();
            }
        }

        public bool UpdateSelectedAnnotationStyle(string? color = null, double? thickness = null, bool coalesceUndo = false)
        {
            var annotation = SelectedAnnotation;
            if (annotation == null || !Annotations.Contains(annotation))
            {
                return false;
            }

            if (!coalesceUndo || !ReferenceEquals(_lastWheelStyledAnnotation, annotation))
            {
                PushUndo();
                _lastWheelStyledAnnotation = coalesceUndo ? annotation : null;
            }

            if (!string.IsNullOrWhiteSpace(color))
            {
                annotation.Color = color;
            }

            if (thickness.HasValue)
            {
                double clamped = Math.Clamp(thickness.Value, 1, 32);
                annotation.Thickness = clamped;
                if (annotation.Kind == AnnotationKind.Text)
                {
                    annotation.FontSize = Math.Max(10, clamped * 6);
                }
                else if (annotation.Kind == AnnotationKind.Blur)
                {
                    annotation.PixelPreview = null;
                }
            }

            NotifyChanged();
            return true;
        }

        public void UpdateSelectedText(Annotation annotation, string newText, bool isRtl)
        {
            if (!Annotations.Contains(annotation))
            {
                return;
            }

            PushUndo();
            if (string.IsNullOrWhiteSpace(newText))
            {
                Annotations.Remove(annotation);
                if (ReferenceEquals(SelectedAnnotation, annotation))
                {
                    SelectedAnnotation = null;
                }
            }
            else
            {
                annotation.Text = newText.Trim();
                annotation.IsRtl = isRtl;
            }
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
            _lastWheelStyledAnnotation = null;
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

            _lastWheelStyledAnnotation = null;
            _redo.Push(Annotations.Select(item => item.Clone()).ToList());
            var snapshot = _undo.Pop();
            Annotations.Clear();
            Annotations.AddRange(snapshot);
            SelectedAnnotation = null;
            NotifyChanged();
        }

        public void Redo()
        {
            if (_redo.Count == 0)
            {
                return;
            }

            _lastWheelStyledAnnotation = null;
            _undo.Push(Annotations.Select(item => item.Clone()).ToList());
            var snapshot = _redo.Pop();
            Annotations.Clear();
            Annotations.AddRange(snapshot);
            SelectedAnnotation = null;
            NotifyChanged();
        }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void RemoveAnnotation(Annotation annotation)
        {
            if (!Annotations.Contains(annotation))
            {
                return;
            }

            PushUndo();
            Annotations.Remove(annotation);
            if (ReferenceEquals(SelectedAnnotation, annotation))
            {
                SelectedAnnotation = null;
            }
            NotifyChanged();
        }

        public void CropToSelection()
        {
            var bitmap = RenderSelection();
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            CloseAll(restoreDashboard: false);
            StartForImage(bitmap);
        }

        public void RotateSelection()
        {
            var bitmap = RenderSelection();
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            var rotated = new TransformedBitmap(bitmap, new RotateTransform(90));
            rotated.Freeze();
            CloseAll(restoreDashboard: false);
            StartForImage(rotated);
        }

        public void StartScrollingCapture()
        {
            if (!Selection.HasValue)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            var physical = GetSelectionPhysical();
            var region = new NativeMethods.RECT
            {
                Left = physical.X,
                Top = physical.Y,
                Right = physical.X + physical.Width,
                Bottom = physical.Y + physical.Height
            };
            string sourceApp = SourceApp;
            string sourceWindow = SourceWindow;
            CloseAll(restoreDashboard: false);
            var window = new ScrollingCaptureWindow(region, sourceApp, sourceWindow);
            window.Show();
            window.Activate();
        }

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

        public BitmapSource? RenderSelection(bool allowFrame = true)
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

            return allowFrame && UsePrettyFrame
                ? ApplyPrettyFrame(target)
                : target;
        }

        internal static BitmapSource ApplyPrettyFrame(BitmapSource source)
        {
            int w = Math.Max(1, source.PixelWidth);
            int h = Math.Max(1, source.PixelHeight);
            int minDim = Math.Min(w, h);
            int pad = Math.Clamp((int)Math.Round(minDim * 0.06), 20, 56);
            int radius = Math.Clamp((int)Math.Round(minDim * 0.02), 8, 18);
            int outW = w + (pad * 2);
            int outH = h + (pad * 2);

            var bgBrush = new LinearGradientBrush(
                Color.FromRgb(0x18, 0x22, 0x35),
                Color.FromRgb(0x0D, 0x13, 0x1F),
                new Point(0, 0),
                new Point(1, 1));
            bgBrush.Freeze();

            var outerStroke = new Pen(new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), 1);
            outerStroke.Freeze();
            var innerStroke = new Pen(new SolidColorBrush(Color.FromArgb(68, 255, 255, 255)), 1);
            innerStroke.Freeze();

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(bgBrush, outerStroke, new Rect(0.5, 0.5, outW - 1, outH - 1));

                // Soft multi-pass ambient drop shadow under the shot card (pure vector, zero GPU effect overhead).
                for (int i = 4; i >= 1; i--)
                {
                    byte alpha = (byte)(18 + (5 - i) * 7);
                    var shadowBrush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
                    double expand = i * 3.0;
                    double dy = i * 2.5;
                    var shadowRect = new Rect(pad - expand * 0.5, pad + dy, w + expand, h + expand * 0.8);
                    dc.DrawRoundedRectangle(shadowBrush, null, shadowRect, radius + 2, radius + 2);
                }

                var shotRect = new Rect(pad, pad, w, h);
                var clip = new RectangleGeometry(shotRect, radius, radius);
                clip.Freeze();
                dc.PushClip(clip);
                dc.DrawImage(source, shotRect);
                dc.Pop();
                dc.DrawRoundedRectangle(null, innerStroke, new Rect(pad + 0.5, pad + 0.5, w - 1, h - 1), radius, radius);
            }

            var framed = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
            framed.Render(visual);
            framed.Freeze();
            return framed;
        }

        private string? TryQuickSaveCapture(BitmapSource bitmap, string? overrideExtension = null)
        {
            try
            {
                string extension = overrideExtension
                    ?? ImageIO.ExtensionForFormat(SettingsService.Instance.Current.ImageFormat);
                string? path = ScreenCaptureService.QuickSave(bitmap, extension);
                if (!string.IsNullOrEmpty(path))
                {
                    CaptureLibraryService.Instance.Record(
                        path,
                        SourceApp,
                        SourceWindow,
                        bitmap.PixelWidth,
                        bitmap.PixelHeight);
                }
                return path;
            }
            catch
            {
                return null;
            }
        }

        private void CaptureSourceInfo()
        {
            try
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                SourceWindow = ScreenCaptureService.GetWindowTitle(foreground);
                NativeMethods.GetWindowThreadProcessId(foreground, out uint processId);
                if (processId == 0)
                {
                    SourceApp = "";
                    return;
                }

                using var process = Process.GetProcessById((int)processId);
                SourceApp = process.ProcessName;
            }
            catch
            {
                SourceApp = "";
                SourceWindow = "";
            }
        }

        public void UploadSelectionAsync()
        {
            if (!UploadConsentService.EnsureUploadAllowed(ActiveOverlay))
            {
                return;
            }

            var bitmap = RenderSelection();
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            string? savedPath = SettingsService.Instance.Current.ArchiveCopiedCaptures
                ? TryQuickSaveCapture(bitmap)
                : null;
            string? displayName = string.IsNullOrEmpty(savedPath) ? null : System.IO.Path.GetFileName(savedPath);

            RememberSelectionBounds();
            CloseAll(restoreDashboard: true, allowOneShotExit: false);
            UploadProgressWindow.StartCaptureUpload(
                bitmap,
                onSuccess: url => RecentCaptureService.Instance.Add(bitmap, savedPath, url),
                onFailure: _ =>
                {
                    try
                    {
                        Clipboard.SetImage(bitmap);
                    }
                    catch
                    {
                    }
                    RecentCaptureService.Instance.Add(bitmap, savedPath, LocalizationService.Get("Nav_Capture"));
                },
                fileName: displayName);
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

            string? savedPath = SettingsService.Instance.Current.ArchiveCopiedCaptures
                ? TryQuickSaveCapture(bitmap)
                : null;
            RecentCaptureService.Instance.Add(bitmap, savedPath, LocalizationService.Get("Nav_Capture"));
            if (SettingsService.Instance.Current.CaptureSound)
            {
                NativeMethods.MessageBeep(0x40);
            }
            RememberSelectionBounds();
            CloseAll(restoreDashboard: true, waitForOneShotToast: true);
            (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_Copied"));
            _ = WorkflowService.Instance.RunPostCaptureAsync(
                bitmap,
                SourceApp,
                SourceWindow,
                skipSaveAction: savedPath != null);
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

            CaptureLibraryService.Instance.Record(
                path,
                SourceApp,
                SourceWindow,
                bitmap.PixelWidth,
                bitmap.PixelHeight);
            RecentCaptureService.Instance.Add(bitmap, path, LocalizationService.Get("Recent_OpenFolder"));
            if (SettingsService.Instance.Current.CaptureSound)
            {
                NativeMethods.MessageBeep(0x40);
            }
            RememberSelectionBounds();
            CloseAll(restoreDashboard: true, waitForOneShotToast: true);
            (Application.Current as App)?.ShowToast(LocalizationService.Format("Capture_Saved", path));
            _ = WorkflowService.Instance.RunPostCaptureAsync(
                bitmap,
                SourceApp,
                SourceWindow,
                skipSaveAction: true);
        }

        public void PinSelection()
        {
            var bitmap = RenderSelection(allowFrame: false);
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            RememberSelectionBounds();
            CloseAll(restoreDashboard: false, allowOneShotExit: false);
            var pin = new PinWindow(bitmap);
            pin.Show();
            pin.Activate();
        }

        public async void OcrSelection(bool copyOnly = false)
        {
            var bitmap = RenderSelection(allowFrame: false);
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            var app = Application.Current as App;
            RememberSelectionBounds();
            CloseAll(restoreDashboard: !copyOnly ? false : true, allowOneShotExit: copyOnly, waitForOneShotToast: copyOnly);
            app?.ShowToast(LocalizationService.Get("Capture_OcrRunning"));
            try
            {
                string text = await OcrService.RecognizeAsync(bitmap);
                if (string.IsNullOrWhiteSpace(text))
                {
                    app?.ShowToast(LocalizationService.Get("Capture_OcrEmpty"));
                    return;
                }

                if (copyOnly)
                {
                    try
                    {
                        Clipboard.SetText(text);
                    }
                    catch
                    {
                        try
                        {
                            Clipboard.SetDataObject(text, true);
                        }
                        catch
                        {
                        }
                    }

                    if (SettingsService.Instance.Current.CaptureSound)
                    {
                        NativeMethods.MessageBeep(0x40);
                    }
                    app?.ShowToast(LocalizationService.Get("Capture_OcrCopied"));
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

        public void SnapForegroundWindow()
        {
            if (IsActive || _starting)
            {
                return;
            }

            var bitmap = ScreenCaptureService.CaptureForegroundWindow(out string appName, out string windowTitle);
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_SnapEmpty"));
                return;
            }

            var output = SettingsService.Instance.Current.PrettyFrameExport
                ? ApplyPrettyFrame(bitmap)
                : bitmap;

            try
            {
                Clipboard.SetImage(output);
            }
            catch
            {
                try
                {
                    var data = new DataObject();
                    data.SetImage(output);
                    Clipboard.SetDataObject(data, true);
                }
                catch
                {
                }
            }

            string? savedPath = (SettingsService.Instance.Current.QuickSave ||
                                 SettingsService.Instance.Current.ArchiveCopiedCaptures)
                ? TryQuickSaveCapture(output)
                : null;
            RecentCaptureService.Instance.Add(output, savedPath, LocalizationService.Get("Nav_Capture"));
            if (SettingsService.Instance.Current.CaptureSound)
            {
                NativeMethods.MessageBeep(0x40);
            }

            var app = Application.Current as App;
            app?.ShowToast(savedPath != null
                ? LocalizationService.Format("Capture_SnappedSaved", savedPath)
                : LocalizationService.Get("Capture_Snapped"));

            _ = WorkflowService.Instance.RunPostCaptureAsync(
                output,
                appName,
                windowTitle,
                skipSaveAction: savedPath != null);
            MemoryTrimService.ScheduleTrim(250);
        }

        public string? ExportDragFile()
        {
            var bitmap = RenderSelection();
            if (bitmap == null)
            {
                return null;
            }

            string? path = WriteTempDragPng(bitmap, SourceApp);
            if (!string.IsNullOrEmpty(path))
            {
                RecentCaptureService.Instance.Add(bitmap, null, LocalizationService.Get("Nav_Capture"));
            }
            return path;
        }

        internal static string? WriteTempDragPng(BitmapSource bitmap, string? prefix = null)
        {
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CryoUtils", "Drag");
                System.IO.Directory.CreateDirectory(dir);
                try
                {
                    DateTime cutoff = DateTime.UtcNow.AddHours(-12);
                    foreach (string stale in System.IO.Directory.GetFiles(dir, "*.png"))
                    {
                        if (System.IO.File.GetLastWriteTimeUtc(stale) < cutoff)
                        {
                            System.IO.File.Delete(stale);
                        }
                    }
                }
                catch
                {
                }

                string fileName = ScreenCaptureService.CreateTimestampedFileName(".png");
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    string safe = string.Concat(prefix.Take(20).Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')).Trim('_');
                    if (!string.IsNullOrEmpty(safe))
                    {
                        fileName = $"{safe}_{fileName}";
                    }
                }

                string path = System.IO.Path.Combine(dir, fileName);
                ImageIO.SaveBitmap(bitmap, path);
                return path;
            }
            catch
            {
                return null;
            }
        }

        public void SetOverlaysDragTransparent(bool transparent)
        {
            foreach (var overlay in _windows)
            {
                overlay.Opacity = transparent ? 0 : 1;
                overlay.IsHitTestVisible = !transparent;
                overlay.Topmost = !transparent;
            }

            if (!transparent)
            {
                ActiveOverlay?.Activate();
            }
        }

        public void QrSelection()
        {
            var bitmap = RenderSelection(allowFrame: false);
            if (bitmap == null)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_NoSelection"));
                return;
            }

            var app = Application.Current as App;
            RememberSelectionBounds();
            CloseAll(restoreDashboard: false, allowOneShotExit: false);
            try
            {
                var result = QrCodeService.Decode(bitmap);
                if (result == null || string.IsNullOrWhiteSpace(result.Text))
                {
                    app?.ShowToast(LocalizationService.Get("Qr_NotFound"));
                    return;
                }

                string? savedPath = TryQuickSaveCapture(bitmap, ".png");

                RecentCaptureService.Instance.Add(bitmap, savedPath, LocalizationService.Get("Tools_QrTitle"));

                if (SettingsService.Instance.Current.CaptureSound)
                {
                    NativeMethods.MessageBeep(0x40);
                }

                BitmapSource cleanQr = bitmap;
                try
                {
                    cleanQr = QrCodeService.Encode(result.Text, 512);
                }
                catch
                {
                }

                var window = new QrResultWindow(result.Text, result.FormatName, cleanQr);
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
            RememberSelectionBounds();
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
            RememberSelectionBounds();
            CloseAll(restoreDashboard: false, allowOneShotExit: false);
            var window = new RecordingBarWindow(physical);
            window.Show();
            window.Activate();
        }

        private void RememberSelectionBounds()
        {
            if (!Selection.HasValue || Selection.Value.Width < 4 || Selection.Value.Height < 4)
            {
                return;
            }

            var s = SettingsService.Instance.Current;
            s.LastCaptureX = Selection.Value.X;
            s.LastCaptureY = Selection.Value.Y;
            s.LastCaptureWidth = Selection.Value.Width;
            s.LastCaptureHeight = Selection.Value.Height;
            SettingsService.Instance.SaveWithoutNotification();
        }

        public void Close()
        {
            CloseAll();
        }

        public void CloseAll(bool restoreDashboard = true, bool allowOneShotExit = true, bool waitForOneShotToast = false)
        {
            if (IsClosing)
            {
                return;
            }

            IsClosing = true;
            bool shouldOneShotExit = allowOneShotExit && _oneShotExit;
            _oneShotExit = false;
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
            SelectedAnnotation = null;
            _lastWheelStyledAnnotation = null;
            _undo.Clear();
            _redo.Clear();
            VirtualBitmap = null;
            IsClosing = false;

            var app = Application.Current as App;
            if (shouldOneShotExit)
            {
                app?.CompleteOneShotCapture(waitForOneShotToast);
                return;
            }

            if (restoreDashboard)
            {
                app?.FinishCapture();
            }
            NotifyChanged();
            MemoryTrimService.ScheduleTrim(250);
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
                Selection = null;
                Annotations.Clear();
                SelectedAnnotation = null;
                _lastWheelStyledAnnotation = null;
                _undo.Clear();
                _redo.Clear();
                VirtualBitmap = null;
                (Application.Current as App)?.FinishCapture();
                MemoryTrimService.ScheduleTrim(250);
            }
        }
    }
}
