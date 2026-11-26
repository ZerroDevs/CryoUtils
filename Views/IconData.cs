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
        public static Geometry QrCode { get; } = Frozen(CryoIcons.QrCode);
        public static Geometry Contrast { get; } = Frozen(CryoIcons.Contrast);
        public static Geometry Folder { get; } = Frozen(CryoIcons.Folder);
        public static Geometry Trash { get; } = Frozen(CryoIcons.Trash);
        public static Geometry Check { get; } = Frozen(CryoIcons.Check);
        public static Geometry Tools { get; } = Frozen(CryoIcons.Tools);
        public static Geometry Menu { get; } = Frozen(CryoIcons.Menu);
        public static Geometry Record { get; } = Frozen(CryoIcons.Record);
        public static Geometry Play { get; } = Frozen(CryoIcons.Play);
        public static Geometry Edit { get; } = Frozen(CryoIcons.Pen);
        public static Geometry Home { get; } = Frozen(CryoIcons.Home);
        public static Geometry Keyboard { get; } = Frozen(CryoIcons.Keyboard);
        public static Geometry Info { get; } = Frozen(CryoIcons.Info);
        public static Geometry Power { get; } = Frozen(CryoIcons.Power);
        public static Geometry Minimize { get; } = Frozen(CryoIcons.Minimize);
        public static Geometry CloudUpload { get; } = Frozen(CryoIcons.CloudUpload);
        public static Geometry ChevronDown { get; } = Frozen(CryoIcons.ChevronDown);
        public static Geometry ChevronRight { get; } = Frozen(CryoIcons.ChevronRight);

        private static Geometry Frozen(string data)
        {
            var geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }
    }
}
