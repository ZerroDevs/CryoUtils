using System.Windows.Media;

namespace CryoUtils.Views
{
    /// <summary>Shared vector geometry for XAML bindings.</summary>
    public static class IconData
    {
        public static Geometry Capture { get; } = Frozen(CryoIcons.Rectangle);
        public static Geometry Droplet { get; } = Frozen(CryoIcons.Droplet);
        public static Geometry Pin { get; } = Frozen(CryoIcons.Pin);
        public static Geometry Compress { get; } = Frozen(CryoIcons.Compress);
        public static Geometry Clipboard { get; } = Frozen(CryoIcons.Clipboard);
        public static Geometry Settings { get; } = Frozen(CryoIcons.Settings);
        public static Geometry Copy { get; } = Frozen(CryoIcons.Copy);
        public static Geometry Save { get; } = Frozen(CryoIcons.Save);
        public static Geometry Close { get; } = Frozen(CryoIcons.Close);

        private static Geometry Frozen(string data)
        {
            var geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }
    }
}
