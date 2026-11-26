using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CryoUtils.Services;
using Forms = System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace CryoUtils.Views
{
    public sealed class ShelfItem
    {
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public bool IsFolder { get; init; }
        public long Size { get; init; }

        public string Display => $"{Name}  ·  {(IsFolder ? LocalizationService.Get("Shelf_Folder") : ImageIO.FormatBytes(Size))}";
        public override string ToString() => Path;
    }

    public partial class FileShelfWindow : Window
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".jfif",
            ".pdf", ".mp4", ".mov", ".mkv", ".avi", ".webm"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".jfif"
        };

        private readonly ObservableCollection<ShelfItem> _items = new();
        private Point _dragStart;
        private ShelfItem? _dragItem;

        public FileShelfWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            ShelfList.ItemsSource = _items;
            _items.CollectionChanged += (_, _) =>
            {
                EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            EmptyHint.Visibility = Visibility.Visible;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                AddPaths(paths);
            }
            e.Handled = true;
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            int added = 0;
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                bool folder = Directory.Exists(path);
                if (!folder && !File.Exists(path))
                {
                    continue;
                }
                if (!folder && !SupportedExtensions.Contains(Path.GetExtension(path)))
                {
                    continue;
                }
                if (_items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                long size = 0;
                try
                {
                    size = folder ? 0 : new FileInfo(path).Length;
                }
                catch
                {
                }
                _items.Add(new ShelfItem
                {
                    Path = path,
                    Name = folder ? new DirectoryInfo(path).Name : Path.GetFileName(path),
                    IsFolder = folder,
                    Size = size
                });
                added++;
            }

            TxtStatus.Text = added > 0
                ? LocalizationService.Format("Shelf_Added", added)
                : LocalizationService.Get("Shelf_NothingAdded");
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = LocalizationService.Get("Shelf_FileFilter")
            };
            if (dialog.ShowDialog(this) == true)
            {
                AddPaths(dialog.FileNames);
            }
        }

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                AddPaths(new[] { dialog.SelectedPath });
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in ShelfList.SelectedItems.Cast<ShelfItem>().ToList())
            {
                _items.Remove(item);
            }
            TxtStatus.Text = LocalizationService.Get("Shelf_Ready");
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _items.Clear();
            TxtStatus.Text = LocalizationService.Get("Shelf_Ready");
        }

        private void BtnCopyPaths_Click(object sender, RoutedEventArgs e)
        {
            var paths = ShelfList.SelectedItems.Count > 0
                ? ShelfList.SelectedItems.Cast<ShelfItem>().Select(item => item.Path).ToList()
                : _items.Select(item => item.Path).ToList();
            if (paths.Count == 0)
            {
                return;
            }

            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, paths));
                TxtStatus.Text = LocalizationService.Get("Shelf_PathsCopied");
            }
            catch
            {
            }
        }

        private async void BtnCompress_Click(object sender, RoutedEventArgs e)
        {
            var images = GetImagePaths();
            if (images.Count == 0)
            {
                TxtStatus.Text = LocalizationService.Get("Shelf_NoImages");
                return;
            }

            TxtStatus.Text = LocalizationService.Get("Shelf_Busy");
            var job = FileActionQueueService.Instance.Add(
                images,
                FileActionKind.Compress,
                "PNG",
                OverwritePolicy.Rename,
                dryRun: false);
            await FileActionQueueService.Instance.RunAsync();
            TxtStatus.Text = job.Message.Length > 0
                ? job.Message
                : LocalizationService.Get("Shelf_CompressDone");
        }

        private async void BtnConvert_Click(object sender, RoutedEventArgs e)
        {
            var images = GetImagePaths();
            if (images.Count == 0)
            {
                TxtStatus.Text = LocalizationService.Get("Shelf_NoImages");
                return;
            }

            string target = (CmbFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "PNG";
            TxtStatus.Text = LocalizationService.Get("Shelf_Busy");
            var job = FileActionQueueService.Instance.Add(
                images,
                FileActionKind.Convert,
                target,
                OverwritePolicy.Rename,
                dryRun: false);
            await FileActionQueueService.Instance.RunAsync();
            TxtStatus.Text = job.Message.Length > 0
                ? job.Message
                : LocalizationService.Get("Shelf_ConvertDone");
        }

        private async void BtnMergePdf_Click(object sender, RoutedEventArgs e)
        {
            var paths = ShelfList.SelectedItems.Count > 0
                ? ShelfList.SelectedItems.Cast<ShelfItem>().Select(item => item.Path).ToList()
                : _items.Select(item => item.Path).ToList();
            var pdfs = paths.Where(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
            var images = paths.Where(path => ImageExtensions.Contains(Path.GetExtension(path))).ToList();
            if (pdfs.Count == 0 && images.Count == 0)
            {
                TxtStatus.Text = LocalizationService.Get("Shelf_NoImages");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Shelf_MergePdf"),
                Filter = "PDF|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                FileName = "CryoUtils_shelf_merge.pdf",
                InitialDirectory = SettingsService.Instance.EffectiveSaveFolder
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                TxtStatus.Text = LocalizationService.Get("Shelf_Busy");
                if (pdfs.Count > 0)
                {
                    await PdfToolsService.MergePdfsAsync(pdfs, dialog.FileName);
                }
                else
                {
                    await Task.Run(() => PdfToolsService.ImagesToPdf(images, dialog.FileName));
                }
                TxtStatus.Text = LocalizationService.Format("Shelf_PdfSaved", dialog.FileName);
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
            }
        }

        private List<string> GetImagePaths()
        {
            var items = ShelfList.SelectedItems.Count > 0
                ? ShelfList.SelectedItems.Cast<ShelfItem>()
                : _items;
            return items
                .Where(item => !item.IsFolder && ImageExtensions.Contains(Path.GetExtension(item.Path)))
                .Select(item => item.Path)
                .ToList();
        }

        private void ShelfList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(ShelfList);
            _dragItem = (e.OriginalSource as DependencyObject) is { } source
                ? FindItem(ShelfList, source)
                : null;
        }

        private void ShelfList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null)
            {
                return;
            }

            var point = e.GetPosition(ShelfList);
            if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var paths = ShelfList.SelectedItems.Count > 0
                ? ShelfList.SelectedItems.Cast<ShelfItem>().Select(item => item.Path).ToArray()
                : new[] { _dragItem.Path };
            _dragItem = null;
            var data = new DataObject(DataFormats.FileDrop, paths);
            DragDrop.DoDragDrop(ShelfList, data, DragDropEffects.Copy);
        }

        private static ShelfItem? FindItem(ListBox list, DependencyObject source)
        {
            while (source != null && source is not ListBoxItem)
            {
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            return (source as ListBoxItem)?.DataContext as ShelfItem;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
