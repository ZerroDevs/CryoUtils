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
            value |= NativeMethods.WS_EX_TRANSPARENT |
                     NativeMethods.WS_EX_TOOLWINDOW |
                     NativeMethods.WS_EX_NOACTIVATE |
                     NativeMethods.WS_EX_LAYERED;
            NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE, new IntPtr(value));
        }

        public void SetBounds(NativeMethods.RECT bounds)
        {
            _handle = new WindowInteropHelper(this).Handle;
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
