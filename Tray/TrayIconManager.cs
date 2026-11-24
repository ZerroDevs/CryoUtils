using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using CryoUtils.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CryoUtils.Tray
{
    public sealed class TrayIconManager : IDisposable
    {
        private const int MainMenuFloorWidth = 216;
        private const int SubMenuFloorWidth = 190;
        private const int MenuTextHorizontalReserve = 48;

        private readonly Forms.NotifyIcon _notifyIcon;
        private readonly Drawing.Icon _icon;
        private Forms.ContextMenuStrip? _menu;
        private IntPtr _iconHandle;

        private readonly Drawing.Font _menuFont = new("Segoe UI", 9.5f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point);
        private readonly Drawing.Font _menuPrimaryFont = new("Segoe UI Semibold", 9.5f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Point);

        public TrayIconManager()
        {
            _icon = CreateIcon(out _iconHandle);
            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _icon,
                Text = LocalizationService.Get("Tray_Tooltip"),
                Visible = true
            };

            _menu = BuildMenu();
            _notifyIcon.ContextMenuStrip = _menu;

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            TopmostService.Instance.StateChanged += OnTopmostStateChanged;

            _notifyIcon.DoubleClick += (_, _) => Invoke(() => App.CurrentApp?.ShowDashboard());
            _notifyIcon.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    Invoke(() =>
                    {
                        var app = App.CurrentApp;
                        if (app == null)
                        {
                            return;
                        }
                        if (string.Equals(
                                SettingsService.Instance.Current.TrayLeftClickAction,
                                "Capture",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            app.StartCapture();
                        }
                        else
                        {
                            app.ShowDashboard();
                        }
                    });
                }
            };
        }

        private Forms.ContextMenuStrip BuildMenu()
        {
            bool rtl = LocalizationService.Instance.IsArabic;
            var renderer = new DarkMenuRenderer();
            var menu = new Forms.ContextMenuStrip();
            ConfigureDarkDropDown(menu, renderer, rtl);

            // 1. Primary Open Dashboard item
            var itemOpen = CreateMenuItem(
                LocalizationService.Get("Tray_OpenDashboard"),
                () => App.CurrentApp?.ShowDashboard(),
                _menuPrimaryFont);
            menu.Items.Add(itemOpen);
            menu.Items.Add(CreateSeparator());

            // 2. Screen capture & recording tools
            var itemCapture = CreateMenuItem(
                LocalizationService.Get("Tray_TakeScreenshot"),
                () => App.CurrentApp?.StartCapture(0));
            menu.Items.Add(itemCapture);

            var delayedSubmenu = new Forms.ToolStripMenuItem(LocalizationService.Get("Tray_TakeDelayed"));
            if (delayedSubmenu.DropDown is Forms.ToolStripDropDownMenu delayedDropDown)
            {
                ConfigureDarkDropDown(delayedDropDown, renderer, rtl);
            }
            delayedSubmenu.DropDownItems.Add(CreateMenuItem(
                LocalizationService.Get("Tray_Delay3s"),
                () => App.CurrentApp?.StartCapture(3)));
            delayedSubmenu.DropDownItems.Add(CreateMenuItem(
                LocalizationService.Get("Tray_Delay5s"),
                () => App.CurrentApp?.StartCapture(5)));
            delayedSubmenu.DropDownItems.Add(CreateMenuItem(
                LocalizationService.Get("Tray_Delay10s"),
                () => App.CurrentApp?.StartCapture(10)));
            menu.Items.Add(delayedSubmenu);

            var itemRecord = CreateMenuItem(
                LocalizationService.Get("Tray_RecordGif"),
                () => App.CurrentApp?.StartRecording());
            menu.Items.Add(itemRecord);

            menu.Items.Add(CreateSeparator());

            // 3. Local Utilities
            var itemColor = CreateMenuItem(
                LocalizationService.Get("Tray_ColorPicker"),
                () => App.CurrentApp?.OpenColorPicker());
            menu.Items.Add(itemColor);

            var qrSubmenu = new Forms.ToolStripMenuItem(LocalizationService.Get("Tray_QrSuite"));
            if (qrSubmenu.DropDown is Forms.ToolStripDropDownMenu qrDropDown)
            {
                ConfigureDarkDropDown(qrDropDown, renderer, rtl);
            }
            qrSubmenu.DropDownItems.Add(CreateMenuItem(
                LocalizationService.Get("Tray_QrScan"),
                () => App.CurrentApp?.StartCapture()));
            qrSubmenu.DropDownItems.Add(CreateMenuItem(
                LocalizationService.Get("Tray_QrGenerate"),
                () => App.CurrentApp?.ShowDashboard("Tools")));
            menu.Items.Add(qrSubmenu);

            var itemClipboard = CreateMenuItem(
                LocalizationService.Get("Tray_Clipboard"),
                () => App.CurrentApp?.OpenClipboardHistory());
            menu.Items.Add(itemClipboard);

            // Window Pinner dynamic items
            if (TopmostService.Instance.IsPinned)
            {
                string title = TopmostService.Instance.PinnedTitle;
                if (title.Length > 24)
                {
                    title = title.Substring(0, 21) + "...";
                }
                string itemText = LocalizationService.Format("Tray_UnpinWindow", title);
                menu.Items.Add(CreateMenuItem(itemText, () => TopmostService.Instance.Unpin()));
                menu.Items.Add(CreateMenuItem(
                    LocalizationService.Get("Tray_ResetPinner"),
                    () => TopmostService.Instance.ForceClearAll()));
            }
            else
            {
                menu.Items.Add(CreateMenuItem(
                    LocalizationService.Get("Tray_PinWindow"),
                    () => App.CurrentApp?.TogglePinnerFromDashboard()));
            }

            menu.Items.Add(CreateSeparator());

            // 4. App settings & exit
            var itemSettings = CreateMenuItem(
                LocalizationService.Get("Tray_Settings"),
                () => App.CurrentApp?.ShowDashboard("General"));
            menu.Items.Add(itemSettings);

            menu.Items.Add(CreateSeparator());

            var itemExit = CreateMenuItem(
                LocalizationService.Get("Tray_Exit"),
                () => App.CurrentApp?.ExitApplication());
            menu.Items.Add(itemExit);

            // Dynamic size adjustment on opening
            menu.Opening += (_, _) =>
            {
                UpdateDropDownSizes(menu, MainMenuFloorWidth, MenuTextHorizontalReserve);
                UpdateDropDownSizes(delayedSubmenu.DropDown, SubMenuFloorWidth, MenuTextHorizontalReserve);
                UpdateDropDownSizes(qrSubmenu.DropDown, SubMenuFloorWidth, MenuTextHorizontalReserve);
            };

            UpdateDropDownSizes(menu, MainMenuFloorWidth, MenuTextHorizontalReserve);
            UpdateDropDownSizes(delayedSubmenu.DropDown, SubMenuFloorWidth, MenuTextHorizontalReserve);
            UpdateDropDownSizes(qrSubmenu.DropDown, SubMenuFloorWidth, MenuTextHorizontalReserve);

            return menu;
        }

        private void ConfigureDarkDropDown(Forms.ToolStripDropDownMenu dropDown, Forms.ToolStripRenderer renderer, bool rtl)
        {
            dropDown.Renderer = renderer;
            dropDown.ShowImageMargin = false;
            dropDown.ShowCheckMargin = false;
            dropDown.BackColor = DarkMenuRenderer.SurfaceBg;
            dropDown.ForeColor = DarkMenuRenderer.TextNormal;
            dropDown.Font = _menuFont;
            dropDown.Padding = new Forms.Padding(1, 5, 1, 5);
            dropDown.RightToLeft = rtl ? Forms.RightToLeft.Yes : Forms.RightToLeft.No;
        }

        private Forms.ToolStripMenuItem CreateMenuItem(string text, Action onClick, Drawing.Font? font = null)
        {
            var item = new Forms.ToolStripMenuItem(text)
            {
                ForeColor = DarkMenuRenderer.TextNormal,
                Padding = new Forms.Padding(0, 5, 0, 5)
            };
            if (font != null)
            {
                item.Font = font;
            }
            item.Click += (_, _) => Invoke(onClick);
            return item;
        }

        private static Forms.ToolStripSeparator CreateSeparator() => new()
        {
            AutoSize = false,
            Height = 9,
            Margin = new Forms.Padding(0, 2, 0, 2)
        };

        private void UpdateDropDownSizes(Forms.ToolStripDropDown dropDown, int floorWidth, int horizontalExtra)
        {
            int maxTextWidth = 0;
            foreach (Forms.ToolStripItem item in dropDown.Items)
            {
                if (item is Forms.ToolStripMenuItem menuItem && !string.IsNullOrEmpty(menuItem.Text))
                {
                    Drawing.Size measured = Forms.TextRenderer.MeasureText(
                        menuItem.Text,
                        menuItem.Font ?? _menuFont,
                        Drawing.Size.Empty,
                        Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.NoPrefix);
                    if (measured.Width > maxTextWidth)
                    {
                        maxTextWidth = measured.Width;
                    }
                }
            }

            int targetWidth = Math.Max(floorWidth, maxTextWidth + horizontalExtra);
            dropDown.MinimumSize = new Drawing.Size(targetWidth, 0);

            foreach (Forms.ToolStripItem item in dropDown.Items)
            {
                if (item is Forms.ToolStripMenuItem menuItem)
                {
                    int textH = Forms.TextRenderer.MeasureText("Ag", menuItem.Font ?? _menuFont).Height;
                    int itemHeight = Math.Max(30, textH + 12);
                    menuItem.AutoSize = false;
                    menuItem.Size = new Drawing.Size(targetWidth - 2, itemHeight);
                }
            }
        }

        private void OnLanguageChanged()
        {
            Invoke(RebuildMenu);
        }

        private void OnTopmostStateChanged()
        {
            Invoke(RebuildMenu);
        }

        private void RebuildMenu()
        {
            var previous = _menu;
            _menu = BuildMenu();
            _notifyIcon.ContextMenuStrip = _menu;
            previous?.Dispose();
        }

        private static void Invoke(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action);
            }
        }

        private static Drawing.Icon CreateIcon(out IntPtr handle)
        {
            handle = IntPtr.Zero;
            try
            {
                var resource = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/Images/CryoUtils-Tray.ico"));
                if (resource != null)
                {
                    using var stream = resource.Stream;
                    int traySize = Forms.SystemInformation.SmallIconSize.Width;
                    traySize = Math.Clamp(traySize, 16, 48);
                    return new Drawing.Icon(stream, new Drawing.Size(traySize, traySize));
                }
            }
            catch
            {
                // Fall back to the vector mark if the packaged icon cannot be read.
            }

            using var bitmap = new Drawing.Bitmap(32, 32);
            using (var graphics = Drawing.Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Drawing.Color.Transparent);
                using var pen = new Drawing.Pen(Drawing.Color.FromArgb(47, 211, 189), 2.2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };

                graphics.DrawLines(pen, new[]
                {
                    new Drawing.PointF(13, 6), new Drawing.PointF(8, 6), new Drawing.PointF(6, 8),
                    new Drawing.PointF(6, 13)
                });
                graphics.DrawLines(pen, new[]
                {
                    new Drawing.PointF(19, 6), new Drawing.PointF(24, 6), new Drawing.PointF(26, 8),
                    new Drawing.PointF(26, 13)
                });
                graphics.DrawLines(pen, new[]
                {
                    new Drawing.PointF(6, 19), new Drawing.PointF(6, 24), new Drawing.PointF(8, 26),
                    new Drawing.PointF(13, 26)
                });
                graphics.DrawLines(pen, new[]
                {
                    new Drawing.PointF(26, 19), new Drawing.PointF(26, 24), new Drawing.PointF(24, 26),
                    new Drawing.PointF(19, 26)
                });
                using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(47, 211, 189));
                graphics.FillEllipse(dot, 13, 13, 6, 6);
            }

            handle = bitmap.GetHicon();
            using var temp = Drawing.Icon.FromHandle(handle);
            var icon = (Drawing.Icon)temp.Clone();
            return icon;
        }

        public void Dispose()
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            TopmostService.Instance.StateChanged -= OnTopmostStateChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _icon.Dispose();
            _menu?.Dispose();
            if (_iconHandle != IntPtr.Zero)
            {
                DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        // Custom owner-drawn renderer avoiding .NET 8 ToolStripProfessionalRenderer white margins.
        private sealed class DarkMenuRenderer : Forms.ToolStripRenderer
        {
            public static readonly Drawing.Color SurfaceBg = Drawing.Color.FromArgb(20, 24, 28);
            public static readonly Drawing.Color SurfaceBorder = Drawing.Color.FromArgb(46, 52, 60);
            public static readonly Drawing.Color HoverFill = Drawing.Color.FromArgb(32, 40, 48);
            public static readonly Drawing.Color PressedFill = Drawing.Color.FromArgb(26, 34, 42);
            public static readonly Drawing.Color HoverBorder = Drawing.Color.FromArgb(47, 211, 189);
            public static readonly Drawing.Color SeparatorLine = Drawing.Color.FromArgb(40, 46, 54);
            public static readonly Drawing.Color TextNormal = Drawing.Color.FromArgb(236, 239, 242);
            public static readonly Drawing.Color TextSelected = Drawing.Color.White;
            public static readonly Drawing.Color TextDisabled = Drawing.Color.FromArgb(120, 126, 134);
            public static readonly Drawing.Color ChevronNormal = Drawing.Color.FromArgb(170, 178, 186);
            public static readonly Drawing.Color ChevronSelected = Drawing.Color.FromArgb(47, 211, 189);

            protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
            {
                using var bg = new Drawing.SolidBrush(SurfaceBg);
                e.Graphics.FillRectangle(bg, e.AffectedBounds);
            }

            protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
            {
                // Suppress default Win32 white image/check margin strip.
            }

            protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
            {
                SmoothingMode prev = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.None;
                using var pen = new Drawing.Pen(SurfaceBorder, 1f);
                e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
                e.Graphics.SmoothingMode = prev;
            }

            protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
            {
                int stripW = e.ToolStrip != null && e.ToolStrip.Width > 0
                    ? e.ToolStrip.Width
                    : e.Item.Width + 2;
                int itemX = e.Item.Bounds.Left;

                if (e.Item.Enabled && (e.Item.Selected || e.Item.Pressed))
                {
                    int pillLeft = 5 - itemX;
                    int pillRight = (stripW - 6) - itemX;
                    var pillRect = new Drawing.Rectangle(pillLeft, 1, Math.Max(8, pillRight - pillLeft), Math.Max(8, e.Item.Height - 3));
                    Drawing.Color fillColor = e.Item.Pressed ? PressedFill : HoverFill;

                    SmoothingMode prev = e.Graphics.SmoothingMode;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using var path = CreateRoundedRectPath(pillRect, 4);
                    using var fill = new Drawing.SolidBrush(fillColor);
                    using var border = new Drawing.Pen(HoverBorder, 1f);
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                    e.Graphics.SmoothingMode = prev;
                }
            }

            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                int stripW = e.ToolStrip != null && e.ToolStrip.Width > 0
                    ? e.ToolStrip.Width
                    : e.Item.Width + 2;
                int itemX = e.Item.Bounds.Left;
                bool rtl = IsRtl(e.ToolStrip, e.Item);

                int textLeft = rtl ? 32 - itemX : 14 - itemX;
                int textRight = rtl ? (stripW - 14) - itemX : (stripW - 32) - itemX;
                var textBounds = new Drawing.Rectangle(textLeft, 0, Math.Max(20, textRight - textLeft), e.Item.Height);

                Drawing.Color textColor = !e.Item.Enabled
                    ? TextDisabled
                    : (e.Item.Selected || e.Item.Pressed ? TextSelected : TextNormal);

                Forms.TextFormatFlags flags = Forms.TextFormatFlags.VerticalCenter
                    | Forms.TextFormatFlags.SingleLine
                    | Forms.TextFormatFlags.EndEllipsis
                    | Forms.TextFormatFlags.NoPrefix;
                if (rtl)
                {
                    flags |= Forms.TextFormatFlags.Right | Forms.TextFormatFlags.RightToLeft;
                }

                Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textBounds, textColor, flags);
            }

            protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
            {
                Forms.ToolStrip? owner = e.Item?.Owner;
                int stripW = owner != null && owner.Width > 0
                    ? owner.Width
                    : (e.Item?.Width ?? 0) + 2;
                int itemX = e.Item?.Bounds.Left ?? 0;
                bool rtl = IsRtl(owner, e.Item);

                float cx = rtl ? 18f - itemX : (stripW - 18f) - itemX;
                float cy = (e.Item?.Height ?? e.ArrowRectangle.Height) / 2f;
                Drawing.Color arrowColor = e.Item != null && !e.Item.Enabled
                    ? TextDisabled
                    : (e.Item != null && (e.Item.Selected || e.Item.Pressed) ? ChevronSelected : ChevronNormal);

                SmoothingMode prev = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Drawing.Pen(arrowColor, 1.55f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };

                Drawing.PointF[] pts = rtl
                    ? new[]
                    {
                        new Drawing.PointF(cx + 2.0f, cy - 3.6f),
                        new Drawing.PointF(cx - 2.0f, cy),
                        new Drawing.PointF(cx + 2.0f, cy + 3.6f)
                    }
                    : new[]
                    {
                        new Drawing.PointF(cx - 2.0f, cy - 3.6f),
                        new Drawing.PointF(cx + 2.0f, cy),
                        new Drawing.PointF(cx - 2.0f, cy + 3.6f)
                    };
                e.Graphics.DrawLines(pen, pts);
                e.Graphics.SmoothingMode = prev;
            }

            protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
            {
                int stripW = e.ToolStrip != null && e.ToolStrip.Width > 0
                    ? e.ToolStrip.Width
                    : e.Item.Width + 4;
                int sepX = e.Item.Bounds.Left;
                int left = 10 - sepX;
                int right = (stripW - 11) - sepX;
                int y = e.Item.Height / 2;

                SmoothingMode prev = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.None;
                using var pen = new Drawing.Pen(SeparatorLine, 1f);
                e.Graphics.DrawLine(pen, left, y, right, y);
                e.Graphics.SmoothingMode = prev;
            }

            private static bool IsRtl(Forms.ToolStrip? strip, Forms.ToolStripItem? item) =>
                (item?.RightToLeft == Forms.RightToLeft.Yes) || (strip?.RightToLeft == Forms.RightToLeft.Yes);

            private static GraphicsPath CreateRoundedRectPath(Drawing.Rectangle bounds, int radius)
            {
                int diameter = radius * 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                return path;
            }
        }
    }
}
