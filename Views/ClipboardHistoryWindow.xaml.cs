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
        private bool _loadingSettings;

        public ClipboardHistoryWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            _service.Changed += RefreshList;
            Closed += (_, _) => _service.Changed -= RefreshList;
            Loaded += (_, _) =>
            {
                LoadSettings();
                RefreshList();
                TxtSearch.Focus();
                Activate();
            };
        }

        private void LoadSettings()
        {
            _loadingSettings = true;
            ChkPersistence.IsChecked = SettingsService.Instance.Current.ClipboardPersistenceEnabled;
            int minutes = SettingsService.Instance.Current.ClipboardExpirationMinutes;
            foreach (ComboBoxItem item in CmbExpiry.Items)
            {
                if (item.Tag is string tag && int.TryParse(tag, out int value) && value == minutes)
                {
                    CmbExpiry.SelectedItem = item;
                    break;
                }
            }
            if (CmbExpiry.SelectedItem == null)
            {
                CmbExpiry.SelectedIndex = 0;
            }
            _loadingSettings = false;
        }

        private void RefreshList()
        {
            string query = TxtSearch.Text.Trim();
            IEnumerable<ClipItem> items = _service.Items;
            if (!string.IsNullOrEmpty(query))
            {
                items = items.Where(item =>
                    (item.Text != null && item.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
                    item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            }

            string filter = (CmbFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
            items = filter switch
            {
                "Text" => items.Where(item => item.Text != null && !item.IsSnippet),
                "Image" => items.Where(item => item.Image != null),
                "Snippets" => items.Where(item => item.IsSnippet),
                "Pinned" => items.Where(item => item.IsPinned),
                _ => items
            };

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

        private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshList();
        }

        private void BtnAddSnippet_Click(object sender, RoutedEventArgs e)
        {
            _service.AddSnippet(TxtSnippetName.Text, TxtSnippetText.Text);
            TxtSnippetName.Clear();
            TxtSnippetText.Clear();
            RefreshList();
        }

        private async void BtnOcrClip_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not ClipItem item || item.Image == null)
            {
                return;
            }

            try
            {
                string text = await OcrService.RecognizeAsync(item.Image);
                if (string.IsNullOrWhiteSpace(text))
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_OcrEmpty"));
                    return;
                }

                Clipboard.SetText(text);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Format("Error_Body", ex.Message));
            }
        }

        private void Persistence_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings)
            {
                return;
            }

            SettingsService.Instance.Current.ClipboardPersistenceEnabled = ChkPersistence.IsChecked == true;
            SettingsService.Instance.Save();
            _service.SetPersistence(ChkPersistence.IsChecked == true);
        }

        private void Expiry_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings || CmbExpiry.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            if (int.TryParse(item.Tag as string, out int minutes))
            {
                SettingsService.Instance.Current.ClipboardExpirationMinutes = minutes;
                SettingsService.Instance.Save();
            }
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
            menu.Items.Add(new Separator());
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformBase64Encode"), TextTransformType.Base64Encode);
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformBase64Decode"), TextTransformType.Base64Decode);
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformJsonFormat"), TextTransformType.JsonFormat);
            AddTransformMenuItem(menu, item, LocalizationService.Get("Clip_TransformJsonMinify"), TextTransformType.JsonMinify);

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
                    var preview = new ClipboardTransformWindow(item.Text, transformed);
                    preview.ShowDialog();
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

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
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
