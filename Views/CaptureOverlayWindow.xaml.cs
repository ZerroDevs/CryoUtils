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
        private Point _textAnchorPhysical;
        private bool _textCommitting;
        private Rect _toolbarBounds = Rect.Empty;
        private Rect _actionToolbarBounds = Rect.Empty;

        public CaptureController Controller { get; }
        public MonitorInfo Monitor { get; }
        private double Scale => Math.Max(0.1, Monitor.Scale);

        public CaptureOverlayWindow(CaptureController controller, MonitorInfo monitor)
        {
            Controller = controller;
            Monitor = monitor;
            InitializeComponent();

            Width = monitor.Bounds.Width / Scale;
            Height = monitor.Bounds.Height / Scale;
            Left = 0;
            Top = 0;
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                Monitor.Bounds.Left,
                Monitor.Bounds.Top,
                Monitor.Bounds.Width,
                Monitor.Bounds.Height,
                NativeMethods.SWP_SHOWWINDOW);
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
            Controller.RemoveOverlay(this);
            base.OnClosed(e);
            if (!Controller.IsClosing)
            {
                Controller.CloseAll();
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
            AddToolButton(CaptureTool.Select, CryoIcons.Select, "Capture_ToolSelect", filled: true);
            AddSeparator();
            AddToolButton(CaptureTool.Pen, CryoIcons.Pen, "Capture_ToolPen");
            AddToolButton(CaptureTool.Line, CryoIcons.Line, "Capture_ToolLine");
            AddToolButton(CaptureTool.Arrow, CryoIcons.Arrow, "Capture_ToolArrow");
            AddToolButton(CaptureTool.Rectangle, CryoIcons.Rectangle, "Capture_ToolRect");
            AddToolButton(CaptureTool.Highlighter, CryoIcons.Highlighter, "Capture_ToolHighlighter");
            AddToolButton(CaptureTool.Text, CryoIcons.Text, "Capture_ToolText");
            AddToolButton(CaptureTool.Blur, CryoIcons.Blur, "Capture_ToolBlur");
            AddStepToolButton();
            AddSeparator();

            _undoButton = CreateIconButton(CryoIcons.Undo, "Capture_ToolUndo", () => Controller.Undo(), false);
            _redoButton = CreateIconButton(CryoIcons.Redo, "Capture_ToolRedo", () => Controller.Redo(), false);
            AnnotationToolbar.Children.Add(_undoButton);
            AnnotationToolbar.Children.Add(_redoButton);

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

            AddActionButton(CryoIcons.Copy, "Capture_ToolCopy", () => Controller.CopySelection());
            AddActionButton(CryoIcons.Save, "Capture_ToolSave", () => Controller.SaveSelection(quick: false));
            AddActionButton(CryoIcons.Record, "Capture_ToolRecord", () => Controller.BeginRecording());
            AddActionSeparator();
            AddActionButton(CryoIcons.Ocr, "Capture_ToolOcr", () => Controller.OcrSelection());
            AddActionButton(CryoIcons.QrCode, "Capture_ToolQr", () => Controller.QrSelection());
            AddActionButton(CryoIcons.Pin, "Capture_ToolPin", () => Controller.PinSelection());
            AddActionButton(CryoIcons.Print, "Capture_ToolPrint", () => Controller.PrintSelection());
            AddActionSeparator();
            AddActionButton(CryoIcons.Close, "Capture_ToolClose", () => Controller.CloseAll());

            UpdateToolStyles();
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
                Width = 32,
                Height = 30,
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0),
                ToolTip = LocalizationService.Get(tooltipKey)
            };
            AutomationProperties.SetName(button, LocalizationService.Get(tooltipKey));
            var icon = CryoIcons.Create(iconData, 18, !filled);
            button.Content = new Grid
            {
                Width = 18,
                Height = 18,
                Children = { icon }
            };
            button.Click += (_, _) => Controller.SetTool(tool);
            _toolButtons[tool] = new ToolButtonRef { Button = button, Icon = icon, Filled = filled };
            AnnotationToolbar.Children.Add(button);
        }

        private void AddStepToolButton()
        {
            var button = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 32,
                Height = 30,
                Margin = new Thickness(0, 1, 0, 1),
                ToolTip = LocalizationService.Get("Capture_ToolStep")
            };
            AutomationProperties.SetName(button, LocalizationService.Get("Capture_ToolStep"));
            var circle = CryoIcons.Create(CryoIcons.Step, 18, true);
            var number = new TextBlock
            {
                Text = "1",
                Foreground = ThemeService.GetBrush("IconBrush"),
                FontSize = 8.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var grid = new Grid { Width = 18, Height = 18 };
            grid.Children.Add(circle);
            grid.Children.Add(number);
            button.Content = grid;
            button.Click += (_, _) => Controller.SetTool(CaptureTool.Step);
            _toolButtons[CaptureTool.Step] = new ToolButtonRef { Button = button, Icon = circle, Filled = false };
            AnnotationToolbar.Children.Add(button);
        }

        private Button CreateIconButton(string iconData, string tooltipKey, Action click, bool filled = false)
        {
            var button = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(0),
                ToolTip = LocalizationService.Get(tooltipKey)
            };
            AutomationProperties.SetName(button, LocalizationService.Get(tooltipKey));
            var icon = CryoIcons.Create(iconData, 17, !filled);
            button.Content = icon;
            button.Click += (_, _) => click();
            return button;
        }

        private void AddActionButton(string iconData, string tooltipKey, Action click)
        {
            var button = CreateIconButton(iconData, tooltipKey, click);
            button.Margin = new Thickness(1, 0, 1, 0);
            ActionToolbar.Children.Add(button);
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
                e.Handled = true;
                return;
            }

            if (Controller.Tool != CaptureTool.Select)
            {
                if (Controller.Selection.HasValue && Controller.Selection.Value.Contains(global))
                {
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
                CaptureTool.Highlighter => AnnotationKind.Highlighter,
                CaptureTool.Blur => AnnotationKind.Blur,
                _ => AnnotationKind.Pen
            };

            var annotation = new Annotation
            {
                Kind = kind,
                Color = Controller.CurrentColor,
                Thickness = kind == AnnotationKind.Highlighter
                    ? Math.Max(8, Controller.Thickness * 4)
                    : Controller.Thickness,
                Rect = new Rect(global.X, global.Y, 0, 0)
            };

            if (kind is AnnotationKind.Pen or AnnotationKind.Highlighter)
            {
                annotation.Points.Add(global);
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
            Surface.Refresh();
            UpdateCursor(global);

            bool overToolbar = AnnotationToolbarBorder.IsMouseOver || ActionToolbarBorder.IsMouseOver;
            if (SettingsService.Instance.Current.ShowCaptureLoupe &&
                !overToolbar &&
                Controller.Selection.HasValue)
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
                case AnnotationKind.Blur:
                    var rect = NormalizeRect(_startPhysical, global);
                    if (shift && annotation.Kind == AnnotationKind.Rectangle)
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
            if (!_creating && !_moving && !_resizing && !_drawing)
            {
                return;
            }

            bool autoRecord = false;
            if (_creating)
            {
                var selection = Controller.Selection;
                if (selection.HasValue && (selection.Value.Width < 4 || selection.Value.Height < 4))
                {
                    Controller.SetSelection(null);
                }
                else if (selection.HasValue && Controller.RecordAfterSelection)
                {
                    autoRecord = true;
                }
            }

            if (_drawing && _activeAnnotation != null)
            {
                if (_activeAnnotation.Kind is AnnotationKind.Rectangle or AnnotationKind.Blur &&
                    (_activeAnnotation.Rect.Width < 2 || _activeAnnotation.Rect.Height < 2))
                {
                    Controller.Annotations.Remove(_activeAnnotation);
                    Controller.NotifyChanged();
                }
            }

            _creating = false;
            _moving = false;
            _resizing = false;
            _drawing = false;
            _activeAnnotation = null;
            _handle = -1;
            if (Surface.IsMouseCaptured)
            {
                Surface.ReleaseMouseCapture();
            }

            LoupePanel.Visibility = Visibility.Collapsed;
            e.Handled = true;

            if (autoRecord)
            {
                Controller.RecordAfterSelection = false;
                Controller.BeginRecording();
            }
        }

        private void Surface_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Controller.CloseAll();
            e.Handled = true;
        }

        private void Surface_MouseLeave(object sender, MouseEventArgs e)
        {
            LoupePanel.Visibility = Visibility.Collapsed;
            Surface.CursorPhysical = null;
            Surface.Refresh();
        }

        private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.5 : 1;
            Controller.SetThickness(Controller.Thickness + (e.Delta > 0 ? step : -step));
            ShowThicknessPreview(e.GetPosition(Surface));
            e.Handled = true;
        }

        private void ShowThicknessPreview(Point local)
        {
            ThicknessText.Text = LocalizationService.Format(
                "Capture_SizePreview",
                Math.Round(Controller.Thickness, 1));
            double dot = Math.Clamp(Controller.Thickness * 1.2, 3, 20);
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

            var local = ToLocalDip(global);
            var rect = ToLocalRect(Controller.Selection.Value);
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
            double dx = global.X - _startPhysical.X;
            double dy = global.Y - _startPhysical.Y;
            double left = initial.Left;
            double top = initial.Top;
            double right = initial.Right;
            double bottom = initial.Bottom;

            switch (_handle)
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

            var virtualBounds = new Rect(
                Controller.VirtualBounds.Left,
                Controller.VirtualBounds.Top,
                Controller.VirtualBounds.Width,
                Controller.VirtualBounds.Height);
            left = Math.Clamp(left, virtualBounds.Left, virtualBounds.Right - 4);
            top = Math.Clamp(top, virtualBounds.Top, virtualBounds.Bottom - 4);
            right = Math.Clamp(right, left + 4, virtualBounds.Right);
            bottom = Math.Clamp(bottom, top + 4, virtualBounds.Bottom);
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
                "#FFFFFF", "#141414", "#F2994A", "#2FD3BD"
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
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(
                    AnnotationRenderer.ParseColor(Controller.CurrentColor).R,
                    AnnotationRenderer.ParseColor(Controller.CurrentColor).G,
                    AnnotationRenderer.ParseColor(Controller.CurrentColor).B)
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            Controller.SetColor(hex);
            SettingsService.Instance.Current.AnnotationColor = hex;
            SettingsService.Instance.Save();
        }

        // ============================ Text tool ============================
        private void ShowTextEditor(Point global)
        {
            _textAnchorPhysical = global;
            var local = ToLocalDip(global);
            TextEditor.Text = "";
            TextEditor.FontSize = Math.Max(11, Controller.Thickness * 6 / Scale);
            TextEditor.Foreground = new SolidColorBrush(AnnotationRenderer.ParseColor(Controller.CurrentColor));
            TextEditor.FlowDirection = FlowDirection.LeftToRight;
            TextEditor.TextAlignment = TextAlignment.Left;
            TextEditor.Visibility = Visibility.Visible;
            TextEditor.Width = Math.Max(140, Math.Min(320, ActualWidth - local.X - 20));
            Canvas.SetLeft(TextEditor, Math.Clamp(local.X, 4, Math.Max(4, ActualWidth - TextEditor.Width - 8)));
            Canvas.SetTop(TextEditor, Math.Clamp(local.Y, 4, Math.Max(4, ActualHeight - 40)));
            TextEditor.Focus();
            Keyboard.Focus(TextEditor);
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
                TextEditor.Visibility = Visibility.Collapsed;
                TextEditor.Text = "";
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
            if (e.Key == Key.Escape)
            {
                Controller.CloseAll();
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

            CaptureTool? shortcut = e.Key switch
            {
                Key.V => CaptureTool.Select,
                Key.P => CaptureTool.Pen,
                Key.L => CaptureTool.Line,
                Key.A => CaptureTool.Arrow,
                Key.R => CaptureTool.Rectangle,
                Key.H => CaptureTool.Highlighter,
                Key.T => CaptureTool.Text,
                Key.B => CaptureTool.Blur,
                Key.N => CaptureTool.Step,
                _ => null
            };
            if (shortcut.HasValue)
            {
                Controller.SetTool(shortcut.Value);
                e.Handled = true;
            }
        }
    }
}
