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
        private readonly Forms.NotifyIcon _notifyIcon;
        private readonly Drawing.Icon _icon;
        private Forms.ContextMenuStrip? _menu;
        private IntPtr _iconHandle;

        public TrayIconManager()
        {
            _icon = CreateIcon(out _iconHandle);
            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _icon,
                Text = "CryoUtils",
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
            var menu = new Forms.ContextMenuStrip
            {
                BackColor = Drawing.Color.FromArgb(22, 24, 27),
                ForeColor = Drawing.Color.FromArgb(236, 239, 242),
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Padding = new Forms.Padding(3),
                Font = new Drawing.Font("Segoe UI", 9f),
                RightToLeft = rtl ? Forms.RightToLeft.Yes : Forms.RightToLeft.No
            };
            menu.Renderer = new CryoMenuRenderer(menu.BackColor, Drawing.Color.FromArgb(15, 122, 108));
            menu.Items.Add(CreateItem(LocalizationService.Get("Tray_TakeScreenshot"), (_, _) => Invoke(() => App.CurrentApp?.StartCapture(0))));
            var delayedSubmenu = new Forms.ToolStripMenuItem(LocalizationService.Get("Tray_TakeDelayed"))
            {
                ForeColor = Drawing.Color.FromArgb(236, 239, 242),
                Padding = new Forms.Padding(6, 3, 6, 3)
            };
            delayedSubmenu.DropDownItems.Add(CreateItem(LocalizationService.Get("Tray_Delay3s"), (_, _) => Invoke(() => App.CurrentApp?.StartCapture(3))));
            delayedSubmenu.DropDownItems.Add(CreateItem(LocalizationService.Get("Tray_Delay5s"), (_, _) => Invoke(() => App.CurrentApp?.StartCapture(5))));
            delayedSubmenu.DropDownItems.Add(CreateItem(LocalizationService.Get("Tray_Delay10s"), (_, _) => Invoke(() => App.CurrentApp?.StartCapture(10))));
            menu.Items.Add(delayedSubmenu);
            menu.Items.Add(CreateItem(LocalizationService.Get("Tray_RecordGif"), (_, _) => Invoke(() => App.CurrentApp?.StartRecording())));
            menu.Items.Add(CreateItem(LocalizationService.Get("Tray_ColorPicker"), (_, _) => Invoke(() => App.CurrentApp?.OpenColorPicker())));

            if (TopmostService.Instance.IsPinned)
            {
                string title = TopmostService.Instance.PinnedTitle;
                if (title.Length > 24)
                {
                    title = title.Substring(0, 21) + "...";
                }
                string itemText = LocalizationService.Format("Tray_UnpinWindow", title);
                menu.Items.Add(CreateItem(itemText, (_, _) => Invoke(() => TopmostService.Instance.Unpin())));
                menu.Items.Add(CreateItem(LocalizationService.Get("Tray_ResetPinner"), (_, _) => Invoke(() => TopmostService.Instance.ForceClearAll())));
            }
            else
            {
                menu.Items.Add(CreateItem(LocalizationService.Get("Tray_PinWindow"), (_, _) => Invoke(() => App.CurrentApp?.TogglePinnerFromDashboard())));
            }

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(CreateItem(LocalizationService.Get("Tray_OpenDashboard"), (_, _) => Invoke(() => App.CurrentApp?.ShowDashboard())));
            menu.Items.Add(CreateItem(LocalizationService.Get("Tray_Settings"), (_, _) => Invoke(() => App.CurrentApp?.ShowDashboard("General"))));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(CreateItem(LocalizationService.Get("Tray_Exit"), (_, _) => Invoke(() => App.CurrentApp?.ExitApplication())));
            return menu;
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

        private static Forms.ToolStripMenuItem CreateItem(string text, EventHandler handler)
        {
            return new Forms.ToolStripMenuItem(text)
            {
                ForeColor = Drawing.Color.FromArgb(236, 239, 242),
                Padding = new Forms.Padding(6, 3, 6, 3)
            }.WithHandler(handler);
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
            if (_iconHandle != IntPtr.Zero)
            {
                DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        private sealed class CryoMenuRenderer : Forms.ToolStripProfessionalRenderer
        {
            private readonly Drawing.Color _background;
            private readonly Drawing.Color _accent;

            public CryoMenuRenderer(Drawing.Color background, Drawing.Color accent)
                : base(new CryoColorTable(background, accent))
            {
                _background = background;
                _accent = accent;
                RoundedEdges = false;
            }

            protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
            {
                using var brush = new Drawing.SolidBrush(_background);
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using var pen = new Drawing.Pen(Drawing.Color.FromArgb(46, 50, 56));
                e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
            }

            protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected)
                {
                    return;
                }

                using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(30, 34, 39));
                e.Graphics.FillRectangle(brush, new Drawing.Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2));
            }
        }

        private sealed class CryoColorTable : Forms.ProfessionalColorTable
        {
            private readonly Drawing.Color _background;
            private readonly Drawing.Color _accent;

            public CryoColorTable(Drawing.Color background, Drawing.Color accent)
            {
                _background = background;
                _accent = accent;
                UseSystemColors = false;
            }

            public override Drawing.Color ToolStripDropDownBackground => _background;
            public override Drawing.Color ImageMarginGradientBegin => _background;
            public override Drawing.Color ImageMarginGradientMiddle => _background;
            public override Drawing.Color ImageMarginGradientEnd => _background;
            public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(46, 50, 56);
            public override Drawing.Color MenuItemBorder => Drawing.Color.Transparent;
            public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(30, 34, 39);
            public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(46, 50, 56);
            public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(46, 50, 56);
        }
    }

    internal static class ToolStripItemExtensions
    {
        public static Forms.ToolStripMenuItem WithHandler(
            this Forms.ToolStripMenuItem item,
            EventHandler handler)
        {
            item.Click += handler;
            return item;
        }
    }
}
