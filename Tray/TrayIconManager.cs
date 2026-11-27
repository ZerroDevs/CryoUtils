using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using CryoUtils.Services;
using Drawing = System.Drawing;

namespace CryoUtils.Tray
{
    /// <summary>
    /// Native Win32 system-tray host and dark owner-drawn popup menu, zero WinForms dependency.
    /// </summary>
    public sealed class TrayIconManager : IDisposable
    {
        private const int MainMenuFloorWidth = 216;
        private const int SubMenuFloorWidth = 190;
        private const int MenuTextHorizontalReserve = 48;
        private const uint WmTrayIcon = 0x8001; // WM_APP + 1
        private const uint TrayIconId = 1;

        private readonly NativeMethods.WndProcDelegate _wndProc;
        private readonly string _className;
        private readonly IntPtr _hInstance;
        private readonly IntPtr _hwnd;
        private readonly uint _wmTaskbarCreated;
        private readonly List<MenuEntry> _menuEntries = new();
        private readonly List<IntPtr> _ownedMenus = new();

        private Drawing.Icon _icon;
        private IntPtr _iconHandle;
        private IntPtr _rootMenu;
        private IntPtr _fontRegular;
        private IntPtr _fontPrimary;
        private bool _recordingBadge;
        private bool _lastScreenshotOnly;
        private bool _iconAdded;
        private bool _disposed;

        // BGR COLORREF constants matching the original dark slate + blue palette.
        private const uint ColorSurfaceBg = 0x001C1814;     // RGB(20, 24, 28)
        private const uint ColorHoverFill = 0x00302820;     // RGB(32, 40, 48)
        private const uint ColorHoverBorder = 0x00D8882B;   // RGB(43, 136, 216)
        private const uint ColorSeparator = 0x00362E28;     // RGB(40, 46, 54)
        private const uint ColorTextNormal = 0x00F2EFEC;    // RGB(236, 239, 242)
        private const uint ColorTextSelected = 0x00FFFFFF;  // White
        private const uint ColorTextDisabled = 0x00867E78;  // RGB(120, 126, 134)
        private const uint ColorChevronNormal = 0x00BAB2AA; // RGB(170, 178, 186)

        private sealed class MenuEntry
        {
            public string Text { get; init; } = "";
            public bool IsSeparator { get; init; }
            public bool IsPrimary { get; init; }
            public bool HasSubMenu { get; init; }
            public bool IsRtl { get; init; }
            public int MeasuredWidth { get; set; }
            public int MeasuredHeight { get; set; }
            public Action? OnClick { get; init; }
        }

        public TrayIconManager()
        {
            EnableDarkMenuTheme();
            _icon = CreateIcon(out _iconHandle, false);
            _lastScreenshotOnly = SettingsService.Instance.Current.ScreenshotOnlyMode;

            _wndProc = WndProc;
            _hInstance = NativeMethods.GetModuleHandleW(null);
            _className = $"CryoUtils_Tray_{Guid.NewGuid():N}";
            _wmTaskbarCreated = NativeMethods.RegisterWindowMessageW("TaskbarCreated");

            var wc = new NativeMethods.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = _hInstance,
                lpszClassName = _className
            };

            if (NativeMethods.RegisterClassExW(ref wc) != 0)
            {
                // Zero-size unshown top-level tool window receives WM_TASKBARCREATED when Explorer restarts
                // without initializing WPF DirectX surfaces.
                _hwnd = NativeMethods.CreateWindowExW(
                    NativeMethods.WS_EX_TOOLWINDOW_STYLE,
                    _className,
                    "CryoUtilsTrayHost",
                    NativeMethods.WS_POPUP,
                    0, 0, 0, 0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    _hInstance,
                    IntPtr.Zero);
            }

            CreateMenuFonts();
            RebuildMenu();
            AddOrUpdateNotifyIcon(add: true, LocalizationService.Get("Tray_Tooltip"));

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            TopmostService.Instance.StateChanged += OnTopmostStateChanged;
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
            CryoUtils.Services.Recording.ReplayService.Instance.StateChanged += OnReplayStateChanged;
        }

