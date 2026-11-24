using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Views;

namespace CryoUtils.Services
{
    public sealed class RecentCapture : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public BitmapSource? Image { get; init; }
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
        public ObservableCollection<RecentCapture> AllTimeItems { get; } = new();

        public void Add(BitmapSource image, string? path, string source)
        {
            var thumb = CreateThumbnail(image);
            long size = TryGetFileSize(path);

            var entry = new RecentCapture
            {
                Image = image,
                Thumbnail = thumb,
                Path = path,
                Source = source,
                FileSize = size
            };

            Items.Insert(0, entry);
            while (Items.Count > 10)
            {
                Items.RemoveAt(Items.Count - 1);
            }

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                AllTimeItems.Insert(0, new RecentCapture
                {
                    Image = image,
                    Thumbnail = thumb,
                    Path = path,
                    CreatedAt = entry.CreatedAt,
                    Source = source,
                    FileSize = size
                });
            }
        }

        public void ReloadAllTime()
        {
            var existingByPath = new Dictionary<string, RecentCapture>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Items.Concat(AllTimeItems))
            {
                if (!string.IsNullOrEmpty(item.Path) && !existingByPath.ContainsKey(item.Path))
                {
                    existingByPath[item.Path] = item;
                }
            }

            var files = ScreenCaptureService.GetSavedCaptureFiles();
            var next = new List<RecentCapture>(files.Count);

            foreach (var file in files)
            {
                if (existingByPath.TryGetValue(file.FullName, out var cached) && cached.FileSize == file.Length)
                {
                    next.Add(cached);
                    continue;
                }

                BitmapSource? thumb = null;
                try
                {
                    if (file.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                    {
                        thumb = ImageIO.CreateVideoThumbnailPlaceholder();
                    }
                    else
                    {
                        thumb = ImageIO.LoadThumbnail(file.FullName, 180);
                    }
                }
                catch
                {
                }

                if (thumb == null)
                {
                    continue;
                }

                next.Add(new RecentCapture
                {
                    Image = null,
                    Thumbnail = thumb,
                    Path = file.FullName,
                    CreatedAt = file.LastWriteTime,
                    Source = LocalizationService.Get("Nav_Capture"),
                    FileSize = file.Length
                });
            }

            AllTimeItems.Clear();
            foreach (var item in next)
            {
                AllTimeItems.Add(item);
            }
        }

        public void Remove(RecentCapture capture)
        {
            Items.Remove(capture);
            AllTimeItems.Remove(capture);
            if (!string.IsNullOrEmpty(capture.Path))
            {
                RemoveByPath(Items, capture.Path);
                RemoveByPath(AllTimeItems, capture.Path);
            }
        }

        public void NotifyFilesDeleted(IEnumerable<string> paths)
        {
            var pathSet = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            if (pathSet.Count == 0)
            {
                return;
            }

            void PruneOnUi()
            {
                RemoveMatching(Items, pathSet);
                RemoveMatching(AllTimeItems, pathSet);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                PruneOnUi();
            }
            else
            {
                dispatcher.BeginInvoke(new Action(PruneOnUi));
            }
        }

        private static BitmapSource CreateThumbnail(BitmapSource image)
        {
            int thumbWidth = Math.Min(180, image.PixelWidth);
            double scale = thumbWidth / (double)Math.Max(1, image.PixelWidth);
            var thumb = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            thumb.Freeze();
            return thumb;
        }

        private static long TryGetFileSize(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return 0;
            }

            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        private static void RemoveByPath(ObservableCollection<RecentCapture> list, string path)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (string.Equals(list[i].Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                }
            }
        }

        private static void RemoveMatching(ObservableCollection<RecentCapture> list, HashSet<string> pathSet)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                string? p = list[i].Path;
                if (!string.IsNullOrEmpty(p) && pathSet.Contains(p))
                {
                    list.RemoveAt(i);
                }
            }
        }
    }
}
