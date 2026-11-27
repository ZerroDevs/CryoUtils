using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Views;

namespace CryoUtils.Services
{
    public sealed class RecentCapture : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public byte[]? CompressedPng { get; set; }
        public BitmapSource Thumbnail { get; init; } = null!;
        public string? Path { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.Now;
        public string Source { get; init; } = "";
        public long FileSize { get; init; }
        public int PixelWidth { get; init; }
        public int PixelHeight { get; init; }

        public BitmapSource? ResolveFullImage()
        {
            if (CompressedPng != null && CompressedPng.Length > 0)
            {
                try
                {
                    return ImageIO.DecodeBytes(CompressedPng);
                }
                catch
                {
                }
            }

            if (!string.IsNullOrEmpty(Path) && File.Exists(Path))
            {
                try
                {
                    return ImageProcessingService.LoadWpfBitmap(Path);
                }
                catch
                {
                }
            }

            return null;
        }

        public bool IsVideo => HasExtension(".mp4");
        public bool IsRecording => IsVideo || HasExtension(".gif");
        public bool HasKindLabel => IsRecording;

        public string KindLabel => IsVideo
            ? "MP4"
            : HasExtension(".gif")
                ? "GIF"
                : "";

        public string CreatedDisplay => CreatedAt.ToString("MMM d · HH:mm");

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
                if (PixelWidth > 0 && PixelHeight > 0)
                {
                    return $"{PixelWidth} × {PixelHeight}";
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

        private bool HasExtension(string extension) =>
            !string.IsNullOrEmpty(Path) &&
            string.Equals(System.IO.Path.GetExtension(Path), extension, StringComparison.OrdinalIgnoreCase);

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
            bool savedOnDisk = !string.IsNullOrEmpty(path) && File.Exists(path);
            // Store compact compressed PNG bytes in RAM only when no saved file backs the capture on disk.
            byte[]? retainedPng = savedOnDisk ? null : TryEncodeCompressedPng(image);

            var entry = new RecentCapture
            {
                CompressedPng = retainedPng,
                Thumbnail = thumb,
                Path = path,
                CreatedAt = DateTime.Now,
                Source = source,
                FileSize = size,
                PixelWidth = image.PixelWidth,
                PixelHeight = image.PixelHeight
            };

            Items.Insert(0, entry);
            while (Items.Count > 10)
            {
                Items.RemoveAt(Items.Count - 1);
            }
            // Keep at most 2 unsaved compressed PNG buffers in RAM.
            int retainedCount = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].CompressedPng != null)
                {
                    retainedCount++;
                    if (retainedCount > 2)
                    {
                        Items[i].CompressedPng = null;
                    }
                }
            }

            if (savedOnDisk)
            {
                AllTimeItems.Insert(0, new RecentCapture
                {
                    CompressedPng = null,
                    Thumbnail = thumb,
                    Path = path,
                    CreatedAt = entry.CreatedAt,
                    Source = source,
                    FileSize = size,
                    PixelWidth = image.PixelWidth,
                    PixelHeight = image.PixelHeight
                });
            }
        }

        private static byte[]? TryEncodeCompressedPng(BitmapSource image)
        {
            try
            {
                return ImageIO.EncodePng(image);
            }
            catch
            {
                return null;
            }
        }

        public async Task ReloadAllTimeAsync(CancellationToken cancellationToken = default)
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
                cancellationToken.ThrowIfCancellationRequested();

                if (existingByPath.TryGetValue(file.FullName, out var cached) && cached.FileSize == file.Length)
                {
                    next.Add(cached);
                    continue;
                }

                var fileToLoad = file;
                var thumb = await Task.Run(() => LoadThumbnail(fileToLoad), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (thumb == null)
                {
                    continue;
                }

                next.Add(new RecentCapture
                {
                    CompressedPng = null,
                    Thumbnail = thumb,
                    Path = file.FullName,
                    CreatedAt = file.LastWriteTime,
                    Source = LocalizationService.Get("Nav_Capture"),
                    FileSize = file.Length
                });
            }

            cancellationToken.ThrowIfCancellationRequested();
            AllTimeItems.Clear();
            foreach (var item in next)
            {
                AllTimeItems.Add(item);
            }
        }

        public void ReleaseAllTimeCache()
        {
            if (AllTimeItems.Count > 0)
            {
                AllTimeItems.Clear();
            }
        }

        public void ReleaseBackgroundCache(bool screenshotOnly)
        {
            ReleaseAllTimeCache();
            foreach (var item in Items)
            {
                if (!string.IsNullOrEmpty(item.Path))
                {
                    item.CompressedPng = null;
                }
            }

            if (screenshotOnly)
            {
                // Keep only the latest unsaved session capture in RAM when Screenshot-Only returns to tray.
                while (Items.Count > 2)
                {
                    Items.RemoveAt(Items.Count - 1);
                }
            }
        }

        private static BitmapSource? LoadThumbnail(FileInfo file)
        {
            try
            {
                if (file.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    string tempThumb = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        $"cryo_thumb_{Guid.NewGuid():N}.jpg");
                    try
                    {
                        MediaConversionService.SaveThumbnailAsync(file.FullName, tempThumb).GetAwaiter().GetResult();
                        if (File.Exists(tempThumb))
                        {
                            var bmp = ImageIO.LoadThumbnail(tempThumb, 180);
                            if (bmp != null)
                            {
                                return bmp;
                            }
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        try { if (File.Exists(tempThumb)) File.Delete(tempThumb); } catch { }
                    }

                    return ImageIO.CreateVideoThumbnailPlaceholder();
                }

                return ImageIO.LoadThumbnail(file.FullName, 180);
            }
            catch
            {
                return null;
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
