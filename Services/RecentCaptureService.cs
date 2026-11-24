using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Views;

namespace CryoUtils.Services
{
    public sealed class RecentCapture : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public BitmapSource Image { get; init; } = null!;
        public BitmapSource Thumbnail { get; init; } = null!;
        public string? Path { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.Now;
        public string Source { get; init; } = "";
        public long FileSize { get; init; }

        public string DisplayName => !string.IsNullOrEmpty(Path)
            ? System.IO.Path.GetFileName(Path)
            : (!string.IsNullOrEmpty(Source) ? Source : LocalizationService.Get("Nav_Capture"));

        public string SizeText
        {
            get
            {
                if (FileSize > 0)
                {
                    return ImageIO.FormatBytes(FileSize);
                }
                if (Image != null)
                {
                    return $"{Image.PixelWidth} × {Image.PixelHeight}";
                }
                return Source;
            }
        }

        private bool _isConfirmingDelete;
        public bool IsConfirmingDelete
        {
            get => _isConfirmingDelete;
            set
            {
                if (_isConfirmingDelete != value)
                {
                    _isConfirmingDelete = value;
                    OnPropertyChanged(nameof(IsConfirmingDelete));
                    OnPropertyChanged(nameof(DeleteIcon));
                    OnPropertyChanged(nameof(DeleteIconBrush));
                    OnPropertyChanged(nameof(DeleteBackground));
                    OnPropertyChanged(nameof(DeleteBorderBrush));
                    OnPropertyChanged(nameof(DeleteToolTip));
                }
            }
        }

        public Geometry DeleteIcon => IsConfirmingDelete ? IconData.Check : IconData.Trash;

        public Brush DeleteIconBrush => IsConfirmingDelete
            ? ThemeService.GetBrush("DangerTextBrush")
            : ThemeService.GetBrush("TextSecondaryBrush");

        public Brush DeleteBackground => IsConfirmingDelete
            ? ThemeService.GetBrush("DangerSoftBackgroundBrush")
            : ThemeService.GetBrush("PanelAltBackgroundBrush");

        public Brush DeleteBorderBrush => IsConfirmingDelete
            ? ThemeService.GetBrush("DangerTextBrush")
            : ThemeService.GetBrush("BorderStrongBrush");

        public string DeleteToolTip => IsConfirmingDelete
            ? LocalizationService.Get("Recent_ConfirmDelete")
            : LocalizationService.Get("Recent_Delete");

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
