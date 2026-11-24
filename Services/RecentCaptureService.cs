using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public sealed class RecentCapture
    {
        public BitmapSource Image { get; init; } = null!;
        public BitmapSource Thumbnail { get; init; } = null!;
        public string? Path { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.Now;
        public string Source { get; init; } = "";
        public long FileSize { get; init; }
        public string DisplayName => string.IsNullOrEmpty(Path)
            ? Source
            : System.IO.Path.GetFileName(Path);
        public string SizeText => FileSize > 0 ? ImageIO.FormatBytes(FileSize) : Source;
    }

    public sealed class RecentCaptureService
    {
        private static RecentCaptureService? _instance;
        public static RecentCaptureService Instance => _instance ??= new RecentCaptureService();

        public ObservableCollection<RecentCapture> Items { get; } = new();

        public void Add(BitmapSource image, string? path, string source)
        {
            int thumbWidth = Math.Min(180, image.PixelWidth);
            double scale = thumbWidth / (double)Math.Max(1, image.PixelWidth);
            var thumb = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            thumb.Freeze();

            long size = 0;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    size = new FileInfo(path).Length;
                }
                catch
                {
                }
            }

            Items.Insert(0, new RecentCapture
            {
                Image = image,
                Thumbnail = thumb,
                Path = path,
                Source = source,
                FileSize = size
            });

            while (Items.Count > 10)
            {
                Items.RemoveAt(Items.Count - 1);
            }
        }

        public void Remove(RecentCapture capture)
        {
            Items.Remove(capture);
        }
    }
}
