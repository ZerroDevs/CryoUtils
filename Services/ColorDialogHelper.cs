using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CryoUtils.Services
{
    /// <summary>Opens the native Windows color picker without loading System.Windows.Forms.</summary>
    public static class ColorDialogHelper
    {
        private static readonly int[] CustomColors = CreateDefaultCustomColors();

        private static int[] CreateDefaultCustomColors()
        {
            var colors = new int[16];
            Array.Fill(colors, 0x00FFFFFF);
            return colors;
        }

        internal static uint ColorToColorRef(Color color) =>
            color.R | ((uint)color.G << 8) | ((uint)color.B << 16);

        internal static Color ColorRefToColor(uint colorRef) =>
            Color.FromRgb(
                (byte)(colorRef & 0xFF),
                (byte)((colorRef >> 8) & 0xFF),
                (byte)((colorRef >> 16) & 0xFF));

        internal static string ColorRefToHex(uint colorRef)
        {
            var c = ColorRefToColor(colorRef);
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        public static string? PickColorHex(Window? owner, string? currentHex)
        {
            var current = AnnotationRenderer.ParseColor(currentHex);
            return PickColorHex(owner, current);
        }

        public static string? PickColorHex(Window? owner, Color current)
        {
            IntPtr hwndOwner = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
            var handle = GCHandle.Alloc(CustomColors, GCHandleType.Pinned);
            try
            {
                var cc = new NativeMethods.CHOOSECOLORW
                {
                    lStructSize = (uint)Marshal.SizeOf<NativeMethods.CHOOSECOLORW>(),
                    hwndOwner = hwndOwner,
                    rgbResult = ColorToColorRef(current),
                    lpCustColors = handle.AddrOfPinnedObject(),
                    Flags = NativeMethods.CC_RGBINIT | NativeMethods.CC_FULLOPEN | NativeMethods.CC_ANYCOLOR
                };

                if (!NativeMethods.ChooseColorW(ref cc))
                {
                    return null;
                }

                return ColorRefToHex(cc.rgbResult);
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