        private static void EnableDarkMenuTheme()
        {
            try
            {
                NativeMethods.SetPreferredAppMode(2); // ForceDark
                NativeMethods.FlushMenuThemes();
            }
            catch
            {
                // Older Windows builds may not expose ordinal 135/136; WM_DRAWITEM still paints dark items.
            }
        }

        private double GetDpiScale()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return 1.0;
            }

            uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }

        private int ScalePx(int px96) => Math.Max(1, (int)Math.Round(px96 * GetDpiScale()));

        private void CreateMenuFonts()
        {
            DeleteMenuFonts();
            double scale = GetDpiScale();
            int fontHeight = -(int)Math.Round(12.67 * scale); // ~9.5 pt at 96 DPI
            _fontRegular = NativeMethods.CreateFontW(
                fontHeight, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0,
                NativeMethods.CLEARTYPE_QUALITY, 0, "Segoe UI");
            _fontPrimary = NativeMethods.CreateFontW(
                fontHeight, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0,
                NativeMethods.CLEARTYPE_QUALITY, 0, "Segoe UI");
        }

        private void DeleteMenuFonts()
        {
            if (_fontRegular != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(_fontRegular);
                _fontRegular = IntPtr.Zero;
            }

            if (_fontPrimary != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(_fontPrimary);
                _fontPrimary = IntPtr.Zero;
            }
        }

        private void AddOrUpdateNotifyIcon(bool add, string tooltip)
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            string clippedTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
            var nid = new NativeMethods.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = TrayIconId,
                uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
                uCallbackMessage = WmTrayIcon,
                hIcon = _iconHandle != IntPtr.Zero ? _iconHandle : _icon.Handle,
                szTip = clippedTip,
                szInfo = "",
                szInfoTitle = ""
            };

            if (add || !_iconAdded)
            {
                _iconAdded = NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref nid);
            }
            else
            {
                NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref nid);
            }
        }

        private void RemoveNotifyIcon()
        {
            if (!_iconAdded || _hwnd == IntPtr.Zero)
            {
                return;
            }

            var nid = new NativeMethods.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = TrayIconId,
                szTip = "",
                szInfo = "",
                szInfoTitle = ""
            };
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref nid);
            _iconAdded = false;
        }

        internal enum TrayPrimaryOutcome
        {
            None,
            Capture,
            ShowDashboard
        }

        internal static TrayPrimaryOutcome ResolvePrimaryAction(
            bool screenshotOnlyMode,
            string? trayLeftClickAction,
            bool isDoubleClick)
        {
            bool captureOnLeftClick = screenshotOnlyMode ||
                string.Equals(trayLeftClickAction, "Capture", StringComparison.OrdinalIgnoreCase);

            if (captureOnLeftClick)
            {
                // Suppress dashboard launch on double-click so rapid tray clicks capture instead of opening the window.
                return isDoubleClick ? TrayPrimaryOutcome.None : TrayPrimaryOutcome.Capture;
            }

            return TrayPrimaryOutcome.ShowDashboard;
        }

        private static void HandlePrimaryClick(bool isDoubleClick)
        {
            MemoryTrimService.PauseForInteraction();
            var settings = SettingsService.Instance.Current;
            TrayPrimaryOutcome outcome = ResolvePrimaryAction(
                settings.ScreenshotOnlyMode,
                settings.TrayLeftClickAction,
                isDoubleClick);

            var app = App.CurrentApp;
            if (app == null || outcome == TrayPrimaryOutcome.None)
            {
                return;
            }

            if (outcome == TrayPrimaryOutcome.Capture)
            {
                app.StartCapture();
            }
            else
            {
                app.ShowDashboard();
            }
        }

        private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WmTrayIcon)
            {
                uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
                switch (mouseMsg)
                {
                    case NativeMethods.WM_LBUTTONDBLCLK:
                        Invoke(() => HandlePrimaryClick(isDoubleClick: true));
                        break;
                    case NativeMethods.WM_LBUTTONUP:
                        Invoke(() => HandlePrimaryClick(isDoubleClick: false));
                        break;
                    case NativeMethods.WM_RBUTTONUP:
                    case NativeMethods.WM_CONTEXTMENU:
                        MemoryTrimService.PauseForInteraction();
                        ShowContextMenu();
                        MemoryTrimService.ScheduleTrim(1000);
                        break;
                }
                return IntPtr.Zero;
            }

            if (_wmTaskbarCreated != 0 && msg == _wmTaskbarCreated)
            {
                _iconAdded = false;
                AddOrUpdateNotifyIcon(
                    add: true,
                    _recordingBadge
                        ? LocalizationService.Get("Tray_RecordingTooltip")
                        : LocalizationService.Get("Tray_Tooltip"));
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WM_MEASUREITEM)
            {
                var mis = Marshal.PtrToStructure<NativeMethods.MEASUREITEMSTRUCT>(lParam);
                if (mis.CtlType == NativeMethods.ODT_MENU)
                {
                    int idx = mis.itemData.ToInt32();
                    if (idx >= 0 && idx < _menuEntries.Count)
                    {
                        var entry = _menuEntries[idx];
                        mis.itemWidth = (uint)Math.Max(1, entry.MeasuredWidth);
                        mis.itemHeight = (uint)Math.Max(1, entry.MeasuredHeight);
                        Marshal.StructureToPtr(mis, lParam, false);
                        return new IntPtr(1);
                    }
                }
            }

            if (msg == NativeMethods.WM_DRAWITEM)
            {
                var dis = Marshal.PtrToStructure<NativeMethods.DRAWITEMSTRUCT>(lParam);
                if (dis.CtlType == NativeMethods.ODT_MENU)
                {
                    int idx = dis.itemData.ToInt32();
                    if (idx >= 0 && idx < _menuEntries.Count)
                    {
                        DrawMenuItem(dis, _menuEntries[idx]);
                        return new IntPtr(1);
                    }
                }
            }

            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            if (_hwnd == IntPtr.Zero || _rootMenu == IntPtr.Zero)
            {
                return;
            }

            CreateMenuFonts();
            RebuildMenu();

            NativeMethods.GetCursorPos(out var pt);
            NativeMethods.SetForegroundWindow(_hwnd);
            uint cmd = NativeMethods.TrackPopupMenuEx(
                _rootMenu,
                NativeMethods.TPM_LEFTALIGN | NativeMethods.TPM_BOTTOMALIGN | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
                pt.X,
                pt.Y,
                _hwnd,
                IntPtr.Zero);
            NativeMethods.PostMessageW(_hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (cmd > 0)
            {
                int idx = (int)cmd - 1;
                if (idx >= 0 && idx < _menuEntries.Count)
                {
                    var onClick = _menuEntries[idx].OnClick;
                    if (onClick != null)
                    {
                        Invoke(onClick);
                    }
                }
            }
        }

        private void RebuildMenu()
        {
            DestroyCurrentMenus();

            bool rtl = LocalizationService.Instance.IsArabic;
            bool screenshotOnly = SettingsService.Instance.Current.ScreenshotOnlyMode;
            _lastScreenshotOnly = screenshotOnly;

            _rootMenu = NativeMethods.CreatePopupMenu();
            _ownedMenus.Add(_rootMenu);
            var mainPending = new List<(IntPtr Menu, MenuEntry Entry, IntPtr SubMenu)>();

            void QueueItem(List<(IntPtr Menu, MenuEntry Entry, IntPtr SubMenu)> bucket, IntPtr targetMenu,
                string text, Action? onClick, bool isPrimary = false, bool isSeparator = false, IntPtr subMenu = default)
            {
                var entry = new MenuEntry
                {
                    Text = text,
                    IsSeparator = isSeparator,
                    IsPrimary = isPrimary,
                    HasSubMenu = subMenu != IntPtr.Zero,
                    IsRtl = rtl,
                    OnClick = onClick
                };
                bucket.Add((targetMenu, entry, subMenu));
            }

            // 1. Primary Open Dashboard item
            QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_OpenDashboard"),
                () => App.CurrentApp?.ShowDashboard(), isPrimary: true);
            QueueItem(mainPending, _rootMenu, "", null, isSeparator: true);

            // 2. Screen capture
            QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_TakeScreenshot"),
                () => App.CurrentApp?.StartCapture(0));
            QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_SnapWindow"),
                () => App.CurrentApp?.SnapActiveWindow(waitForFocusRestore: true));
            QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_RepeatLastRegion"),
                () => App.CurrentApp?.RepeatLastRegion());

            IntPtr delayedMenu = NativeMethods.CreatePopupMenu();
            _ownedMenus.Add(delayedMenu);
            var delayedPending = new List<(IntPtr Menu, MenuEntry Entry, IntPtr SubMenu)>();
            QueueItem(delayedPending, delayedMenu, LocalizationService.Get("Tray_Delay3s"),
                () => App.CurrentApp?.StartCapture(3));
            QueueItem(delayedPending, delayedMenu, LocalizationService.Get("Tray_Delay5s"),
                () => App.CurrentApp?.StartCapture(5));
            QueueItem(delayedPending, delayedMenu, LocalizationService.Get("Tray_Delay10s"),
                () => App.CurrentApp?.StartCapture(10));
            QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_TakeDelayed"),
                null, subMenu: delayedMenu);

            var qrPending = new List<(IntPtr Menu, MenuEntry Entry, IntPtr SubMenu)>();
            if (!screenshotOnly)
            {
                QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_RecordGif"),
                    () => App.CurrentApp?.StartRecording());

                bool replayRunning = CryoUtils.Services.Recording.ReplayService.Instance.IsRunning;
                QueueItem(mainPending, _rootMenu,
                    LocalizationService.Get(replayRunning ? "Tray_StopReplay" : "Tray_StartReplay"),
                    () => App.CurrentApp?.ToggleReplayBuffer());
                if (replayRunning)
                {
                    QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_SaveReplay"),
                        () => App.CurrentApp?.SaveReplay());
                }
                QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_RecorderSettings"),
                    () => App.CurrentApp?.OpenRecorderSettings());

                QueueItem(mainPending, _rootMenu, "", null, isSeparator: true);

                // 3. Local Utilities
                QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_ColorPicker"),
                    () => App.CurrentApp?.OpenColorPicker());

                IntPtr qrMenu = NativeMethods.CreatePopupMenu();
                _ownedMenus.Add(qrMenu);
                QueueItem(qrPending, qrMenu, LocalizationService.Get("Tray_QrScan"),
                    () => App.CurrentApp?.StartCapture());
                QueueItem(qrPending, qrMenu, LocalizationService.Get("Tray_QrGenerate"),
                    () => App.CurrentApp?.ShowDashboard("Tools"));
                QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_QrSuite"),
                    null, subMenu: qrMenu);

                QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_Clipboard"),
                    () => App.CurrentApp?.OpenClipboardHistory());
                QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_Workbench"),
                    () => App.CurrentApp?.OpenWorkbench());

                if (TopmostService.Instance.IsPinned)
                {
                    string title = TopmostService.Instance.PinnedTitle;
                    if (title.Length > 24)
                    {
                        title = title[..21] + "...";
                    }
                    QueueItem(mainPending, _rootMenu, LocalizationService.Format("Tray_UnpinWindow", title),
                        () => TopmostService.Instance.Unpin());
                    QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_ResetPinner"),
                        () => TopmostService.Instance.ForceClearAll());
                }
                else
                {
                    QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_PinWindow"),
                        () => App.CurrentApp?.TogglePinnerFromDashboard());
                }
            }

            if (App.CurrentApp?.HasClickThroughPinWindows() == true)
            {
                QueueItem(mainPending, _rootMenu, "", null, isSeparator: true);
                QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_RestoreGhostPins"),
                    () => App.CurrentApp?.RestoreClickThroughPins());
                QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_ClosePins"),
                    () => App.CurrentApp?.CloseAllPinWindows());
            }

            QueueItem(mainPending, _rootMenu, "", null, isSeparator: true);

            // 4. Lite mode, settings & exit
            QueueItem(mainPending, _rootMenu,
                LocalizationService.Get("Tray_ScreenshotOnly") + (screenshotOnly ? " ✓" : ""),
                () =>
                {
                    var s = SettingsService.Instance.Current;
                    s.ScreenshotOnlyMode = !s.ScreenshotOnlyMode;
                    SettingsService.Instance.Save();
                });
            QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_Settings"),
                () => App.CurrentApp?.ShowDashboard("General"));

            QueueItem(mainPending, _rootMenu, "", null, isSeparator: true);

            QueueItem(mainPending, _rootMenu, LocalizationService.Get("Tray_Exit"),
                () => App.CurrentApp?.ExitApplication());

            FinalizeMenuGroup(mainPending, ScalePx(MainMenuFloorWidth));
            FinalizeMenuGroup(delayedPending, ScalePx(SubMenuFloorWidth));
            if (qrPending.Count > 0)
            {
                FinalizeMenuGroup(qrPending, ScalePx(SubMenuFloorWidth));
            }
        }

        private void FinalizeMenuGroup(
            List<(IntPtr Menu, MenuEntry Entry, IntPtr SubMenu)> items,
            int floorWidth)
        {
            int maxWidth = MeasureGroupWidth(items, floorWidth);
            int padV = ScalePx(12);
            int minRowH = ScalePx(30);
            int sepH = ScalePx(9);

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                IntPtr prev = NativeMethods.SelectObject(screenDc, _fontRegular);
                var rect = new NativeMethods.RECT();
                NativeMethods.DrawTextW(screenDc, "Ag", 2, ref rect, NativeMethods.DT_CALCRECT | NativeMethods.DT_SINGLELINE);
                int rowH = Math.Max(minRowH, rect.Height + padV);
                NativeMethods.SelectObject(screenDc, prev);

                foreach (var (menu, entry, subMenu) in items)
                {
                    int entryIndex = _menuEntries.Count;
                    _menuEntries.Add(entry);

                    int w = maxWidth;
                    int h = entry.IsSeparator ? sepH : rowH;
                    var finalEntry = entry;
                    finalEntry.MeasuredWidth = w;
                    finalEntry.MeasuredHeight = h;

                    var mii = new NativeMethods.MENUITEMINFOW
                    {
                        cbSize = (uint)Marshal.SizeOf<NativeMethods.MENUITEMINFOW>(),
                        fMask = NativeMethods.MIIM_FTYPE | NativeMethods.MIIM_STATE | NativeMethods.MIIM_DATA |
                                (subMenu != IntPtr.Zero ? NativeMethods.MIIM_SUBMENU : NativeMethods.MIIM_ID),
                        fType = NativeMethods.MFT_OWNERDRAW,
                        fState = NativeMethods.MFS_ENABLED,
                        wID = subMenu != IntPtr.Zero ? 0u : (uint)(entryIndex + 1),
                        hSubMenu = subMenu,
                        dwItemData = new IntPtr(entryIndex)
                    };
                    NativeMethods.InsertMenuItemW(menu, uint.MaxValue, true, ref mii);
                }
            }
            finally
            {
                if (screenDc != IntPtr.Zero)
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        private int MeasureGroupWidth(
            List<(IntPtr Menu, MenuEntry Entry, IntPtr SubMenu)> items,
            int floorWidth)
        {
            int maxTextW = 0;
            int reserve = ScalePx(MenuTextHorizontalReserve);
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                return floorWidth;
            }

            try
            {
                foreach (var (_, entry, _) in items)
                {
                    if (entry.IsSeparator || string.IsNullOrEmpty(entry.Text))
                    {
                        continue;
                    }

                    IntPtr font = entry.IsPrimary ? _fontPrimary : _fontRegular;
                    IntPtr prev = NativeMethods.SelectObject(screenDc, font);
                    var r = new NativeMethods.RECT();
                    NativeMethods.DrawTextW(
                        screenDc,
                        entry.Text,
                        entry.Text.Length,
                        ref r,
                        NativeMethods.DT_CALCRECT | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX);
                    NativeMethods.SelectObject(screenDc, prev);
                    if (r.Width > maxTextW)
                    {
                        maxTextW = r.Width;
                    }
                }
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }

            return Math.Max(floorWidth, maxTextW + reserve);
        }

        private void DrawMenuItem(NativeMethods.DRAWITEMSTRUCT dis, MenuEntry entry)
        {
            IntPtr hdc = dis.hDC;
            var bounds = dis.rcItem;

            IntPtr bgBrush = NativeMethods.CreateSolidBrush(ColorSurfaceBg);
            NativeMethods.FillRect(hdc, ref bounds, bgBrush);
            NativeMethods.DeleteObject(bgBrush);

            if (entry.IsSeparator)
            {
                int sepLeft = bounds.Left + ScalePx(10);
                int sepRight = bounds.Right - ScalePx(11);
                int sepY = bounds.Top + (bounds.Height / 2);
                IntPtr sepPen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, 1, ColorSeparator);
                IntPtr prevPen = NativeMethods.SelectObject(hdc, sepPen);
                NativeMethods.MoveToEx(hdc, sepLeft, sepY, IntPtr.Zero);
                NativeMethods.LineTo(hdc, sepRight, sepY);
                NativeMethods.SelectObject(hdc, prevPen);
                NativeMethods.DeleteObject(sepPen);
                return;
            }

            bool selected = (dis.itemState & NativeMethods.ODS_SELECTED) != 0;
            bool disabled = (dis.itemState & (NativeMethods.ODS_DISABLED | NativeMethods.ODS_GRAYED)) != 0;

            if (!disabled && selected)
            {
                int radius = ScalePx(8);
                int pillLeft = bounds.Left + ScalePx(5);
                int pillRight = bounds.Right - ScalePx(6);
                int pillTop = bounds.Top + ScalePx(1);
                int pillBottom = bounds.Bottom - ScalePx(2);

                IntPtr fillBrush = NativeMethods.CreateSolidBrush(ColorHoverFill);
                IntPtr borderPen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, 1, ColorHoverBorder);
                IntPtr prevBrush = NativeMethods.SelectObject(hdc, fillBrush);
                IntPtr prevPen = NativeMethods.SelectObject(hdc, borderPen);
                NativeMethods.RoundRect(hdc, pillLeft, pillTop, pillRight, pillBottom, radius, radius);
                NativeMethods.SelectObject(hdc, prevBrush);
                NativeMethods.SelectObject(hdc, prevPen);
                NativeMethods.DeleteObject(fillBrush);
                NativeMethods.DeleteObject(borderPen);
            }

            int padLead = ScalePx(14);
            int padTrail = ScalePx(32);
            var textRect = new NativeMethods.RECT
            {
                Left = entry.IsRtl ? bounds.Left + padTrail : bounds.Left + padLead,
                Top = bounds.Top,
                Right = Math.Max(bounds.Left + padLead + 20, entry.IsRtl ? bounds.Right - padLead : bounds.Right - padTrail),
                Bottom = bounds.Bottom
            };

            uint textColor = disabled
                ? ColorTextDisabled
                : (selected ? ColorTextSelected : ColorTextNormal);

            NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
            NativeMethods.SetTextColor(hdc, textColor);
            IntPtr font = entry.IsPrimary ? _fontPrimary : _fontRegular;
            IntPtr prevFont = NativeMethods.SelectObject(hdc, font);

            uint flags = NativeMethods.DT_VCENTER
                | NativeMethods.DT_SINGLELINE
                | NativeMethods.DT_END_ELLIPSIS
                | NativeMethods.DT_NOPREFIX;
            if (entry.IsRtl)
            {
                flags |= NativeMethods.DT_RIGHT | NativeMethods.DT_RTLREADING;
            }

            NativeMethods.DrawTextW(hdc, entry.Text, entry.Text.Length, ref textRect, flags);
            NativeMethods.SelectObject(hdc, prevFont);

            if (entry.HasSubMenu)
            {
                int cx = entry.IsRtl ? bounds.Left + ScalePx(16) : bounds.Right - ScalePx(16);
                int cy = bounds.Top + (bounds.Height / 2);
                int dx = ScalePx(3);
                int dy = ScalePx(4);
                uint arrowColor = disabled ? ColorTextDisabled : (selected ? ColorHoverBorder : ColorChevronNormal);
                IntPtr arrowPen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, Math.Max(1, ScalePx(1)), arrowColor);
                IntPtr prevPen = NativeMethods.SelectObject(hdc, arrowPen);
                if (entry.IsRtl)
                {
                    NativeMethods.MoveToEx(hdc, cx + dx, cy - dy, IntPtr.Zero);
                    NativeMethods.LineTo(hdc, cx - dx, cy);
                    NativeMethods.LineTo(hdc, cx + dx, cy + dy + 1);
                }
                else
                {
                    NativeMethods.MoveToEx(hdc, cx - dx, cy - dy, IntPtr.Zero);
                    NativeMethods.LineTo(hdc, cx + dx, cy);
                    NativeMethods.LineTo(hdc, cx - dx, cy + dy + 1);
                }
                NativeMethods.SelectObject(hdc, prevPen);
                NativeMethods.DeleteObject(arrowPen);
            }
        }

        private void DestroyCurrentMenus()
        {
            if (_rootMenu != IntPtr.Zero)
            {
                NativeMethods.DestroyMenu(_rootMenu);
                _rootMenu = IntPtr.Zero;
            }
            else
            {
                foreach (IntPtr hMenu in _ownedMenus)
                {
                    NativeMethods.DestroyMenu(hMenu);
                }
            }

            _ownedMenus.Clear();
            _menuEntries.Clear();
        }

        private void OnLanguageChanged()
        {
            Invoke(RebuildMenu);
        }

        private void OnTopmostStateChanged()
        {
            Invoke(RebuildMenu);
        }

        private void OnSettingsChanged()
        {
            if (SettingsService.Instance.Current.ScreenshotOnlyMode != _lastScreenshotOnly)
            {
                Invoke(RebuildMenu);
            }
        }

        private void OnReplayStateChanged()
        {
            Invoke(() =>
            {
                App.CurrentApp?.SyncRecordingBadge();
                RebuildMenu();
            });
        }

        public void SetRecordingBadge(bool active)
        {
            if (_recordingBadge == active)
            {
                return;
            }

            _recordingBadge = active;
            var replacement = CreateIcon(out IntPtr replacementHandle, active);
            var previous = _icon;
            var previousHandle = _iconHandle;
            _icon = replacement;
            _iconHandle = replacementHandle;

            AddOrUpdateNotifyIcon(
                add: false,
                active
                    ? LocalizationService.Get("Tray_RecordingTooltip")
                    : LocalizationService.Get("Tray_Tooltip"));

            previous.Dispose();
            if (previousHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(previousHandle);
            }
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

        private static Drawing.Icon CreateIcon(out IntPtr handle, bool recordingBadge)
        {
            handle = IntPtr.Zero;
            try
            {
                var resource = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/Images/CryoUtils-Tray.ico"));
                if (resource != null)
                {
                    using var stream = resource.Stream;
                    int traySize = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
                    traySize = Math.Clamp(traySize > 0 ? traySize : 16, 16, 48);
                    var packaged = new Drawing.Icon(stream, new Drawing.Size(traySize, traySize));
                    if (!recordingBadge)
                    {
                        return packaged;
                    }

                    using (packaged)
                    using (var packagedBitmap = packaged.ToBitmap())
                    using (var packagedGraphics = Drawing.Graphics.FromImage(packagedBitmap))
                    {
                        packagedGraphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(229, 83, 75));
                        using var border = new Drawing.Pen(Drawing.Color.FromArgb(20, 24, 28), 1.5f);
                        int size = Math.Max(5, packagedBitmap.Width / 3);
                        packagedGraphics.FillEllipse(brush, packagedBitmap.Width - size, 0, size, size);
                        packagedGraphics.DrawEllipse(border, packagedBitmap.Width - size, 0, size, size);
                        handle = packagedBitmap.GetHicon();
                        using var packagedTemp = Drawing.Icon.FromHandle(handle);
                        return (Drawing.Icon)packagedTemp.Clone();
                    }
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
                using var pen = new Drawing.Pen(Drawing.Color.FromArgb(43, 136, 216), 2.2f)
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
                using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(43, 136, 216));
                graphics.FillEllipse(dot, 13, 13, 6, 6);
                if (recordingBadge)
                {
                    using var badge = new Drawing.SolidBrush(Drawing.Color.FromArgb(229, 83, 75));
                    graphics.FillEllipse(badge, 23, 1, 8, 8);
                }
            }

            handle = bitmap.GetHicon();
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            TopmostService.Instance.StateChanged -= OnTopmostStateChanged;
            SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
            CryoUtils.Services.Recording.ReplayService.Instance.StateChanged -= OnReplayStateChanged;

            RemoveNotifyIcon();
            DestroyCurrentMenus();
            DeleteMenuFonts();
            _icon.Dispose();
            if (_iconHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }

            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_hwnd);
            }

            NativeMethods.UnregisterClassW(_className, _hInstance);
        }
    }
}
