using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class ClipboardHistoryWindow : Window
    {
        private readonly ClipboardService _service = ClipboardService.Instance;

        public ClipboardHistoryWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            _service.Changed += RefreshList;
            Closed += (_, _) => _service.Changed -= RefreshList;
            Loaded += (_, _) =>
            {
                RefreshList();
                TxtSearch.Focus();
                Activate();
            };
        }

        private void RefreshList()
        {
            string query = TxtSearch.Text.Trim();
            IEnumerable<ClipItem> items = _service.Items;
            if (!string.IsNullOrEmpty(query))
            {
                items = items.Where(item =>
                    item.Text != null &&
                    item.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            }

            items = items.OrderByDescending(item => item.IsPinned).ThenByDescending(item => item.CreatedAt);
            var list = items.ToList();
            ClipList.ItemsSource = list;
            bool empty = list.Count == 0;
            EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ClipList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshList();
        }

        private void BtnCopyClip_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ClipItem item)
            {
                CopyItem(item);
            }
        }

        private void BtnDeleteClip_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ClipItem item)
            {
                _service.Remove(item);
            }
        }

        private void BtnPinClip_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ClipItem item)
            {
                _service.TogglePin(item);
                RefreshList();
            }
        }

        private void BtnTransformClip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not ClipItem item || string.IsNullOrEmpty(item.Text))
            {
                return;
            }

            var menu = new ContextMenu();
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformUpper"), TextTransformType.Uppercase);
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformLower"), TextTransformType.Lowercase);
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformTitle"), TextTransformType.TitleCase);
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformCamel"), TextTransformType.CamelCase);
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformSnake"), TextTransformType.SnakeCase);
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformKebab"), TextTransformType.KebabCase);
            menu.Items.Add(new Separator());
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformTrim"), TextTransformType.Trim);
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformUrlEncode"), TextTransformType.UrlEncode);
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformUrlDecode"), TextTransformType.UrlDecode);

            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AddTransformMenuItem(ContextMenu menu, ClipItem item, string header, TextTransformType type)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) =>
            {
                if (item.Text != null)
                {
                    string transformed = ClipboardService.TransformText(item.Text, type);
                    Clipboard.SetText(transformed);
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
                    Close();
                }
            };
            menu.Items.Add(mi);
        }

        private void ClipList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ClipList.SelectedItem is ClipItem item)
            {
                CopyItem(item);
            }
        }

        private void CopyItem(ClipItem item)
        {
            _service.CopyItem(item);
            (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
            Close();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _service.Clear();
            RefreshList();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }
    }
}
