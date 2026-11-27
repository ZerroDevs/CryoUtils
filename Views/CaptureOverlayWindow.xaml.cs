using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CryoUtils.Models;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class CaptureOverlayWindow : Window
    {
        private sealed class ToolButtonRef
        {
            public Button Button { get; init; } = null!;
            public Path Icon { get; init; } = null!;
            public bool Filled { get; init; }
        }

        private readonly Dictionary<CaptureTool, ToolButtonRef> _toolButtons = new();
        private readonly DispatcherTimer _thicknessTimer;
        private Button? _undoButton;
        private Button? _redoButton;
        private Button? _colorButton;
        private Border? _colorSwatch;
        private TextBlock? _thicknessText;
        private Popup? _colorPopup;
        private bool _creating;
        private bool _moving;
        private bool _resizing;
        private bool _drawing;
        private int _handle = -1;
        private Point _startPhysical;
        private Rect _initialSelection;
        private Annotation? _activeAnnotation;
        private Annotation? _selectedAnnotation;
        private bool _movingAnnotation;
        private bool _resizingAnnotation;
        private int _annotationHandle = -1;
        private Point _annotationStart;
        private Rect _annotationStartRect;
        private List<Point> _annotationStartPoints = new();
        private Point _textAnchorPhysical;
        private Annotation? _editingTextAnnotation;
        private bool _textCommitting;
        private Rect _toolbarBounds = Rect.Empty;
        private Rect _actionToolbarBounds = Rect.Empty;
        private Button? _frameButton;
        private Button? _dragButton;
        private Point _dragStartPoint;
        private bool _dragArmed;
        private bool _disposingFromPool;
        private bool _builtScreenshotOnly;

        public CaptureController Controller { get; private set; }
        public MonitorInfo Monitor { get; private set; }
        private double Scale => Math.Max(0.1, Monitor.Scale);

        public CaptureOverlayWindow(CaptureController controller, MonitorInfo monitor)
        {
            Controller = controller;
            Monitor = monitor;
            InitializeComponent();
            _builtScreenshotOnly = SettingsService.Instance.Current.ScreenshotOnlyMode;

            Width = monitor.Bounds.Width / Scale;
            Height = monitor.Bounds.Height / Scale;
            Left = monitor.Bounds.Left / Scale;
            Top = monitor.Bounds.Top / Scale;
            ShowActivated = ReferenceEquals(monitor, controller.ActiveMonitor);

            Surface.Controller = controller;
            Surface.SetMonitor(monitor);
            Surface.Width = Width;
            Surface.Height = Height;
            Surface.Cursor = Cursors.Cross;

            Surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;
            Surface.MouseMove += Surface_MouseMove;
            Surface.MouseLeftButtonUp += Surface_MouseLeftButtonUp;
            Surface.MouseRightButtonDown += Surface_MouseRightButtonDown;
            Surface.MouseWheel += Surface_MouseWheel;
            Surface.MouseLeave += Surface_MouseLeave;

            Controller.StateChanged += OnStateChanged;
            Controller.AttachOverlay(this);

            _thicknessTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _thicknessTimer.Tick += (_, _) =>
            {
                _thicknessTimer.Stop();
                ThicknessPreview.Visibility = Visibility.Collapsed;
            };

            BuildToolbars();
            Loaded += (_, _) => UpdateChrome();
            UpdateChrome();
        }

        internal bool MatchesMonitor(MonitorInfo monitor)
        {
            return string.Equals(Monitor.DeviceName, monitor.DeviceName, StringComparison.Ordinal) &&
                   Monitor.Bounds.Left == monitor.Bounds.Left &&
                   Monitor.Bounds.Top == monitor.Bounds.Top &&
                   Monitor.Bounds.Width == monitor.Bounds.Width &&
                   Monitor.Bounds.Height == monitor.Bounds.Height &&
                   Math.Abs(Monitor.Scale - monitor.Scale) < 0.001 &&
                   _builtScreenshotOnly == SettingsService.Instance.Current.ScreenshotOnlyMode;
        }

        public void PrepareForReuse(CaptureController controller, MonitorInfo monitor)
        {
            Controller = controller;
            Monitor = monitor;
            _creating = false;
            _moving = false;
            _resizing = false;
            _drawing = false;
            _handle = -1;
            _activeAnnotation = null;
            _selectedAnnotation = null;
            _movingAnnotation = false;
            _resizingAnnotation = false;
            _annotationHandle = -1;
            _editingTextAnnotation = null;
            _textCommitting = false;
            _dragArmed = false;

            SizeBadge.Visibility = Visibility.Collapsed;
            CursorCoordsBadge.Visibility = Visibility.Collapsed;
            LoupePanel.Visibility = Visibility.Collapsed;
            ThicknessPreview.Visibility = Visibility.Collapsed;
            AnnotationToolbarBorder.Visibility = Visibility.Collapsed;
            ActionToolbarBorder.Visibility = Visibility.Collapsed;
            TextEditor.Visibility = Visibility.Collapsed;
            if (_colorPopup != null)
            {
                _colorPopup.IsOpen = false;
            }

            double s = Scale;
            Width = monitor.Bounds.Width / s;
            Height = monitor.Bounds.Height / s;
            Left = monitor.Bounds.Left / s;
            Top = monitor.Bounds.Top / s;
            ShowActivated = ReferenceEquals(monitor, controller.ActiveMonitor);
            SetDragTransparentNative(false);

            Surface.Controller = controller;
            Surface.IsActiveMonitor = ReferenceEquals(monitor, controller.ActiveMonitor);
            Surface.CursorPhysical = null;
            Surface.SetMonitor(monitor);
            Surface.Width = Width;
            Surface.Height = Height;
            Surface.Cursor = Cursors.Cross;

            Controller.StateChanged -= OnStateChanged;
            Controller.StateChanged += OnStateChanged;
            Controller.AttachOverlay(this);

            UpdateChrome();
            Surface.Refresh();
        }

        public void ParkInPool()
        {
            _thicknessTimer.Stop();
            _creating = false;
            _moving = false;
            _resizing = false;
            _drawing = false;
            _handle = -1;
            _activeAnnotation = null;
            _selectedAnnotation = null;
            _movingAnnotation = false;
            _resizingAnnotation = false;
            _annotationHandle = -1;
            _editingTextAnnotation = null;
            _textCommitting = false;
            _dragArmed = false;

            if (_colorPopup != null)
            {
                _colorPopup.IsOpen = false;
            }
            TextEditor.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            CursorCoordsBadge.Visibility = Visibility.Collapsed;
            LoupePanel.Visibility = Visibility.Collapsed;
            ThicknessPreview.Visibility = Visibility.Collapsed;
            AnnotationToolbarBorder.Visibility = Visibility.Collapsed;
            ActionToolbarBorder.Visibility = Visibility.Collapsed;

            Controller.StateChanged -= OnStateChanged;
            Surface.ReleaseBitmap();
            SetDragTransparentNative(false);
            Hide();
        }

        public void DisposeFromPool()
        {
            _disposingFromPool = true;
            try
            {
                Controller.StateChanged -= OnStateChanged;
                Close();
            }
            catch
            {
            }
        }

        public void SetDragTransparentNative(bool transparent)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
                if (transparent)
                {
                    ex |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT;
                    NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
                    NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LWA_ALPHA);
                }
                else
                {
                    if ((ex & (NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT)) != 0)
                    {
                        ex &= ~(long)(NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT);
                        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
                    }
                }
            }

            IsHitTestVisible = !transparent;
            Topmost = !transparent;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.DisableWindowTransitions(hwnd);
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                Monitor.Bounds.Left,
                Monitor.Bounds.Top,
                Monitor.Bounds.Width,
                Monitor.Bounds.Height,
                NativeMethods.SWP_NOACTIVATE);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (Controller.ActiveOverlay == null && !Controller.IsClosing)
            {
                Controller.ActiveOverlay = this;
                Controller.ActiveMonitor = Monitor;
                Controller.NotifyChanged();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            Controller.StateChanged -= OnStateChanged;
            Surface.ReleaseBitmap();
            if (!_disposingFromPool)
            {
                Controller.RemoveOverlay(this);
                base.OnClosed(e);
                if (!Controller.IsClosing)
                {
                    Controller.CloseAll();
                }
            }
            else
            {
                base.OnClosed(e);
            }
        }

        private void OnStateChanged()
        {
            Surface.Refresh();
            UpdateChrome();
        }

        // ============================ Toolbar ============================
        private void BuildToolbars()
        {
            AddToolButton(CaptureTool.Pen, CryoIcons.Pen, "Capture_ToolPen");
            AddToolButton(CaptureTool.Line, CryoIcons.Line, "Capture_ToolLine");
            AddToolButton(CaptureTool.Arrow, CryoIcons.Arrow, "Capture_ToolArrow");
            AddToolButton(CaptureTool.Rectangle, CryoIcons.Rectangle, "Capture_ToolRect");
            AddToolButton(CaptureTool.Ellipse, CryoIcons.Ellipse, "Capture_ToolEllipse");
            AddToolButton(CaptureTool.Spotlight, CryoIcons.Spotlight, "Capture_ToolSpotlight");
            AddToolButton(CaptureTool.Redaction, CryoIcons.Redaction, "Capture_ToolRedaction");
            AddToolButton(CaptureTool.Highlighter, CryoIcons.Highlighter, "Capture_ToolHighlighter");
            AddToolButton(CaptureTool.Text, CryoIcons.Text, "Capture_ToolText");
            AddToolButton(CaptureTool.Blur, CryoIcons.Blur, "Capture_ToolBlur");
            AddToolButton(CaptureTool.Zoom, CryoIcons.ZoomLoupe, "Capture_ToolZoom");
            AddStepToolButton();
            AddSeparator();

            _undoButton = CreateIconButton(CryoIcons.Undo, "Capture_ToolUndo", () => Controller.Undo(), false);
            _redoButton = CreateIconButton(CryoIcons.Redo, "Capture_ToolRedo", () => Controller.Redo(), false);
            AnnotationToolbar.Children.Add(_undoButton);
            AnnotationToolbar.Children.Add(_redoButton);
            AnnotationToolbar.Children.Add(CreateIconButton(CryoIcons.Crop, "Capture_ToolCrop", () => Controller.CropToSelection(), false));
            AnnotationToolbar.Children.Add(CreateIconButton(CryoIcons.Rotate, "Capture_ToolRotate", () => Controller.RotateSelection(), false));

            AddSeparator();

            _colorButton = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0),
                ToolTip = LocalizationService.Get("Capture_Color")
            };
            AutomationProperties.SetName(_colorButton, LocalizationService.Get("Capture_Color"));
            _colorSwatch = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(1),
                BorderBrush = ThemeService.GetBrush("BorderStrongBrush"),
                Background = new SolidColorBrush(AnnotationRenderer.ParseColor(Controller.CurrentColor))
            };
            _colorButton.Content = _colorSwatch;
            _colorButton.Click += (_, _) => ShowColorPopup();
            AnnotationToolbar.Children.Add(_colorButton);

            var quickSwatchGrid = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 2,
                Rows = 2,
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 2, 0, 2)
            };
            foreach (string hex in new[] { "#FF4F5E", "#2B88D8", "#F5B731", "#26A269" })
            {
                string swatchHex = hex;
                var dotBorder = new Border
                {
                    Width = 11,
                    Height = 11,
                    CornerRadius = new CornerRadius(2),
                    BorderThickness = new Thickness(1),
                    BorderBrush = ThemeService.GetBrush("BorderStrongBrush"),
                    Background = new SolidColorBrush(AnnotationRenderer.ParseColor(swatchHex))
                };
                var dotBtn = new Button
                {
                    Style = (Style)FindResource("IconButton"),
                    Width = 14,
                    Height = 14,
                    Padding = new Thickness(0),
                    Content = dotBorder,
                    ToolTip = LocalizationService.Get("Capture_Color") + $" ({swatchHex})"
                };
                dotBtn.Click += (_, _) =>
                {
                    Controller.SetColor(swatchHex);
                    Controller.UpdateSelectedAnnotationStyle(color: swatchHex);
                    SettingsService.Instance.Current.AnnotationColor = swatchHex;
                    SettingsService.Instance.SaveWithoutNotification();
                    _colorSwatch.Background = new SolidColorBrush(AnnotationRenderer.ParseColor(Controller.CurrentColor));
                };
                quickSwatchGrid.Children.Add(dotBtn);
            }
            AnnotationToolbar.Children.Add(quickSwatchGrid);

            _thicknessText = new TextBlock
            {
                Foreground = ThemeService.GetBrush("TextTertiaryBrush"),
                FontFamily = (FontFamily)FindResource("CryoMono"),
                FontSize = 9.5,
                Margin = new Thickness(0, 3, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight
            };
            AnnotationToolbar.Children.Add(_thicknessText);

            bool screenshotOnly = SettingsService.Instance.Current.ScreenshotOnlyMode;
            AddActionButton(CryoIcons.Copy, "Capture_ToolCopy", () => Controller.CopySelection());
            AddActionButton(CryoIcons.Save, "Capture_ToolSave", () => Controller.SaveSelection(quick: false));
            _frameButton = AddActionButton(CryoIcons.Frame, "Capture_ToolFrame", () => Controller.TogglePrettyFrame());
            _dragButton = AddDragActionButton();
            if (IsOverlayActionVisible("Upload", screenshotOnly))
            {
                AddActionButton(CryoIcons.CloudUpload, "Capture_ToolUpload", () => Controller.UploadSelectionAsync());
            }
            if (IsOverlayActionVisible("Record", screenshotOnly))
            {
                AddActionButton(CryoIcons.Record, "Capture_ToolRecord", () => Controller.BeginRecording());
            }
            AddActionButton(CryoIcons.Scroll, "Capture_ToolScroll", () => Controller.StartScrollingCapture());
            AddActionSeparator();
            AddActionButton(CryoIcons.Ocr, "Capture_ToolOcr", () => Controller.OcrSelection());
            if (IsOverlayActionVisible("OcrCopy", screenshotOnly))
            {
                AddActionButton(CryoIcons.OcrCopy, "Capture_ToolOcrCopy", () => Controller.OcrSelection(copyOnly: true));
            }
            AddActionButton(CryoIcons.RedactAuto, "Capture_ToolAutoRedact", () => _ = Controller.AutoRedactSelectionAsync());
            AddActionButton(CryoIcons.QrCode, "Capture_ToolQr", () => Controller.QrSelection());
            if (IsOverlayActionVisible("Pin", screenshotOnly))
            {
                AddActionButton(CryoIcons.Pin, "Capture_ToolPin", () => Controller.PinSelection());
            }
            AddActionButton(CryoIcons.Print, "Capture_ToolPrint", () => Controller.PrintSelection());
            AddActionSeparator();
            AddActionButton(CryoIcons.Close, "Capture_ToolClose", () => Controller.CloseAll());

            UpdateToolStyles();
        }

        private Button AddDragActionButton()
        {
            var button = CreateIconButton(CryoIcons.Drag, "Capture_ToolDrag", () => { });
            button.Margin = new Thickness(1, 0, 1, 0);
            button.Cursor = Cursors.SizeAll;
            button.PreviewMouseLeftButtonDown += DragButton_PreviewMouseLeftButtonDown;
            button.PreviewMouseMove += DragButton_PreviewMouseMove;
            button.PreviewMouseLeftButtonUp += (_, _) => _dragArmed = false;
            ActionToolbar.Children.Add(button);
            return button;
        }

        private void DragButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(this);
            _dragArmed = true;
        }

        private void DragButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed || _dragButton == null)
            {
                return;
            }

            Point current = e.GetPosition(this);
            if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _dragArmed = false;
            string? path = Controller.ExportDragFile();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Controller.SetOverlaysDragTransparent(true);
            try
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { path });
                var result = DragDrop.DoDragDrop(_dragButton, data, DragDropEffects.Copy);
                if (result != DragDropEffects.None)
                {
                    Controller.CloseAll(restoreDashboard: true, waitForOneShotToast: true);
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_Dragged"));
                    return;
                }
            }
            catch
            {
            }

            Controller.SetOverlaysDragTransparent(false);
        }

        internal static bool IsOverlayActionVisible(string actionName, bool screenshotOnly)
        {
            if (!screenshotOnly)
            {
                return true;
            }

            return !string.Equals(actionName, "Upload", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(actionName, "Record", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(actionName, "Pin", StringComparison.OrdinalIgnoreCase);
        }

        private void AddToolButton(
            CaptureTool tool,
            string iconData,
            string tooltipKey,
            bool filled = false)
        {
            var button = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 30,
                Height = 26,
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0),
                ToolTip = LocalizationService.Get(tooltipKey)
            };
            AutomationProperties.SetName(button, LocalizationService.Get(tooltipKey));
            var icon = CryoIcons.Create(iconData, 16, !filled);
            button.Content = new Grid
            {
                Width = 16,
                Height = 16,
                Children = { icon }
            };
            button.Click += (_, _) => Controller.ToggleTool(tool);
            _toolButtons[tool] = new ToolButtonRef { Button = button, Icon = icon, Filled = filled };
            AnnotationToolbar.Children.Add(button);
        }

        private void AddStepToolButton()
        {
            var button = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 30,
                Height = 26,
                Margin = new Thickness(0, 1, 0, 1),
                ToolTip = LocalizationService.Get("Capture_ToolStep")
            };
            AutomationProperties.SetName(button, LocalizationService.Get("Capture_ToolStep"));
            var circle = CryoIcons.Create(CryoIcons.Step, 16, true);
            var number = new TextBlock
            {
                Text = "1",
                Foreground = ThemeService.GetBrush("IconBrush"),
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var grid = new Grid { Width = 16, Height = 16 };
            grid.Children.Add(circle);
            grid.Children.Add(number);
            button.Content = grid;
            button.Click += (_, _) => Controller.ToggleTool(CaptureTool.Step);
            _toolButtons[CaptureTool.Step] = new ToolButtonRef { Button = button, Icon = circle, Filled = false };
            AnnotationToolbar.Children.Add(button);
        }

        private Button CreateIconButton(string iconData, string tooltipKey, Action click, bool filled = false)
        {
            var button = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 28,
                Height = 26,
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0),
                ToolTip = LocalizationService.Get(tooltipKey)
            };
            AutomationProperties.SetName(button, LocalizationService.Get(tooltipKey));
            var icon = CryoIcons.Create(iconData, 16, !filled);
            button.Content = icon;
            button.Click += (_, _) => click();
            return button;
        }

        private Button AddActionButton(string iconData, string tooltipKey, Action click)
        {
            var button = CreateIconButton(iconData, tooltipKey, click);
            button.Margin = new Thickness(1, 0, 1, 0);
            ActionToolbar.Children.Add(button);
            return button;
        }

        private void AddSeparator()
        {
            AnnotationToolbar.Children.Add(new Rectangle
            {
                Height = 1,
                Width = 22,
                Fill = ThemeService.GetBrush("DividerBrush"),
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        private void AddActionSeparator()
        {
            ActionToolbar.Children.Add(new Rectangle
            {
                Width = 1,
                Height = 20,
                Fill = ThemeService.GetBrush("DividerBrush"),
                Margin = new Thickness(3, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        private void UpdateToolStyles()
        {
            foreach (var entry in _toolButtons)
            {
                bool selected = Controller.Tool == entry.Key;
                entry.Value.Button.Background = selected
                    ? ThemeService.GetBrush("AccentSoftBrush")
                    : Brushes.Transparent;
                var brush = selected
                    ? ThemeService.GetBrush("AccentBrush")
                    : ThemeService.GetBrush("IconBrush");
                if (entry.Value.Filled)
                {
                    entry.Value.Icon.Fill = brush;
                }
                else
                {
                    entry.Value.Icon.Stroke = brush;
                }
                if (entry.Key == CaptureTool.Step && entry.Value.Button.Content is Grid grid &&
                    grid.Children.Count > 1 && grid.Children[1] is TextBlock number)
                {
                    number.Foreground = brush;
                }
            }

            if (_frameButton != null)
            {
                bool framed = Controller.UsePrettyFrame;
                _frameButton.Background = framed
                    ? ThemeService.GetBrush("AccentSoftBrush")
                    : Brushes.Transparent;
                if (_frameButton.Content is System.Windows.Shapes.Path framePath)
                {
                    framePath.Stroke = framed
                        ? ThemeService.GetBrush("AccentBrush")
                        : ThemeService.GetBrush("IconBrush");
                }
            }

            if (_undoButton != null)
            {
                _undoButton.IsEnabled = Controller.CanUndo;
            }
            if (_redoButton != null)
            {
                _redoButton.IsEnabled = Controller.CanRedo;
            }
            if (_colorSwatch != null)
            {
                _colorSwatch.Background = new SolidColorBrush(AnnotationRenderer.ParseColor(Controller.CurrentColor));
            }
            if (_thicknessText != null)
            {
                _thicknessText.Text = LocalizationService.Format(
                    "Capture_SizePreview",
                    Math.Round(Controller.Thickness, 1));
            }
        }

        // ============================ Chrome layout ============================
        private void UpdateChrome()
        {
            bool isActive = ReferenceEquals(Controller.ActiveOverlay, this);
            Surface.IsActiveMonitor = isActive;
            Surface.Refresh();

            UpdateToolStyles();

            if (!isActive || !Controller.Selection.HasValue || !IsVisible)
            {
                AnnotationToolbarBorder.Visibility = Visibility.Collapsed;
                ActionToolbarBorder.Visibility = Visibility.Collapsed;
                SizeBadge.Visibility = Visibility.Collapsed;
                HintBanner.Visibility = Visibility.Collapsed;
                _toolbarBounds = Rect.Empty;
                _actionToolbarBounds = Rect.Empty;
                if (!Controller.Selection.HasValue && isActive)
                {
                    ShowHint();
                }
                Controller.ShowCrosshair = !Controller.Selection.HasValue;
                return;
            }

            var selection = Controller.Selection.Value;
            var local = ToLocalRect(selection);
            double canvasWidth = Math.Max(1, ActualWidth > 0 ? ActualWidth : Width);
            double canvasHeight = Math.Max(1, ActualHeight > 0 ? ActualHeight : Height);
            var visible = Rect.Intersect(local, new Rect(0, 0, canvasWidth, canvasHeight));
            if (visible.IsEmpty || visible.Width < 1 || visible.Height < 1)
            {
                AnnotationToolbarBorder.Visibility = Visibility.Collapsed;
                ActionToolbarBorder.Visibility = Visibility.Collapsed;
                SizeBadge.Visibility = Visibility.Collapsed;
                HintBanner.Visibility = Visibility.Collapsed;
                _toolbarBounds = Rect.Empty;
                _actionToolbarBounds = Rect.Empty;
                return;
            }

            HintBanner.Visibility = Visibility.Collapsed;
            Controller.ShowCrosshair = false;

            SizeBadge.Visibility = Visibility.Visible;
            SizeText.Text = LocalizationService.Format(
                "Capture_SelectionSize",
                (int)Math.Round(selection.Width),
                (int)Math.Round(selection.Height));
            SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double badgeLeft = Math.Clamp(visible.Left, 8, Math.Max(8, canvasWidth - SizeBadge.DesiredSize.Width - 8));
            double badgeTop = visible.Top - SizeBadge.DesiredSize.Height - 6;
            if (badgeTop < 8)
            {
                badgeTop = Math.Min(canvasHeight - SizeBadge.DesiredSize.Height - 8, visible.Top + 8);
            }
            Canvas.SetLeft(SizeBadge, badgeLeft);
            Canvas.SetTop(SizeBadge, badgeTop);

            double margin = 8;

            AnnotationToolbarBorder.Visibility = Visibility.Visible;
            AnnotationToolbarBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var toolSize = AnnotationToolbarBorder.DesiredSize;
            if (toolSize.Width < 1 || toolSize.Height < 1)
            {
                toolSize = new Size(42, 460);
            }

            var rightTool = new Rect(visible.Right + margin, visible.Top, toolSize.Width, toolSize.Height);
            var leftTool = new Rect(visible.Left - toolSize.Width - margin, visible.Top, toolSize.Width, toolSize.Height);
            var toolCandidates = new List<Rect>();
            if (rightTool.Right <= canvasWidth - margin)
            {
                toolCandidates.Add(ClampChromeRect(rightTool, margin));
            }
            if (leftTool.Left >= margin)
            {
                toolCandidates.Add(ClampChromeRect(leftTool, margin));
            }
            if (toolCandidates.Count == 0)
            {
                toolCandidates.Add(ClampChromeRect(rightTool, margin));
            }

            ActionToolbarBorder.Visibility = Visibility.Visible;
            ActionToolbarBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var actionSize = ActionToolbarBorder.DesiredSize;
            if (actionSize.Width < 1 || actionSize.Height < 1)
            {
                actionSize = new Size(250, 40);
            }

            var actionBelow = new Rect(visible.Left, visible.Bottom + margin, actionSize.Width, actionSize.Height);
            var actionAbove = new Rect(visible.Left, visible.Top - actionSize.Height - margin, actionSize.Width, actionSize.Height);
            var actionBases = new List<Rect>();
            if (actionBelow.Bottom <= canvasHeight - margin)
            {
                actionBases.Add(ClampChromeRect(actionBelow, margin));
            }
            if (actionAbove.Top >= margin)
            {
                actionBases.Add(ClampChromeRect(actionAbove, margin));
            }
            if (actionBases.Count == 0)
            {
                actionBases.Add(ClampChromeRect(actionBelow, margin));
            }

            Rect selectedTool = toolCandidates[0];
            Rect selectedAction = actionBases[0];
            bool placed = false;
            foreach (var tool in toolCandidates)
            {
                foreach (var actionBase in actionBases)
                {
                    var candidates = new List<Rect>
                    {
                        actionBase,
                        ClampChromeRect(new Rect(tool.Right + margin, actionBase.Top, actionSize.Width, actionSize.Height), margin),
                        ClampChromeRect(new Rect(tool.Left - actionSize.Width - margin, actionBase.Top, actionSize.Width, actionSize.Height), margin),
                        ClampChromeRect(new Rect(tool.Left, tool.Bottom + margin, actionSize.Width, actionSize.Height), margin),
                        ClampChromeRect(new Rect(tool.Left, tool.Top - actionSize.Height - margin, actionSize.Width, actionSize.Height), margin)
                    };

                    foreach (var candidate in candidates)
                    {
                        if (!OverlapsWithGap(candidate, tool) && !OverlapsWithGap(candidate, visible))
                        {
                            selectedTool = tool;
                            selectedAction = candidate;
                            placed = true;
                            break;
                        }
                    }

                    if (placed)
                    {
                        break;
                    }
                }

                if (placed)
                {
                    break;
                }
            }

            if (!placed)
            {
                foreach (var tool in toolCandidates)
                {
                    foreach (var actionBase in actionBases)
                    {
                        var candidate = new Rect(tool.Right + margin, actionBase.Top, actionSize.Width, actionSize.Height);
                        candidate = ClampChromeRect(candidate, margin);
                        if (!OverlapsWithGap(candidate, tool))
                        {
                            selectedTool = tool;
                            selectedAction = candidate;
                            placed = true;
                            break;
                        }
                    }

                    if (placed)
                    {
                        break;
                    }
                }
            }

            Canvas.SetLeft(AnnotationToolbarBorder, selectedTool.Left);
            Canvas.SetTop(AnnotationToolbarBorder, selectedTool.Top);
            Canvas.SetLeft(ActionToolbarBorder, selectedAction.Left);
            Canvas.SetTop(ActionToolbarBorder, selectedAction.Top);
            _toolbarBounds = selectedTool;
            _actionToolbarBounds = selectedAction;
        }

        private Rect ClampChromeRect(Rect rect, double margin)
        {
            double width = Math.Max(1, ActualWidth);
            double height = Math.Max(1, ActualHeight);
            return new Rect(
                Math.Clamp(rect.Left, margin, Math.Max(margin, width - rect.Width - margin)),
                Math.Clamp(rect.Top, margin, Math.Max(margin, height - rect.Height - margin)),
                rect.Width,
                rect.Height);
        }

        private static bool OverlapsWithGap(Rect first, Rect second)
        {
            if (first.IsEmpty || second.IsEmpty)
            {
                return false;
            }

            return first.IntersectsWith(new Rect(
                second.Left - 2,
                second.Top - 2,
                second.Width + 4,
                second.Height + 4));
        }

        private void ShowHint()
        {
            HintBanner.Visibility = Visibility.Visible;
            HintBanner.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double width = HintBanner.DesiredSize.Width > 1 ? HintBanner.DesiredSize.Width : 360;
            Canvas.SetLeft(HintBanner, Math.Max(8, (ActualWidth - width) / 2));
            Canvas.SetTop(HintBanner, Math.Max(8, ActualHeight - 64));
        }

        // ============================ Input ============================
        private Point ToPhysical(Point localDip)
        {
            return new Point(
                Monitor.Bounds.Left + localDip.X * Scale,
                Monitor.Bounds.Top + localDip.Y * Scale);
        }

        private Point ToLocalDip(Point physical)
        {
            return new Point(
                (physical.X - Monitor.Bounds.Left) / Scale,
                (physical.Y - Monitor.Bounds.Top) / Scale);
        }

        private Rect ToLocalRect(Rect physical)
        {
            var topLeft = ToLocalDip(physical.TopLeft);
            return new Rect(topLeft.X, topLeft.Y, physical.Width / Scale, physical.Height / Scale);
        }

        private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            Focus();
            var global = ToPhysical(e.GetPosition(Surface));
            Controller.ActiveOverlay = this;
            Controller.ActiveMonitor = Monitor;

            if (e.ClickCount == 2 && Controller.Tool == CaptureTool.Select)
            {
                var hitText = Controller.Selection.HasValue && Controller.Selection.Value.Contains(global)
                    ? HitTestAnnotation(global)
                    : null;
                if (hitText != null && hitText.Kind == AnnotationKind.Text)
                {
                    SelectAnnotation(hitText);
                    ShowTextEditor(hitText.Rect.TopLeft, hitText);
                    e.Handled = true;
                    return;
                }

                SelectWindowUnderCursor(global);
                e.Handled = true;
                return;
            }

            if (TextEditor.Visibility == Visibility.Visible)
            {
                CommitText();
            }

            if (Controller.Tool == CaptureTool.Text)
            {
                if (Controller.Selection.HasValue && Controller.Selection.Value.Contains(global))
                {
                    ShowTextEditor(global);
                }
                else if (!Controller.Selection.HasValue)
                {
                    BeginSelection(global, creating: true);
                }
                e.Handled = true;
                return;
            }

            if (Controller.Tool == CaptureTool.Step)
            {
                if (Controller.Selection.HasValue && Controller.Selection.Value.Contains(global))
                {
                    var annotation = new Annotation
                    {
                        Kind = AnnotationKind.Step,
                        Color = Controller.CurrentColor,
                        Thickness = Controller.Thickness,
                        Number = Controller.NextStep,
                        Rect = new Rect(global.X, global.Y, 0, 0)
                    };
                    Controller.NextStep++;
                    Controller.AddAnnotation(annotation);
                }
                else if (!Controller.Selection.HasValue)
                {
                    BeginSelection(global, creating: true);
                }
                e.Handled = true;
                return;
            }

            if (Controller.Tool != CaptureTool.Select)
            {
                if (Controller.Selection.HasValue && Controller.Selection.Value.Contains(global))
                {
                    SelectAnnotation(null);
                    BeginAnnotation(global);
                }
                else if (!Controller.Selection.HasValue)
                {
                    BeginSelection(global, creating: true);
                }
                e.Handled = true;
                return;
            }

            var selection = Controller.Selection;
            if (selection.HasValue &&
                Controller.SelectedAnnotation != null &&
                Controller.Annotations.Contains(Controller.SelectedAnnotation))
            {
                int annotationHandle = HitTestAnnotationHandle(Controller.SelectedAnnotation, global);
                if (annotationHandle >= 0)
                {
                    _selectedAnnotation = Controller.SelectedAnnotation;
                    _resizingAnnotation = true;
                    _annotationHandle = annotationHandle;
                    _annotationStart = global;
                    _annotationStartRect = _selectedAnnotation.Rect;
                    _annotationStartPoints = new List<Point>(_selectedAnnotation.Points);
                    Controller.PushUndo();
                    Surface.CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }

            var hitAnnotation = HitTestAnnotation(global);
            if (selection.HasValue && selection.Value.Contains(global) && hitAnnotation != null)
            {
                SelectAnnotation(hitAnnotation);
                _movingAnnotation = true;
                _annotationStart = global;
                _annotationStartRect = hitAnnotation.Rect;
                _annotationStartPoints = new List<Point>(hitAnnotation.Points);
                Controller.PushUndo();
                Surface.CaptureMouse();
                e.Handled = true;
                return;
            }

            SelectAnnotation(null);
            _handle = HitTestHandle(global);
            if (selection.HasValue && _handle >= 0)
            {
                _resizing = true;
                _initialSelection = selection.Value;
                _startPhysical = global;
                Surface.CaptureMouse();
            }
            else if (selection.HasValue && selection.Value.Contains(global))
            {
                _moving = true;
                _initialSelection = selection.Value;
                _startPhysical = global;
                Surface.CaptureMouse();
            }
            else
            {
                BeginSelection(global, creating: true);
            }

            e.Handled = true;
        }

        private void BeginSelection(Point global, bool creating)
        {
            SelectAnnotation(null);
            _creating = creating;
            _startPhysical = global;
            _initialSelection = Rect.Empty;
            Controller.SetSelection(new Rect(global.X, global.Y, 0, 0));
            Surface.CaptureMouse();
        }

        private void BeginAnnotation(Point global)
        {
            var kind = Controller.Tool switch
            {
                CaptureTool.Pen => AnnotationKind.Pen,
                CaptureTool.Line => AnnotationKind.Line,
                CaptureTool.Arrow => AnnotationKind.Arrow,
                CaptureTool.Rectangle => AnnotationKind.Rectangle,
                CaptureTool.Ellipse => AnnotationKind.Ellipse,
                CaptureTool.Spotlight => AnnotationKind.Spotlight,
                CaptureTool.Redaction => AnnotationKind.Redaction,
                CaptureTool.Highlighter => AnnotationKind.Highlighter,
                CaptureTool.Blur => AnnotationKind.Blur,
                CaptureTool.Zoom => AnnotationKind.Zoom,
                _ => AnnotationKind.Pen
            };

            var annotation = new Annotation
            {
                Kind = kind,
                Color = kind == AnnotationKind.Redaction ? "#000000" : Controller.CurrentColor,
                Thickness = kind == AnnotationKind.Highlighter
                    ? Math.Max(8, Controller.Thickness * 4)
                    : Controller.Thickness,
                Rect = new Rect(global.X, global.Y, 0, 0)
            };

            if (kind is AnnotationKind.Pen or AnnotationKind.Highlighter or AnnotationKind.Redaction)
            {
                annotation.Points.Add(global);
                if (kind == AnnotationKind.Redaction)
                {
                    annotation.Thickness = Math.Max(10, Controller.Thickness * 3);
                }
            }
            else if (kind is AnnotationKind.Line or AnnotationKind.Arrow)
            {
                annotation.Points.Add(global);
                annotation.Points.Add(global);
            }

            _activeAnnotation = annotation;
            _drawing = true;
            _startPhysical = global;
            Controller.AddAnnotation(annotation);
            Surface.CaptureMouse();
        }

        private void Surface_MouseMove(object sender, MouseEventArgs e)
        {
            var local = e.GetPosition(Surface);
            var global = ToPhysical(local);
            Surface.CursorPhysical = global;
            UpdateCursor(global);

            if (!_creating && !Controller.Selection.HasValue && SettingsService.Instance.Current.HoverWindowHighlight)
            {
                Controller.SetHoveredWindow(Controller.HitTestWindowAt(global));
            }
            else if (Controller.Selection.HasValue && Controller.HoveredWindow != null)
            {
                Controller.SetHoveredWindow(null);
            }
            Surface.Refresh();

            bool overToolbar = AnnotationToolbarBorder.IsMouseOver || ActionToolbarBorder.IsMouseOver;
            UpdateCursorCoordsBadge(global, local, overToolbar);
            bool drawingFreehand = _drawing &&
                _activeAnnotation?.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter or AnnotationKind.Redaction;
            if (SettingsService.Instance.Current.ShowCaptureLoupe &&
                !overToolbar &&
                !drawingFreehand &&
                (!Controller.Selection.HasValue || _creating || _resizing || Controller.Tool != CaptureTool.Text))
            {
                UpdateLoupe(global, local);
            }
            else
            {
                LoupePanel.Visibility = Visibility.Collapsed;
            }

            if (_creating)
            {
                var rect = NormalizeRect(_startPhysical, global);
                rect = ClampRect(rect);
                Controller.SetSelection(rect);
            }
            else if (_moving)
            {
                double dx = global.X - _startPhysical.X;
                double dy = global.Y - _startPhysical.Y;
                var moved = _initialSelection;
                double minDx = Controller.VirtualBounds.Left - moved.Left;
                double maxDx = Controller.VirtualBounds.Right - moved.Right;
                double minDy = Controller.VirtualBounds.Top - moved.Top;
                double maxDy = Controller.VirtualBounds.Bottom - moved.Bottom;
                moved.Offset(Math.Clamp(dx, minDx, maxDx), Math.Clamp(dy, minDy, maxDy));
                Controller.SetSelection(moved);
            }
            else if (_resizingAnnotation && _selectedAnnotation != null)
            {
                ResizeActiveAnnotation(global);
            }
            else if (_movingAnnotation && _selectedAnnotation != null)
            {
                double dx = global.X - _annotationStart.X;
                double dy = global.Y - _annotationStart.Y;
                if (_selectedAnnotation.Points.Count > 0)
                {
                    _selectedAnnotation.Points.Clear();
                    _selectedAnnotation.Points.AddRange(
                        _annotationStartPoints.Select(point => new Point(point.X + dx, point.Y + dy)));
                }
                if (_selectedAnnotation.Kind != AnnotationKind.Pen &&
                    _selectedAnnotation.Kind != AnnotationKind.Highlighter &&
                    _selectedAnnotation.Kind != AnnotationKind.Line &&
                    _selectedAnnotation.Kind != AnnotationKind.Arrow)
                {
                    var movedRect = _annotationStartRect;
                    movedRect.Offset(dx, dy);
                    _selectedAnnotation.Rect = movedRect;
                }
                Controller.NotifyChanged();
            }
            else if (_resizing)
            {
                Controller.SetSelection(ResizeSelection(_initialSelection, global));
            }
            else if (_drawing && _activeAnnotation != null)
            {
                UpdateActiveAnnotation(global);
            }
        }

        private void UpdateActiveAnnotation(Point global)
        {
            var annotation = _activeAnnotation;
            if (annotation == null)
            {
                return;
            }

            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            switch (annotation.Kind)
            {
                case AnnotationKind.Pen:
                case AnnotationKind.Highlighter:
                case AnnotationKind.Redaction:
                    if (annotation.Points.Count == 0 ||
                        (annotation.Points[^1] - global).Length > 1.4)
                    {
                        annotation.Points.Add(global);
                    }
                    break;
                case AnnotationKind.Line:
                case AnnotationKind.Arrow:
                    var end = shift ? SnapAngle(_startPhysical, global) : global;
                    if (annotation.Points.Count < 2)
                    {
                        annotation.Points.Add(_startPhysical);
                        annotation.Points.Add(end);
                    }
                    else
                    {
                        annotation.Points[1] = end;
                    }
                    break;
                case AnnotationKind.Rectangle:
                case AnnotationKind.Ellipse:
                case AnnotationKind.Spotlight:
                case AnnotationKind.Blur:
                case AnnotationKind.Zoom:
                    var rect = NormalizeRect(_startPhysical, global);
                    if (annotation.Kind == AnnotationKind.Zoom)
                    {
                        double side = Math.Min(Math.Abs(rect.Width), Math.Abs(rect.Height));
                        double left = global.X < _startPhysical.X ? _startPhysical.X - side : _startPhysical.X;
                        double top = global.Y < _startPhysical.Y ? _startPhysical.Y - side : _startPhysical.Y;
                        rect = new Rect(left, top, side, side);
                    }
                    else if (shift && annotation.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse)
                    {
                        double side = Math.Min(Math.Abs(rect.Width), Math.Abs(rect.Height));
                        rect = new Rect(
                            rect.X,
                            rect.Y,
                            Math.Sign(rect.Width) * side,
                            Math.Sign(rect.Height) * side);
                    }
                    annotation.Rect = rect;
                    break;
            }

            Controller.NotifyChanged();
        }

        private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_creating && !_moving && !_movingAnnotation && !_resizingAnnotation && !_resizing && !_drawing)
            {
                return;
            }

            bool autoRecord = false;
            bool instantSnapOrCopy = false;
            var profile = CaptureController.GetCurrentProfile();
            bool instantCopyEnabled = profile.InstantCopy ?? SettingsService.Instance.Current.InstantCaptureCopy;
            bool keepToolbarHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
                                   Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (_creating)
            {
                var selection = Controller.Selection;
                if (selection.HasValue && (selection.Value.Width < 4 || selection.Value.Height < 4))
                {
                    if (SettingsService.Instance.Current.HoverWindowHighlight &&
                        Controller.HoveredWindow != null)
                    {
                        Controller.SetSelection(Controller.HoveredWindow.Bounds);
                        Controller.SetHoveredWindow(null);
                        instantSnapOrCopy = instantCopyEnabled && !keepToolbarHeld;
                    }
                    else
                    {
                        Controller.SetSelection(null);
                    }
                }
                else if (selection.HasValue && Controller.RecordAfterSelection)
                {
                    autoRecord = true;
                }
                else if (selection.HasValue && instantCopyEnabled && !keepToolbarHeld)
                {
                    instantSnapOrCopy = true;
                }
            }

            if (_drawing && _activeAnnotation != null)
            {
                if (_activeAnnotation.Kind is AnnotationKind.Redaction &&
                    _activeAnnotation.Points.Count < 2)
                {
                    Controller.Annotations.Remove(_activeAnnotation);
                    Controller.NotifyChanged();
                }
                else if (_activeAnnotation.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse or
                    AnnotationKind.Spotlight or AnnotationKind.Blur or AnnotationKind.Zoom &&
                    (_activeAnnotation.Rect.Width < 2 || _activeAnnotation.Rect.Height < 2))
                {
                    Controller.Annotations.Remove(_activeAnnotation);
                    Controller.NotifyChanged();
                }
            }

            _creating = false;
            _moving = false;
            _movingAnnotation = false;
            _resizingAnnotation = false;
            _annotationHandle = -1;
            _resizing = false;
            _drawing = false;
            _activeAnnotation = null;
            _handle = -1;
            if (Surface.IsMouseCaptured)
            {
                Surface.ReleaseMouseCapture();
            }

            LoupePanel.Visibility = Visibility.Collapsed;
            CursorCoordsBadge.Visibility = Visibility.Collapsed;
            e.Handled = true;

            if (autoRecord)
            {
                Controller.RecordAfterSelection = false;
                Controller.BeginRecording();
            }
            else if (instantSnapOrCopy && Controller.Selection.HasValue)
            {
                if (profile.AutoOcrCopy)
                {
                    Controller.OcrSelection(copyOnly: true);
                }
                else
                {
                    Controller.CopySelection();
                }
            }
        }

        private void Surface_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            StepBackOrClose();
            e.Handled = true;
        }

        private void Surface_MouseLeave(object sender, MouseEventArgs e)
        {
            LoupePanel.Visibility = Visibility.Collapsed;
            CursorCoordsBadge.Visibility = Visibility.Collapsed;
            Surface.CursorPhysical = null;
            Surface.Refresh();
        }

        private void UpdateCursorCoordsBadge(Point global, Point local, bool overToolbar)
        {
            bool showCursorCoords = CaptureController.GetCurrentProfile().CursorCoords ??
                                    SettingsService.Instance.Current.ShowCursorCoordinates;
            if (!showCursorCoords || overToolbar ||
                (Controller.Selection.HasValue && !_creating && !_resizing))
            {
                CursorCoordsBadge.Visibility = Visibility.Collapsed;
                return;
            }

            int x = (int)Math.Round(global.X);
            int y = (int)Math.Round(global.Y);
            if (!Controller.Selection.HasValue && Controller.HoveredWindow != null)
            {
                var wb = Controller.HoveredWindow.Bounds;
                string title = string.IsNullOrWhiteSpace(Controller.HoveredWindow.Title)
                    ? ""
                    : (Controller.HoveredWindow.Title.Length > 26
                        ? Controller.HoveredWindow.Title[..26] + "…"
                        : Controller.HoveredWindow.Title);
                CursorCoordsText.Text = string.IsNullOrEmpty(title)
                    ? $"{x}, {y}  ·  {(int)Math.Round(wb.Width)} × {(int)Math.Round(wb.Height)}"
                    : $"{title}  ·  {(int)Math.Round(wb.Width)} × {(int)Math.Round(wb.Height)}";
            }
            else
            {
                CursorCoordsText.Text = $"{x}, {y}";
            }

            CursorCoordsBadge.Visibility = Visibility.Visible;
            CursorCoordsBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var badgeSize = CursorCoordsBadge.DesiredSize;
            double offsetY = SettingsService.Instance.Current.ShowCaptureLoupe ? -badgeSize.Height - 12 : 18;
            double bx = local.X + 16;
            double by = local.Y + offsetY;
            if (bx + badgeSize.Width + 8 > ActualWidth)
            {
                bx = Math.Max(8, local.X - badgeSize.Width - 14);
            }
            if (by < 8)
            {
                by = Math.Min(ActualHeight - badgeSize.Height - 8, local.Y + 18);
            }
            if (by + badgeSize.Height + 8 > ActualHeight)
            {
                by = Math.Max(8, local.Y - badgeSize.Height - 12);
            }
            Canvas.SetLeft(CursorCoordsBadge, bx);
            Canvas.SetTop(CursorCoordsBadge, by);
        }

        private Annotation? HitTestAnnotation(Point point)
        {
            for (int index = Controller.Annotations.Count - 1; index >= 0; index--)
            {
                var annotation = Controller.Annotations[index];
                switch (annotation.Kind)
                {
                    case AnnotationKind.Step:
                        double radius = Math.Max(12, annotation.Thickness * 5);
                        if ((point - annotation.Rect.TopLeft).Length <= radius + 3)
                        {
                            return annotation;
                        }
                        break;
                    case AnnotationKind.Line:
                    case AnnotationKind.Arrow:
                        if (annotation.Points.Count >= 2 &&
                            DistanceToSegment(point, annotation.Points[0], annotation.Points[1]) <=
                            Math.Max(6, annotation.Thickness + 4))
                        {
                            return annotation;
                        }
                        break;
                    case AnnotationKind.Pen:
                    case AnnotationKind.Highlighter:
                    case AnnotationKind.Redaction:
                        if (annotation.Points.Any(item =>
                                (item - point).Length <= Math.Max(7, annotation.Thickness + 5)))
                        {
                            return annotation;
                        }
                        break;
                    case AnnotationKind.Text:
                        var textRect = AnnotationRenderer.GetAnnotationBounds(annotation);
                        textRect.Inflate(4, 4);
                        if (textRect.Width > 0 && textRect.Height > 0 && textRect.Contains(point))
                        {
                            return annotation;
                        }
                        break;
                    default:
                        var shapeRect = annotation.Rect;
                        shapeRect.Inflate(4, 4);
                        if (annotation.Rect.Width > 0 && shapeRect.Contains(point))
                        {
                            return annotation;
                        }
                        break;
                }
            }

            return null;
        }

        private void SelectAnnotation(Annotation? annotation)
        {
            _selectedAnnotation = annotation;
            Controller.SetSelectedAnnotation(annotation);
        }

        private void StepBackOrClose()
        {
            if (TextEditor.Visibility == Visibility.Visible)
            {
                CancelText();
                return;
            }

            if (_drawing && _activeAnnotation != null)
            {
                Controller.Annotations.Remove(_activeAnnotation);
                _drawing = false;
                _activeAnnotation = null;
                if (Surface.IsMouseCaptured)
                {
                    Surface.ReleaseMouseCapture();
                }
                LoupePanel.Visibility = Visibility.Collapsed;
                Controller.NotifyChanged();
                return;
            }

            if (Controller.Tool != CaptureTool.Select)
            {
                Controller.SetTool(CaptureTool.Select);
                return;
            }

            if (Controller.SelectedAnnotation != null)
            {
                SelectAnnotation(null);
                return;
            }

            Controller.CloseAll();
        }

        private int HitTestAnnotationHandle(Annotation annotation, Point global)
        {
            var local = ToLocalDip(global);
            if (annotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow &&
                annotation.Points.Count >= 2)
            {
                var startDip = ToLocalDip(annotation.Points[0]);
                var endDip = ToLocalDip(annotation.Points[^1]);
                if ((local - startDip).Length <= 9)
                {
                    return 0;
                }
                if ((local - endDip).Length <= 9)
                {
                    return 1;
                }
                return -1;
            }

            if (annotation.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse or
                AnnotationKind.Spotlight or AnnotationKind.Blur or AnnotationKind.Zoom &&
                annotation.Rect.Width > 0 && annotation.Rect.Height > 0)
            {
                return HitTestRectHandles(ToLocalRect(annotation.Rect), local);
            }

            return -1;
        }

        private void ResizeActiveAnnotation(Point global)
        {
            var annotation = _selectedAnnotation;
            if (annotation == null)
            {
                return;
            }

            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (annotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow &&
                annotation.Points.Count >= 2)
            {
                if (_annotationHandle == 0)
                {
                    annotation.Points[0] = shift ? SnapAngle(annotation.Points[^1], global) : global;
                }
                else if (_annotationHandle == 1)
                {
                    annotation.Points[^1] = shift ? SnapAngle(annotation.Points[0], global) : global;
                }
                Controller.NotifyChanged();
                return;
            }

            var bounds = new Rect(
                Controller.VirtualBounds.Left,
                Controller.VirtualBounds.Top,
                Controller.VirtualBounds.Width,
                Controller.VirtualBounds.Height);
            double dx = global.X - _annotationStart.X;
            double dy = global.Y - _annotationStart.Y;
            annotation.Rect = ResizeRectByHandle(_annotationStartRect, _annotationHandle, dx, dy, bounds);
            if (annotation.Kind == AnnotationKind.Blur)
            {
                annotation.PixelPreview = null;
            }
            Controller.NotifyChanged();
        }

        private static double DistanceToSegment(Point point, Point start, Point end)
        {
            var segment = end - start;
            double lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
            if (lengthSquared <= 0.001)
            {
                return (point - start).Length;
            }

            var offset = point - start;
            double t = Math.Clamp((offset.X * segment.X + offset.Y * segment.Y) / lengthSquared, 0, 1);
            var projection = new Point(start.X + segment.X * t, start.Y + segment.Y * t);
            return (point - projection).Length;
        }

        private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.5 : 1;
            double delta = e.Delta > 0 ? step : -step;
            if (Controller.Tool == CaptureTool.Select &&
                Controller.SelectedAnnotation != null &&
                Controller.Annotations.Contains(Controller.SelectedAnnotation))
            {
                double nextThickness = Math.Clamp(Controller.SelectedAnnotation.Thickness + delta, 1, 24);
                Controller.UpdateSelectedAnnotationStyle(thickness: nextThickness, coalesceUndo: true);
                ShowThicknessPreview(e.GetPosition(Surface), nextThickness);
                e.Handled = true;
                return;
            }

            Controller.SetThickness(Controller.Thickness + delta);
            ShowThicknessPreview(e.GetPosition(Surface), Controller.Thickness);
            e.Handled = true;
        }

        private void ShowThicknessPreview(Point local, double thickness)
        {
            ThicknessText.Text = LocalizationService.Format(
                "Capture_SizePreview",
                Math.Round(thickness, 1));
            double dot = Math.Clamp(thickness * 1.2, 3, 20);
            ThicknessDot.Width = dot;
            ThicknessDot.Height = dot;
            ThicknessPreview.Visibility = Visibility.Visible;
            ThicknessPreview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = ThicknessPreview.DesiredSize;
            double left = Math.Clamp(local.X + 18, 8, Math.Max(8, ActualWidth - size.Width - 8));
            double top = Math.Clamp(local.Y + 18, 8, Math.Max(8, ActualHeight - size.Height - 8));
            Canvas.SetLeft(ThicknessPreview, left);
            Canvas.SetTop(ThicknessPreview, top);
            _thicknessTimer.Stop();
            _thicknessTimer.Start();
        }

        private void UpdateCursor(Point global)
        {
            if (Controller.Tool == CaptureTool.Text)
            {
                Surface.Cursor = Cursors.IBeam;
                return;
            }
            if (Controller.Tool == CaptureTool.Step)
            {
                Surface.Cursor = Cursors.Hand;
                return;
            }
            if (Controller.Tool != CaptureTool.Select)
            {
                Surface.Cursor = Cursors.Cross;
                return;
            }

            if (Controller.Selection.HasValue &&
                Controller.SelectedAnnotation != null &&
                Controller.Annotations.Contains(Controller.SelectedAnnotation))
            {
                int annotationHandle = HitTestAnnotationHandle(Controller.SelectedAnnotation, global);
                if (annotationHandle >= 0)
                {
                    if (Controller.SelectedAnnotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow)
                    {
                        Surface.Cursor = Cursors.Hand;
                        return;
                    }
                    Surface.Cursor = annotationHandle switch
                    {
                        0 or 4 => Cursors.SizeNWSE,
                        2 or 6 => Cursors.SizeNESW,
                        1 or 5 => Cursors.SizeNS,
                        3 or 7 => Cursors.SizeWE,
                        _ => Cursors.SizeAll
                    };
                    return;
                }
            }

            if (Controller.Selection.HasValue &&
                Controller.Selection.Value.Contains(global) &&
                HitTestAnnotation(global) != null)
            {
                Surface.Cursor = Cursors.SizeAll;
                return;
            }

            int handle = HitTestHandle(global);
            Surface.Cursor = handle switch
            {
                0 or 4 => Cursors.SizeNWSE,
                2 or 6 => Cursors.SizeNESW,
                1 or 5 => Cursors.SizeNS,
                3 or 7 => Cursors.SizeWE,
                _ => Controller.Selection.HasValue && Controller.Selection.Value.Contains(global)
                    ? Cursors.SizeAll
                    : Cursors.Cross
            };
        }

        private int HitTestHandle(Point global)
        {
            if (!Controller.Selection.HasValue)
            {
                return -1;
            }

            return HitTestRectHandles(ToLocalRect(Controller.Selection.Value), ToLocalDip(global));
        }

        private static int HitTestRectHandles(Rect rect, Point local)
        {
            var handles = new[]
            {
                new Point(rect.Left, rect.Top),
                new Point(rect.Left + rect.Width / 2, rect.Top),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Top + rect.Height / 2),
                new Point(rect.Right, rect.Bottom),
                new Point(rect.Left + rect.Width / 2, rect.Bottom),
                new Point(rect.Left, rect.Bottom),
                new Point(rect.Left, rect.Top + rect.Height / 2)
            };

            for (int i = 0; i < handles.Length; i++)
            {
                if (Math.Abs(local.X - handles[i].X) <= 8 &&
                    Math.Abs(local.Y - handles[i].Y) <= 8)
                {
                    return i;
                }
            }

            return -1;
        }

        private Rect ResizeSelection(Rect initial, Point global)
        {
            var virtualBounds = new Rect(
                Controller.VirtualBounds.Left,
                Controller.VirtualBounds.Top,
                Controller.VirtualBounds.Width,
                Controller.VirtualBounds.Height);
            return ResizeRectByHandle(
                initial,
                _handle,
                global.X - _startPhysical.X,
                global.Y - _startPhysical.Y,
                virtualBounds);
        }

        private static Rect ResizeRectByHandle(Rect initial, int handle, double dx, double dy, Rect bounds)
        {
            double left = initial.Left;
            double top = initial.Top;
            double right = initial.Right;
            double bottom = initial.Bottom;

            switch (handle)
            {
                case 0:
                    left += dx;
                    top += dy;
                    break;
                case 1:
                    top += dy;
                    break;
                case 2:
                    right += dx;
                    top += dy;
                    break;
                case 3:
                    right += dx;
                    break;
                case 4:
                    right += dx;
                    bottom += dy;
                    break;
                case 5:
                    bottom += dy;
                    break;
                case 6:
                    left += dx;
                    bottom += dy;
                    break;
                case 7:
                    left += dx;
                    break;
            }

            left = Math.Clamp(left, bounds.Left, bounds.Right - 4);
            top = Math.Clamp(top, bounds.Top, bounds.Bottom - 4);
            right = Math.Clamp(right, left + 4, bounds.Right);
            bottom = Math.Clamp(bottom, top + 4, bounds.Bottom);
            return new Rect(left, top, right - left, bottom - top);
        }

        private static Rect NormalizeRect(Point start, Point end)
        {
            return new Rect(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Abs(end.X - start.X),
                Math.Abs(end.Y - start.Y));
        }

        private Rect ClampRect(Rect rect)
        {
            var virtualBounds = new Rect(
                Controller.VirtualBounds.Left,
                Controller.VirtualBounds.Top,
                Controller.VirtualBounds.Width,
                Controller.VirtualBounds.Height);
            var intersect = Rect.Intersect(rect, virtualBounds);
            return intersect.IsEmpty ? new Rect(rect.X, rect.Y, 0, 0) : intersect;
        }

        private static Point SnapAngle(Point start, Point end)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.1)
            {
                return end;
            }

            double angle = Math.Atan2(dy, dx);
            double step = Math.PI / 4;
            double snapped = Math.Round(angle / step) * step;
            return new Point(
                start.X + Math.Cos(snapped) * length,
                start.Y + Math.Sin(snapped) * length);
        }

        private void SelectWindowUnderCursor(Point global)
        {
            var point = new NativeMethods.POINT
            {
                X = (int)Math.Round(global.X),
                Y = (int)Math.Round(global.Y)
            };
            IntPtr hwnd = NativeMethods.WindowFromPoint(point);
            uint currentProcess = (uint)Process.GetCurrentProcess().Id;
            for (int i = 0; i < 64 && hwnd != IntPtr.Zero; i++)
            {
                hwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId != currentProcess &&
                    NativeMethods.IsWindowVisible(hwnd) &&
                    !NativeMethods.IsIconic(hwnd))
                {
                    var bounds = ScreenCaptureService.GetWindowBounds(hwnd);
                    if (bounds.Width >= 30 && bounds.Height >= 30)
                    {
                        var virtualBounds = new Rect(
                            Controller.VirtualBounds.Left,
                            Controller.VirtualBounds.Top,
                            Controller.VirtualBounds.Width,
                            Controller.VirtualBounds.Height);
                        var selection = Rect.Intersect(
                            new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
                            virtualBounds);
                        if (!selection.IsEmpty)
                        {
                            Controller.SetSelection(selection);
                            return;
                        }
                    }
                }

                hwnd = NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT);
            }
        }

        // ============================ Loupe ============================
        private void UpdateLoupe(Point global, Point local)
        {
            if (!SettingsService.Instance.Current.ShowCaptureLoupe)
            {
                LoupePanel.Visibility = Visibility.Collapsed;
                return;
            }

            var bitmap = Controller.VirtualBitmap;
            if (bitmap == null)
            {
                return;
            }

            int relativeX = (int)Math.Round(global.X) - Controller.VirtualBounds.Left;
            int relativeY = (int)Math.Round(global.Y) - Controller.VirtualBounds.Top;
            int radius = 8;
            int x = Math.Clamp(relativeX - radius, 0, Math.Max(0, bitmap.PixelWidth - 1));
            int y = Math.Clamp(relativeY - radius, 0, Math.Max(0, bitmap.PixelHeight - 1));
            int width = Math.Min(radius * 2 + 1, bitmap.PixelWidth - x);
            int height = Math.Min(radius * 2 + 1, bitmap.PixelHeight - y);
            if (width < 1 || height < 1)
            {
                return;
            }

            var crop = new CroppedBitmap(bitmap, new Int32Rect(x, y, width, height));
            crop.Freeze();
            LoupeImage.Source = crop;

            try
            {
                var pixel = new CroppedBitmap(bitmap, new Int32Rect(
                    Math.Clamp(relativeX, 0, bitmap.PixelWidth - 1),
                    Math.Clamp(relativeY, 0, bitmap.PixelHeight - 1),
                    1,
                    1));
                var bytes = new byte[4];
                pixel.CopyPixels(bytes, 4, 0);
                LoupeHex.Text = $"#{bytes[2]:X2}{bytes[1]:X2}{bytes[0]:X2}";
            }
            catch
            {
            }

            LoupeCoords.Text = $"{relativeX + Controller.VirtualBounds.Left}, {relativeY + Controller.VirtualBounds.Top}";
            LoupePanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = LoupePanel.DesiredSize;
            var candidates = new[]
            {
                new Rect(local.X + 20, local.Y + 20, size.Width, size.Height),
                new Rect(local.X - size.Width - 20, local.Y + 20, size.Width, size.Height),
                new Rect(local.X + 20, local.Y - size.Height - 20, size.Width, size.Height),
                new Rect(local.X - size.Width - 20, local.Y - size.Height - 20, size.Width, size.Height)
            };

            Rect? placement = null;
            foreach (var candidate in candidates)
            {
                var clamped = ClampChromeRect(candidate, 8);
                if (!OverlapsWithGap(clamped, _toolbarBounds) &&
                    !OverlapsWithGap(clamped, _actionToolbarBounds))
                {
                    placement = clamped;
                    break;
                }
            }

            if (!placement.HasValue)
            {
                LoupePanel.Visibility = Visibility.Collapsed;
                return;
            }

            Canvas.SetLeft(LoupePanel, placement.Value.Left);
            Canvas.SetTop(LoupePanel, placement.Value.Top);
            LoupePanel.Visibility = Visibility.Visible;
        }

        // ============================ Colour popup ============================
        private void ShowColorPopup()
        {
            if (_colorPopup == null)
            {
                BuildColorPopup();
            }

            if (_colorPopup == null)
            {
                return;
            }

            _colorPopup.PlacementTarget = _colorButton;
            _colorPopup.IsOpen = true;
        }

        private void BuildColorPopup()
        {
            var panel = new WrapPanel { Width = 188 };
            string[] palette =
            {
                "#E5534B", "#F2C94C", "#3FBF7F", "#4C8DF6",
                "#FFFFFF", "#141414", "#F2994A", "#2B88D8"
            };

            foreach (string hex in palette)
            {
                var button = new Button
                {
                    Style = (Style)FindResource("SecondaryButton"),
                    Width = 38,
                    Height = 30,
                    Padding = new Thickness(0),
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(AnnotationRenderer.ParseColor(hex)),
                    BorderBrush = ThemeService.GetBrush("BorderStrongBrush"),
                    ToolTip = hex
                };
                AutomationProperties.SetName(button, hex);
                button.Click += (_, _) =>
                {
                    Controller.SetColor(hex);
                    Controller.UpdateSelectedAnnotationStyle(color: hex);
                    SettingsService.Instance.Current.AnnotationColor = hex;
                    SettingsService.Instance.Save();
                    if (_colorPopup != null)
                    {
                        _colorPopup.IsOpen = false;
                    }
                };
                panel.Children.Add(button);
            }

            var customButton = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Content = LocalizationService.Get("Capture_Color"),
                Margin = new Thickness(2, 6, 2, 2),
                Padding = new Thickness(8, 4, 8, 4)
            };
            customButton.Click += (_, _) =>
            {
                if (_colorPopup != null)
                {
                    _colorPopup.IsOpen = false;
                }
                PickCustomColor();
            };
            panel.Children.Add(customButton);

            var container = new Border
            {
                Background = ThemeService.GetBrush("CardBackgroundBrush"),
                BorderBrush = ThemeService.GetBrush("BorderStrongBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4),
                Child = panel
            };

            _colorPopup = new Popup
            {
                Placement = PlacementMode.Right,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                Child = container
            };
        }

        private void PickCustomColor()
        {
            string? hex = ColorDialogHelper.PickColorHex(this, Controller.CurrentColor);
            if (string.IsNullOrEmpty(hex))
            {
                return;
            }

            Controller.SetColor(hex);
            Controller.UpdateSelectedAnnotationStyle(color: hex);
            SettingsService.Instance.Current.AnnotationColor = hex;
            SettingsService.Instance.Save();
        }

        // ============================ Text tool ============================
        private void ShowTextEditor(Point global, Annotation? existing = null)
        {
            _editingTextAnnotation = existing;
            _textAnchorPhysical = existing != null ? existing.Rect.TopLeft : global;
            var local = ToLocalDip(_textAnchorPhysical);
            string colorHex = existing?.Color ?? Controller.CurrentColor;
            double fontSize = existing != null
                ? Math.Max(11, existing.FontSize / Scale)
                : Math.Max(11, Controller.Thickness * 6 / Scale);

            TextEditor.Text = existing?.Text ?? "";
            TextEditor.FontSize = fontSize;
            TextEditor.Foreground = new SolidColorBrush(AnnotationRenderer.ParseColor(colorHex));
            bool rtl = existing?.IsRtl ?? ContainsArabic(TextEditor.Text);
            TextEditor.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            TextEditor.TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left;
            TextEditor.Visibility = Visibility.Visible;
            TextEditor.Width = Math.Max(140, Math.Min(320, ActualWidth - local.X - 20));
            Canvas.SetLeft(TextEditor, Math.Clamp(local.X, 4, Math.Max(4, ActualWidth - TextEditor.Width - 8)));
            Canvas.SetTop(TextEditor, Math.Clamp(local.Y, 4, Math.Max(4, ActualHeight - 40)));
            TextEditor.Focus();
            Keyboard.Focus(TextEditor);
            TextEditor.CaretIndex = TextEditor.Text.Length;
        }

        private void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool rtl = ContainsArabic(TextEditor.Text);
            TextEditor.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            TextEditor.TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left;
        }

        private static bool ContainsArabic(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (char character in text)
            {
                if (character >= '\u0600' && character <= '\u06FF')
                {
                    return true;
                }
            }

            return false;
        }

        private void TextEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelText();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                CommitText();
                e.Handled = true;
            }
        }

        private void TextEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (TextEditor.Visibility == Visibility.Visible && !_textCommitting)
            {
                CommitText();
            }
        }

        private void CommitText()
        {
            if (TextEditor.Visibility != Visibility.Visible || _textCommitting)
            {
                return;
            }

            _textCommitting = true;
            try
            {
                string text = TextEditor.Text.Trim();
                var editingTarget = _editingTextAnnotation;
                _editingTextAnnotation = null;
                TextEditor.Visibility = Visibility.Collapsed;
                TextEditor.Text = "";
                if (editingTarget != null)
                {
                    Controller.UpdateSelectedText(editingTarget, text, ContainsArabic(text));
                    return;
                }

                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                Controller.AddAnnotation(new Annotation
                {
                    Kind = AnnotationKind.Text,
                    Color = Controller.CurrentColor,
                    Thickness = Controller.Thickness,
                    Text = text,
                    FontSize = Math.Max(10, Controller.Thickness * 6),
                    IsRtl = ContainsArabic(text),
                    Rect = new Rect(_textAnchorPhysical.X, _textAnchorPhysical.Y, 0, 0)
                });
            }
            finally
            {
                _textCommitting = false;
            }
        }

        private void CancelText()
        {
            _textCommitting = true;
            _editingTextAnnotation = null;
            TextEditor.Visibility = Visibility.Collapsed;
            TextEditor.Text = "";
            _textCommitting = false;
            Focus();
        }

        // ============================ Keyboard ============================
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (TextEditor.IsKeyboardFocusWithin)
            {
                return;
            }

            bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if ((e.Key == Key.Delete || e.Key == Key.Back) &&
                Controller.Tool == CaptureTool.Select &&
                Controller.SelectedAnnotation != null)
            {
                Controller.RemoveAnnotation(Controller.SelectedAnnotation);
                SelectAnnotation(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                Controller.CloseAll();
                e.Handled = true;
                return;
            }

            if (!control && !shift && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) &&
                e.Key == Key.Space &&
                !Controller.Selection.HasValue &&
                Controller.HoveredWindow != null)
            {
                Controller.SetSelection(Controller.HoveredWindow.Bounds);
                Controller.SetHoveredWindow(null);
                if (SettingsService.Instance.Current.InstantCaptureCopy)
                {
                    Controller.CopySelection();
                }
                e.Handled = true;
                return;
            }

            if (!control && !shift && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) &&
                e.Key == Key.Return &&
                Controller.Selection.HasValue)
            {
                Controller.CopySelection();
                e.Handled = true;
                return;
            }

            if (control && (e.Key == Key.U || e.Key == Key.D))
            {
                Controller.UploadSelectionAsync();
                e.Handled = true;
                return;
            }
            if (control && e.Key == Key.C)
            {
                Controller.CopySelection();
                e.Handled = true;
                return;
            }
            if (control && e.Key == Key.S)
            {
                Controller.SaveSelection(quick: shift);
                e.Handled = true;
                return;
            }
            if (control && e.Key == Key.Z)
            {
                Controller.Undo();
                e.Handled = true;
                return;
            }
            if (control && e.Key == Key.Y)
            {
                Controller.Redo();
                e.Handled = true;
                return;
            }
            if (control && e.Key == Key.A)
            {
                Controller.SelectAll();
                e.Handled = true;
                return;
            }
            if (control && e.Key == Key.P)
            {
                Controller.PrintSelection();
                e.Handled = true;
                return;
            }

            if (control || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                return;
            }

            if (shift && e.Key == Key.O && Controller.Selection.HasValue)
            {
                Controller.OcrSelection(copyOnly: true);
                e.Handled = true;
                return;
            }

            if (!shift && e.Key == Key.F && Controller.Selection.HasValue)
            {
                Controller.TogglePrettyFrame();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.V)
            {
                Controller.SetTool(CaptureTool.Select);
                e.Handled = true;
                return;
            }

            CaptureTool? shortcut = e.Key switch
            {
                Key.D1 or Key.NumPad1 or Key.P => CaptureTool.Pen,
                Key.D2 or Key.NumPad2 or Key.L => CaptureTool.Line,
                Key.D3 or Key.NumPad3 or Key.A => CaptureTool.Arrow,
                Key.D4 or Key.NumPad4 or Key.R => CaptureTool.Rectangle,
                Key.D5 or Key.NumPad5 or Key.E => CaptureTool.Ellipse,
                Key.D6 or Key.NumPad6 or Key.S => CaptureTool.Spotlight,
                Key.D7 or Key.NumPad7 or Key.K => CaptureTool.Redaction,
                Key.D8 or Key.NumPad8 or Key.H => CaptureTool.Highlighter,
                Key.D9 or Key.NumPad9 or Key.T => CaptureTool.Text,
                Key.D0 or Key.NumPad0 or Key.B => CaptureTool.Blur,
                Key.M => CaptureTool.Zoom,
                Key.N => CaptureTool.Step,
                _ => null
            };
            if (shortcut.HasValue)
            {
                Controller.ToggleTool(shortcut.Value);
                e.Handled = true;
            }
        }
    }
}
