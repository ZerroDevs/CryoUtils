using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public sealed class PinnedBorderWindow : Window
    {
        private IntPtr _handle;

        public PinnedBorderWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            IsHitTestVisible = false;
            Focusable = false;
            Content = new Border
            {
                BorderBrush = ThemeService.GetBrush("PinnedBorderBrush"),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _handle = new WindowInteropHelper(this).Handle;
            IntPtr style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE);
            long value = style.ToInt64();
            // WS_EX_TRANSPARENT (click-through), WS_EX_TOOLWINDOW (no taskbar/alt-tab), WS_EX_NOACTIVATE.
            // Do NOT add WS_EX_LAYERED here: WPF's AllowsTransparency already manages the layered ULW surface.
            // Re-adding WS_EX_LAYERED via SetWindowLongPtr resets the alpha channel and causes an opaque white box.
            value |= NativeMethods.WS_EX_TRANSPARENT |
                     NativeMethods.WS_EX_TOOLWINDOW |
                     NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE, new IntPtr(value));
        }

        public void SetBounds(NativeMethods.RECT bounds)
        {
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            if (dpiX <= 0) dpiX = 1.0;
            if (dpiY <= 0) dpiY = 1.0;

            Left = (bounds.Left - 2) / dpiX;
            Top = (bounds.Top - 2) / dpiY;
            Width = Math.Max(10, (bounds.Width + 4) / dpiX);
            Height = Math.Max(10, (bounds.Height + 4) / dpiY);

            if (_handle == IntPtr.Zero)
            {
                _handle = new WindowInteropHelper(this).Handle;
            }

            if (_handle != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(
                    _handle,
                    NativeMethods.HWND_TOPMOST,
                    bounds.Left - 2,
                    bounds.Top - 2,
                    bounds.Width + 4,
                    bounds.Height + 4,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            }
        }
    }
}
