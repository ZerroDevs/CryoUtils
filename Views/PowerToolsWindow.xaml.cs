using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CryoUtils.Models;
using CryoUtils.Services;
using WinForms = System.Windows.Forms;

namespace CryoUtils.Views
{
    public partial class PowerToolsWindow : Window
    {
        private readonly CaptureLibraryService _library = CaptureLibraryService.Instance;
        private readonly WorkflowService _workflows = WorkflowService.Instance;
        private readonly UploadHistoryService _uploads = UploadHistoryService.Instance;
        private readonly FileActionQueueService _fileActions = FileActionQueueService.Instance;

        private TextBox _libSearch = null!;
        private TextBox _libTag = null!;
        private ComboBox _libType = null!;
        private CheckBox _libFavorite = null!;
        private ListBox _libList = null!;
        private Border _libEmpty = null!;

        private ListBox _workflowList = null!;
        private TextBox _wfName = null!;
        private TextBox _wfApp = null!;
        private TextBox _wfTemplate = null!;
        private TextBox _wfResize = null!;
        private CheckBox _wfMaster = null!;
        private CheckBox _wfEnabled = null!;
        private CheckBox _wfSave = null!;
        private CheckBox _wfCopy = null!;
        private CheckBox _wfOcr = null!;
        private CheckBox _wfUpload = null!;
        private CheckBox _wfResizeAction = null!;
        private string? _editingWorkflowId;

        private CheckBox _cloudEnabled = null!;
        private ComboBox _uploadProvider = null!;
        private ComboBox _localExpiry = null!;
        private ListBox _uploadHistory = null!;
        private TextBlock _consentStatus = null!;

        private ListBox _jobList = null!;
        private ComboBox _jobKind = null!;
        private ComboBox _jobFormat = null!;
        private ComboBox _jobOverwrite = null!;
        private CheckBox _jobDryRun = null!;
        private Image _jobPreview = null!;

        private WrapPanel _palettePanel = null!;
        private WrapPanel _blindPanel = null!;
        private ComboBox _blindness = null!;
        private ListBox _pairList = null!;
        private TextBox _pairName = null!;
        private TextBox _pairForeground = null!;
        private TextBox _pairBackground = null!;
        private List<PaletteColor> _palette = new();

        private TextBox _pdfSource = null!;
        private TextBox _pdfOutput = null!;
        private TextBox _pdfRange = null!;
        private TextBox _pdfOrder = null!;
        private TextBox _videoSource = null!;
        private TextBox _videoOutput = null!;
        private TextBox _videoStart = null!;
        private TextBox _videoEnd = null!;
        private TextBox _gifStartFrame = null!;
        private TextBox _gifEndFrame = null!;
        private TextBox _mediaResult = null!;

        private TextBox _logText = null!;
        private ListBox _pinList = null!;
        private Slider _pinOpacity = null!;
        private CheckBox _pinClickThrough = null!;
        private CheckBox _pinRestore = null!;
        private TextBox _pinExclusions = null!;
        private bool _loadingPins;

        public PowerToolsWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;

            Tabs.Items.Add(CreateTab("PowerTools_Library", BuildLibraryTab()));
            Tabs.Items.Add(CreateTab("PowerTools_Workflows", BuildWorkflowTab()));
            Tabs.Items.Add(CreateTab("PowerTools_Privacy", BuildPrivacyTab()));
            Tabs.Items.Add(CreateTab("PowerTools_FileActions", BuildFileActionsTab()));
            Tabs.Items.Add(CreateTab("PowerTools_Color", BuildColorTab()));
            Tabs.Items.Add(CreateTab("PowerTools_Media", BuildMediaTab()));
            Tabs.Items.Add(CreateTab("PowerTools_Pinner", BuildPinnerTab()));
            Tabs.Items.Add(CreateTab("PowerTools_Diagnostics", BuildDiagnosticsTab()));

            Loaded += (_, _) =>
            {
                RefreshLibrary();
                RefreshWorkflows();
                RefreshPrivacy();
                RefreshJobs();
                RefreshPairs();
                RefreshColorBlindness();
                RefreshPins();
                RefreshLog();
            };

            Tabs.SelectionChanged += (_, _) =>
            {
                switch (Tabs.SelectedIndex)
                {
                    case 0: RefreshLibrary(); break;
                    case 1: RefreshWorkflows(); break;
                    case 2: RefreshPrivacy(); break;
                    case 3: RefreshJobs(); break;
                    case 4: RefreshPairs(); RefreshColorBlindness(); break;
                    case 6: RefreshPins(); break;
                    case 7: RefreshLog(); break;
                }
            };

            TopmostService.Instance.StateChanged += RefreshPins;
            _library.Changed += OnLibraryChanged;
            Closed += (_, _) =>
            {
                TopmostService.Instance.StateChanged -= RefreshPins;
                _library.Changed -= OnLibraryChanged;
            };
        }

        private void OnLibraryChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshLibrary));
                return;
            }
            RefreshLibrary();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        public void SelectTab(int index)
        {
            if (index >= 0 && index < Tabs.Items.Count)
            {
                Tabs.SelectedIndex = index;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void SetStatus(string message)
        {
            if (TxtStatus != null)
            {
                TxtStatus.Text = message;
            }
        }

        private TabItem CreateTab(string headerKey, UIElement content) =>
            new()
            {
                Header = LocalizationService.Get(headerKey),
                Content = content
            };

        private ScrollViewer Scroll(UIElement content) =>
            new()
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content,
                Padding = new Thickness(2, 10, 10, 10)
            };

        private static TextBlock Label(string key) =>
            new()
            {
                Text = LocalizationService.Get(key),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeService.GetBrush("TextSecondaryBrush"),
                Margin = new Thickness(0, 8, 0, 3)
            };

        private static TextBlock Muted(string key, string? text = null) =>
            new()
            {
                Text = text ?? LocalizationService.Get(key),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeService.GetBrush("TextMutedBrush"),
                Opacity = 0.75,
                Margin = new Thickness(0, 0, 0, 6)
            };

        private static void SetComboTag(ComboBox combo, string tag)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }

        private TextBox Input(string? text = "", double height = 30, bool multiline = false) =>
            new()
            {
                Text = text ?? "",
                Height = multiline ? 100 : height,
                Padding = new Thickness(7, 4, 7, 4),
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                Style = (Style)FindResource("CryoTextBox")
            };

        private ComboBox Combo(params (string Label, string Tag)[] options)
        {
            var combo = new ComboBox
            {
                Height = 30,
                Style = (Style)FindResource("CryoComboBox")
            };
            foreach (var option in options)
            {
                combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Tag });
            }
            combo.SelectedIndex = 0;
            return combo;
        }

        private CheckBox Check(string key, bool isChecked = false) =>
            new()
            {
                Content = LocalizationService.Get(key),
                IsChecked = isChecked,
                Style = (Style)FindResource("CryoCheckBox"),
                Margin = new Thickness(0, 5, 16, 5)
            };

        private Button Btn(string key, RoutedEventHandler handler, bool primary = false) =>
            new Button
            {
                Content = LocalizationService.Get(key),
                Style = (Style)FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 4, 8, 4)
            }.WithClick(handler);

        private Border Card(UIElement content, string? titleKey = null) =>
            new()
            {
                Style = (Style)FindResource("CardBorder"),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new StackPanel
                {
                    Children =
                    {
                        titleKey == null ? new TextBlock() : new TextBlock
                        {
                            Text = LocalizationService.Get(titleKey),
                            Style = (Style)FindResource("SectionTitle"),
                            Foreground = ThemeService.GetBrush("TextTitleBrush"),
                            Margin = new Thickness(0, 0, 0, 6)
                        },
                        content
                    }
                }
            };

        private Grid Row(params UIElement[] elements)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            for (int i = 0; i < elements.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
                });
                Grid.SetColumn(elements[i], i);
                grid.Children.Add(elements[i]);
            }
            return grid;
        }

        private UIElement BuildLibraryTab()
        {
            var panel = new StackPanel();
            panel.Children.Add(Muted("PowerTools_LibraryHint"));

            _libSearch = Input();
            _libSearch.TextChanged += (_, _) => RefreshLibrary();
            _libSearch.Margin = new Thickness(0, 0, 8, 0);
            _libTag = Input();
            _libTag.TextChanged += (_, _) => RefreshLibrary();
            _libTag.Width = 130;
            _libType = Combo(
                (LocalizationService.Get("PowerTools_LibraryAll"), "All"),
                (LocalizationService.Get("PowerTools_LibraryImages"), "Images"),
                (LocalizationService.Get("PowerTools_LibraryVideos"), "Videos"));
            _libType.Width = 130;
            _libType.Margin = new Thickness(8, 0, 8, 0);
            _libType.SelectionChanged += (_, _) => RefreshLibrary();
            _libFavorite = Check("PowerTools_LibraryFavorites");
            _libFavorite.Checked += (_, _) => RefreshLibrary();
            _libFavorite.Unchecked += (_, _) => RefreshLibrary();

            var toolbar = new Grid { Margin = new Thickness(0, 3, 0, 8) };
            toolbar.ColumnDefinitions.Add(new ColumnDefinition());
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_libSearch, 0);
            Grid.SetColumn(_libType, 1);
            Grid.SetColumn(_libTag, 2);
            Grid.SetColumn(_libFavorite, 3);
            toolbar.Children.Add(_libSearch);
            toolbar.Children.Add(_libType);
            toolbar.Children.Add(_libTag);
            toolbar.Children.Add(_libFavorite);
            panel.Children.Add(Card(toolbar, "PowerTools_LibraryFind"));

            var folderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            folderRow.ColumnDefinitions.Add(new ColumnDefinition());
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var folderText = new TextBlock
            {
                Text = _library.LibraryFolder,
                Style = (Style)FindResource("MutedText"),
                FontFamily = (FontFamily)FindResource("CryoMono"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FlowDirection = FlowDirection.LeftToRight
            };
            Grid.SetColumn(folderText, 0);
            folderRow.Children.Add(folderText);
            var openFolder = Btn("PowerTools_OpenFolder", (_, _) => OpenLibraryFolder());
            Grid.SetColumn(openFolder, 1);
            folderRow.Children.Add(openFolder);
            panel.Children.Add(folderRow);

            _libList = new ListBox
            {
                Style = (Style)FindResource("CryoListView"),
                SelectionMode = SelectionMode.Extended,
                MinHeight = 0,
                MaxHeight = 540,
                ItemTemplate = CreateLibraryCardTemplate(),
                ItemsPanel = new ItemsPanelTemplate(
                    new FrameworkElementFactory(typeof(WrapPanel)))
            };
            _libList.MouseDoubleClick += (_, _) => OpenSelectedLibraryItem();
            _libList.SelectionChanged += (_, _) => UpdateLibrarySelection();
            panel.Children.Add(_libList);

            _libEmpty = new Border
            {
                Style = (Style)FindResource("InsetBorder"),
                Padding = new Thickness(18, 22, 18, 22),
                Visibility = Visibility.Collapsed,
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = LocalizationService.Get("PowerTools_LibraryEmpty"),
                            Foreground = ThemeService.GetBrush("TextSecondaryBrush"),
                            FontSize = 12,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = LocalizationService.Get("PowerTools_LibraryEmptyHint"),
                            Style = (Style)FindResource("MutedText"),
                            Margin = new Thickness(0, 4, 0, 0),
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }
            };
            panel.Children.Add(_libEmpty);

            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            actions.Children.Add(Btn("PowerTools_OpenFile", (_, _) => OpenSelectedLibraryItem()));
            actions.Children.Add(Btn("PowerTools_RevealFile", (_, _) => RevealSelectedLibraryItem()));
            actions.Children.Add(Btn("PowerTools_Favorite", (_, _) => ToggleSelectedFavorite()));
            actions.Children.Add(Btn("PowerTools_ApplyTag", (_, _) => ApplyTagToSelection()));
            actions.Children.Add(Btn("PowerTools_RecycleSelected", (_, _) => RecycleSelectedLibraryItems(), primary: true));
            actions.Children.Add(Btn("PowerTools_Refresh", (_, _) => RefreshLibrary()));
            panel.Children.Add(actions);

            return Scroll(panel);
        }

        private DataTemplate CreateLibraryCardTemplate()
        {
            var root = new FrameworkElementFactory(typeof(Border));
            root.SetValue(FrameworkElement.WidthProperty, 238.0);
            root.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 10));
            root.SetValue(Border.PaddingProperty, new Thickness(7));
            root.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            root.SetValue(Border.BackgroundProperty, ThemeService.GetBrush("InsetBackgroundBrush"));
            root.SetValue(Border.BorderBrushProperty, ThemeService.GetBrush("BorderSubtleBrush"));
            root.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            root.SetBinding(FrameworkElement.ToolTipProperty, new Binding("MetaDisplay"));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            root.AppendChild(stack);

            var thumbFrame = new FrameworkElementFactory(typeof(Border));
            thumbFrame.SetValue(FrameworkElement.HeightProperty, 124.0);
            thumbFrame.SetValue(Border.BackgroundProperty, ThemeService.GetBrush("WindowBackgroundBrush"));
            thumbFrame.SetValue(Border.BorderBrushProperty, ThemeService.GetBrush("BorderSubtleBrush"));
            thumbFrame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            thumbFrame.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            thumbFrame.SetValue(UIElement.ClipToBoundsProperty, true);
            var thumbGrid = new FrameworkElementFactory(typeof(Grid));
            var image = new FrameworkElementFactory(typeof(Image));
            image.SetBinding(Image.SourceProperty, new Binding("Thumbnail"));
            image.SetValue(Image.StretchProperty, Stretch.UniformToFill);
            image.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
            thumbGrid.AppendChild(image);
            var kind = new FrameworkElementFactory(typeof(Border));
            kind.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            kind.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            kind.SetValue(FrameworkElement.MarginProperty, new Thickness(7));
            kind.SetValue(Border.PaddingProperty, new Thickness(6, 2, 6, 2));
            kind.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            kind.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(190, 8, 12, 16)));
            kind.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)));
            kind.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var kindText = new FrameworkElementFactory(typeof(TextBlock));
            kindText.SetBinding(TextBlock.TextProperty, new Binding("KindLabel"));
            kindText.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            kindText.SetValue(TextBlock.FontFamilyProperty, (FontFamily)FindResource("CryoMono"));
            kindText.SetValue(TextBlock.FontSizeProperty, 9.0);
            kindText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            kind.AppendChild(kindText);
            thumbGrid.AppendChild(kind);
            thumbFrame.AppendChild(thumbGrid);
            stack.AppendChild(thumbFrame);

            var titleRow = new FrameworkElementFactory(typeof(Grid));
            titleRow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 7, 0, 0));
            titleRow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding("FileName"));
            title.SetValue(TextBlock.ForegroundProperty, ThemeService.GetBrush("TextPrimaryBrush"));
            title.SetValue(TextBlock.FontSizeProperty, 11.5);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            title.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 22, 0));
            titleRow.AppendChild(title);
            var favorite = new FrameworkElementFactory(typeof(TextBlock));
            favorite.SetBinding(TextBlock.TextProperty, new Binding("FavoriteMark"));
            favorite.SetValue(TextBlock.ForegroundProperty, ThemeService.GetBrush("AccentTextBrush"));
            favorite.SetValue(TextBlock.FontSizeProperty, 13.0);
            favorite.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            titleRow.AppendChild(favorite);
            stack.AppendChild(titleRow);

            var meta = new FrameworkElementFactory(typeof(TextBlock));
            meta.SetBinding(TextBlock.TextProperty, new Binding("MetaDisplay"));
            meta.SetValue(TextBlock.ForegroundProperty, ThemeService.GetBrush("TextMutedBrush"));
            meta.SetValue(TextBlock.FontSizeProperty, 9.5);
            meta.SetValue(TextBlock.MarginProperty, new Thickness(0, 3, 0, 0));
            meta.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            stack.AppendChild(meta);

            var tags = new FrameworkElementFactory(typeof(TextBlock));
            tags.SetBinding(TextBlock.TextProperty, new Binding("TagDisplay"));
            tags.SetValue(TextBlock.ForegroundProperty, ThemeService.GetBrush("AccentTextBrush"));
            tags.SetValue(TextBlock.FontSizeProperty, 9.5);
            tags.SetValue(TextBlock.MarginProperty, new Thickness(0, 3, 0, 0));
            tags.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            stack.AppendChild(tags);

            return new DataTemplate { VisualTree = root };
        }

        private void RefreshLibrary()
        {
            if (_libList == null)
            {
                return;
            }

            string type = (_libType?.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
            var items = _library.Load(
                    _libSearch.Text,
                    _libTag.Text,
                    _libFavorite.IsChecked == true)
                .Where(item => type switch
                {
                    "Videos" => item.IsVideo,
                    "Images" => !item.IsVideo,
                    _ => true
                })
                .ToList();
            foreach (var item in items)
            {
                item.Thumbnail = LoadLibraryThumbnail(item);
            }

            _libList.ItemsSource = items;
            _libList.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            _libEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SetStatus(LocalizationService.Format("PowerTools_LibraryCount", items.Count));
        }

        private static BitmapSource LoadLibraryThumbnail(CaptureLibraryItem item)
        {
            try
            {
                return item.Path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                    ? ImageIO.CreateVideoThumbnailPlaceholder(300, 170)
                    : ImageIO.LoadThumbnail(item.Path, 300);
            }
            catch
            {
                return ImageIO.CreateVideoThumbnailPlaceholder(300, 170);
            }
        }

        private void OpenLibraryFolder()
        {
            try
            {
                string folder = _library.LibraryFolder;
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = true
                });
                SetStatus(LocalizationService.Get("PowerTools_OpenFolderDone"));
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        private IEnumerable<CaptureLibraryItem> SelectedLibraryItems =>
            _libList.SelectedItems.Cast<CaptureLibraryItem>().ToList();

        private void UpdateLibrarySelection()
        {
            if (_libList.SelectedItem is CaptureLibraryItem item)
            {
                SetStatus(item.MetaDisplay);
            }
        }

        private void OpenSelectedLibraryItem()
        {
            foreach (var item in SelectedLibraryItems)
            {
                _library.OpenFile(item.Path);
            }
        }

        private void RevealSelectedLibraryItem()
        {
            foreach (var item in SelectedLibraryItems)
            {
                _library.RevealFile(item.Path);
            }
        }

        private void ToggleSelectedFavorite()
        {
            var selected = SelectedLibraryItems.ToList();
            if (selected.Count == 0)
            {
                return;
            }

            bool favorite = !selected[0].IsFavorite;
            foreach (var item in selected)
            {
                _library.SetFavorite(item.Path, favorite);
            }
            RefreshLibrary();
        }

        private void ApplyTagToSelection()
        {
            var selected = SelectedLibraryItems.ToList();
            if (selected.Count == 0 || string.IsNullOrWhiteSpace(_libTag.Text))
            {
                return;
            }

            var tags = _libTag.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in selected)
            {
                _library.SetTags(item.Path, item.Tags.Concat(tags));
            }
            RefreshLibrary();
        }

        private void RecycleSelectedLibraryItems()
        {
            var selected = SelectedLibraryItems.ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                LocalizationService.Format("PowerTools_RecycleConfirm", selected.Count),
                LocalizationService.Get("PowerTools_RecycleTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var failed = _library.DeleteToRecycleBin(selected.Select(item => item.Path));
            RefreshLibrary();
            SetStatus(failed.Count == 0
                ? LocalizationService.Format("PowerTools_Recycled", selected.Count)
                : LocalizationService.Format("PowerTools_RecycleFailed", failed.Count));
        }

        private UIElement BuildWorkflowTab()
        {
            var panel = new StackPanel();
            panel.Children.Add(Muted("PowerTools_WorkflowHint"));
            _wfMaster = Check("PowerTools_WorkflowsEnabled", SettingsService.Instance.Current.WorkflowsEnabled);
            _wfMaster.Checked += (_, _) => SaveWorkflowMaster();
            _wfMaster.Unchecked += (_, _) => SaveWorkflowMaster();
            panel.Children.Add(_wfMaster);

            _workflowList = new ListBox
            {
                Style = (Style)FindResource("CryoListView"),
                Height = 180,
                DisplayMemberPath = "Name"
            };
            _workflowList.SelectionChanged += (_, _) => LoadSelectedWorkflow();
            panel.Children.Add(_workflowList);

            var actions = new WrapPanel();
            actions.Children.Add(Btn("PowerTools_NewRecipe", (_, _) => ClearWorkflowEditor()));
            actions.Children.Add(Btn("PowerTools_DeleteRecipe", (_, _) => DeleteWorkflow(), primary: false));
            panel.Children.Add(actions);

            var form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition());
            form.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            var right = new StackPanel();
            left.Children.Add(Label("PowerTools_RecipeName"));
            _wfName = Input();
            left.Children.Add(_wfName);
            left.Children.Add(Label("PowerTools_AppFilter"));
            _wfApp = Input();
            left.Children.Add(_wfApp);
            left.Children.Add(Muted("PowerTools_AppFilterHint"));
            left.Children.Add(Label("PowerTools_FileNameTemplate"));
            _wfTemplate = Input("CryoUtils_{date}_{time}");
            left.Children.Add(_wfTemplate);
            left.Children.Add(Label("PowerTools_ResizePercent"));
            _wfResize = Input("100");
            left.Children.Add(_wfResize);

            right.Children.Add(Label("PowerTools_Actions"));
            _wfEnabled = Check("PowerTools_RecipeEnabled", true);
            _wfSave = Check("Workflow_Save", true);
            _wfCopy = Check("Workflow_CopyImage");
            _wfOcr = Check("Workflow_OcrCopy");
            _wfUpload = Check("Workflow_Upload");
            _wfResizeAction = Check("Workflow_Resize");
            right.Children.Add(_wfEnabled);
            right.Children.Add(_wfSave);
            right.Children.Add(_wfCopy);
            right.Children.Add(_wfOcr);
            right.Children.Add(_wfUpload);
            right.Children.Add(_wfResizeAction);
            right.Children.Add(Btn("PowerTools_SaveRecipe", (_, _) => SaveWorkflow(), primary: true));

            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            form.Children.Add(left);
            form.Children.Add(right);
            panel.Children.Add(Card(form, "PowerTools_RecipeEditor"));
            return Scroll(panel);
        }

        private void SaveWorkflowMaster()
        {
            if (_wfMaster == null)
            {
                return;
            }
            SettingsService.Instance.Current.WorkflowsEnabled = _wfMaster.IsChecked == true;
            SettingsService.Instance.Save();
            SetStatus(LocalizationService.Get("PowerTools_SettingsSaved"));
        }

        private void RefreshWorkflows()
        {
            _workflowList.ItemsSource = _workflows.Recipes;
            if (_workflowList.SelectedItem == null)
            {
                ClearWorkflowEditor();
            }
        }

        private void LoadSelectedWorkflow()
        {
            if (_workflowList.SelectedItem is not WorkflowRecipe recipe)
            {
                return;
            }

            _editingWorkflowId = recipe.Id;
            _wfName.Text = recipe.Name;
            _wfApp.Text = recipe.AppFilter;
            _wfTemplate.Text = recipe.FileNameTemplate;
            _wfResize.Text = recipe.ResizePercent.ToString();
            _wfEnabled.IsChecked = recipe.Enabled;
            _wfSave.IsChecked = recipe.Actions.HasFlag(WorkflowAction.Save);
            _wfCopy.IsChecked = recipe.Actions.HasFlag(WorkflowAction.CopyImage);
            _wfOcr.IsChecked = recipe.Actions.HasFlag(WorkflowAction.OcrCopy);
            _wfUpload.IsChecked = recipe.Actions.HasFlag(WorkflowAction.Upload);
            _wfResizeAction.IsChecked = recipe.Actions.HasFlag(WorkflowAction.Resize);
        }

        private void ClearWorkflowEditor()
        {
            _editingWorkflowId = null;
            if (_wfName == null)
            {
                return;
            }
            _wfName.Clear();
            _wfApp.Clear();
            _wfTemplate.Text = "CryoUtils_{date}_{time}";
            _wfResize.Text = "100";
            _wfEnabled.IsChecked = true;
            _wfSave.IsChecked = true;
            _wfCopy.IsChecked = false;
            _wfOcr.IsChecked = false;
            _wfUpload.IsChecked = false;
            _wfResizeAction.IsChecked = false;
            _workflowList.SelectedItem = null;
        }

        private void SaveWorkflow()
        {
            if (string.IsNullOrWhiteSpace(_wfName.Text))
            {
                SetStatus(LocalizationService.Get("PowerTools_NameRequired"));
                return;
            }

            WorkflowAction actions = WorkflowAction.None;
            if (_wfSave.IsChecked == true) actions |= WorkflowAction.Save;
            if (_wfCopy.IsChecked == true) actions |= WorkflowAction.CopyImage;
            if (_wfOcr.IsChecked == true) actions |= WorkflowAction.OcrCopy;
            if (_wfUpload.IsChecked == true) actions |= WorkflowAction.Upload;
            if (_wfResizeAction.IsChecked == true) actions |= WorkflowAction.Resize;
            if (actions == WorkflowAction.None)
            {
                SetStatus(LocalizationService.Get("PowerTools_ActionRequired"));
                return;
            }

            int.TryParse(_wfResize.Text, out int resize);
            var recipe = new WorkflowRecipe
            {
                Id = _editingWorkflowId ?? Guid.NewGuid().ToString("N"),
                Name = _wfName.Text.Trim(),
                Enabled = _wfEnabled.IsChecked == true,
                AppFilter = _wfApp.Text.Trim(),
                FileNameTemplate = string.IsNullOrWhiteSpace(_wfTemplate.Text)
                    ? "CryoUtils_{date}_{time}"
                    : _wfTemplate.Text.Trim(),
                ResizePercent = Math.Clamp(resize <= 0 ? 100 : resize, 10, 100),
                Actions = actions
            };
            _workflows.SaveRecipe(recipe);
            _editingWorkflowId = recipe.Id;
            RefreshWorkflows();
            _workflowList.SelectedItem = _workflows.Recipes.FirstOrDefault(item => item.Id == recipe.Id);
            SetStatus(LocalizationService.Get("PowerTools_RecipeSaved"));
        }

        private void DeleteWorkflow()
        {
            if (_workflowList.SelectedItem is not WorkflowRecipe recipe)
            {
                return;
            }

            _workflows.DeleteRecipe(recipe.Id);
            ClearWorkflowEditor();
            RefreshWorkflows();
        }

        private bool _privacyLoading;

        private UIElement BuildPrivacyTab()
        {
            var panel = new StackPanel();
            panel.Children.Add(Muted("PowerTools_PrivacyHint"));

            var settingsCard = new StackPanel();
            _cloudEnabled = Check("PowerTools_CloudEnabled", SettingsService.Instance.Current.CloudUploadEnabled);
            _cloudEnabled.Checked += (_, _) => SavePrivacySettings();
            _cloudEnabled.Unchecked += (_, _) => SavePrivacySettings();
            settingsCard.Children.Add(_cloudEnabled);

            settingsCard.Children.Add(Label("PowerTools_UploadProvider"));
            _uploadProvider = Combo(
                (LocalizationService.Get("PowerTools_ProviderCatbox"), UploadProviderService.CatboxProvider),
                (LocalizationService.Get("PowerTools_ProviderLocal"), UploadProviderService.LocalProvider));
            _uploadProvider.SelectionChanged += (_, _) => SavePrivacySettings();
            settingsCard.Children.Add(_uploadProvider);

            settingsCard.Children.Add(Label("PowerTools_LocalExpiry"));
            _localExpiry = Combo(
                (LocalizationService.Get("PowerTools_Expiry1h"), "1"),
                (LocalizationService.Get("PowerTools_Expiry24h"), "24"),
                (LocalizationService.Get("PowerTools_Expiry72h"), "72"),
                (LocalizationService.Get("PowerTools_Expiry7d"), "168"));
            _localExpiry.SelectionChanged += (_, _) => SavePrivacySettings();
            settingsCard.Children.Add(_localExpiry);

            _consentStatus = new TextBlock
            {
                Style = (Style)FindResource("MutedText"),
                Margin = new Thickness(0, 8, 0, 0)
            };
            settingsCard.Children.Add(_consentStatus);

            var privacyActions = new WrapPanel();
            privacyActions.Children.Add(Btn("PowerTools_LocalOnly", (_, _) => EnableLocalOnly(), primary: true));
            privacyActions.Children.Add(Btn("PowerTools_WithdrawConsent", (_, _) => WithdrawConsent()));
            privacyActions.Children.Add(Btn("PowerTools_OpenLocalUploads", (_, _) => _uploads.OpenLocalFolder()));
            settingsCard.Children.Add(privacyActions);
            panel.Children.Add(Card(settingsCard, "PowerTools_PrivacySettings"));

            _uploadHistory = new ListBox
            {
                Style = (Style)FindResource("CryoListView"),
                Height = 300,
                DisplayMemberPath = "HistoryLine"
            };
            panel.Children.Add(Card(_uploadHistory, "PowerTools_UploadHistory"));

            var historyActions = new WrapPanel();
            historyActions.Children.Add(Btn("PowerTools_CopyLink", (_, _) => CopySelectedUpload()));
            historyActions.Children.Add(Btn("PowerTools_OpenLink", (_, _) => OpenSelectedUpload()));
            historyActions.Children.Add(Btn("PowerTools_ForgetUpload", (_, _) => ForgetSelectedUpload()));
            historyActions.Children.Add(Btn("PowerTools_ClearHistory", (_, _) => ClearUploadHistory(), primary: true));
            panel.Children.Add(historyActions);
            panel.Children.Add(Muted("PowerTools_RemoteDeleteNote"));
            return Scroll(panel);
        }

        private void RefreshPrivacy()
        {
            if (_uploadHistory == null)
            {
                return;
            }

            _privacyLoading = true;
            var settings = SettingsService.Instance.Current;
            _uploads.ExpireLocalUploads();
            _cloudEnabled.IsChecked = settings.CloudUploadEnabled;
            SetComboTag(_uploadProvider, settings.UploadProvider);
            SetComboTag(_localExpiry, Math.Clamp(settings.LocalUploadExpiryHours, 1, 720).ToString());
            _consentStatus.Text = settings.CloudUploadConsentAccepted
                ? LocalizationService.Get("PowerTools_ConsentAccepted")
                : LocalizationService.Get("PowerTools_ConsentNotAccepted");
            _uploadHistory.ItemsSource = _uploads.Records;
            _privacyLoading = false;
        }

        private void SavePrivacySettings()
        {
            if (_privacyLoading || _cloudEnabled == null)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            settings.CloudUploadEnabled = _cloudEnabled.IsChecked == true;
            settings.UploadProvider =
                (_uploadProvider.SelectedItem as ComboBoxItem)?.Tag as string ?? UploadProviderService.CatboxProvider;
            if (int.TryParse((_localExpiry.SelectedItem as ComboBoxItem)?.Tag as string, out int hours))
            {
                settings.LocalUploadExpiryHours = hours;
            }
            SettingsService.Instance.Save();
            SetStatus(LocalizationService.Get("PowerTools_SettingsSaved"));
        }

        private void EnableLocalOnly()
        {
            var settings = SettingsService.Instance.Current;
            settings.UploadProvider = UploadProviderService.LocalProvider;
            settings.CloudUploadEnabled = false;
            SettingsService.Instance.Save();
            RefreshPrivacy();
            SetStatus(LocalizationService.Get("PowerTools_LocalOnlyOn"));
        }

        private void WithdrawConsent()
        {
            SettingsService.Instance.Current.CloudUploadConsentAccepted = false;
            SettingsService.Instance.Save();
            RefreshPrivacy();
            SetStatus(LocalizationService.Get("PowerTools_ConsentWithdrawn"));
        }

        private void CopySelectedUpload()
        {
            if (_uploadHistory.SelectedItem is UploadRecord record)
            {
                Clipboard.SetText(record.Url);
                SetStatus(LocalizationService.Get("Common_Copied"));
            }
        }

        private void OpenSelectedUpload()
        {
            if (_uploadHistory.SelectedItem is UploadRecord record)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = record.Url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message);
                }
            }
        }

        private void ForgetSelectedUpload()
        {
            if (_uploadHistory.SelectedItem is UploadRecord record)
            {
                _uploads.Remove(record.Id);
                RefreshPrivacy();
                SetStatus(LocalizationService.Get("PowerTools_UploadForgotten"));
            }
        }

        private void ClearUploadHistory()
        {
            if (MessageBox.Show(
                    this,
                    LocalizationService.Get("PowerTools_ClearHistoryConfirm"),
                    LocalizationService.Get("PowerTools_ClearHistory"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            _uploads.Clear();
            RefreshPrivacy();
        }

        private UIElement BuildFileActionsTab()
        {
            var panel = new StackPanel();
            panel.Children.Add(Muted("PowerTools_FileActionsHint"));

            var controls = new StackPanel();
            var row = new WrapPanel();
            row.Children.Add(Btn("PowerTools_AddImages", (_, _) => AddFileActionImages()));
            row.Children.Add(Btn("PowerTools_AddFolder", (_, _) => AddFileActionFolder()));
            row.Children.Add(Btn("PowerTools_StartQueue", async (_, _) => await RunFileActionsAsync(), primary: true));
            row.Children.Add(Btn("PowerTools_Cancel", (_, _) => _fileActions.Cancel()));
            row.Children.Add(Btn("PowerTools_Retry", async (_, _) => await RetrySelectedJobAsync()));
            row.Children.Add(Btn("PowerTools_ClearCompleted", (_, _) => { _fileActions.ClearCompleted(); RefreshJobs(); }));
            controls.Children.Add(row);

            var options = new WrapPanel();
            _jobKind = Combo(
                (LocalizationService.Get("FileActions_Convert"), "Convert"),
                (LocalizationService.Get("FileActions_Compress"), "Compress"),
                (LocalizationService.Get("FileActions_Recycle"), "Recycle"));
            _jobKind.Width = 140;
            _jobKind.Margin = new Thickness(0, 4, 8, 4);
            options.Children.Add(_jobKind);

            _jobFormat = Combo(("PNG", "PNG"), ("JPG", "JPG"), ("WEBP", "WEBP"));
            _jobFormat.Width = 100;
            _jobFormat.Margin = new Thickness(0, 4, 8, 4);
            options.Children.Add(_jobFormat);

            _jobOverwrite = Combo(
                (LocalizationService.Get("PowerTools_OverwriteRename"), "Rename"),
                (LocalizationService.Get("PowerTools_OverwriteSkip"), "Skip"),
                (LocalizationService.Get("PowerTools_OverwriteOverwrite"), "Overwrite"));
            _jobOverwrite.Width = 150;
            _jobOverwrite.Margin = new Thickness(0, 4, 8, 4);
            options.Children.Add(_jobOverwrite);
            _jobDryRun = Check("PowerTools_DryRun");
            options.Children.Add(_jobDryRun);
            controls.Children.Add(options);
            panel.Children.Add(Card(controls, "PowerTools_FileActionQueue"));

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _jobList = new ListBox
            {
                Style = (Style)FindResource("CryoListView"),
                Height = 340,
                DisplayMemberPath = "DisplayLine"
            };
            _jobList.SelectionChanged += (_, _) => UpdateJobPreview();
            body.Children.Add(_jobList);

            var previewPanel = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            previewPanel.Children.Add(Label("PowerTools_Preview"));
            _jobPreview = new Image { Stretch = Stretch.Uniform, Height = 250 };
            var previewBorder = new Border
            {
                Background = ThemeService.GetBrush("InsetBackgroundBrush"),
                BorderBrush = ThemeService.GetBrush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8),
                Child = _jobPreview
            };
            previewPanel.Children.Add(previewBorder);
            Grid.SetColumn(previewPanel, 1);
            body.Children.Add(previewPanel);
            panel.Children.Add(body);
            return Scroll(panel);
        }

        private void AddFileActionImages()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.jfif|All files|*.*"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            AddJob(dialog.FileNames);
        }

        private void AddFileActionFolder()
        {
            using var dialog = new WinForms.FolderBrowserDialog();
            if (dialog.ShowDialog() != WinForms.DialogResult.OK)
            {
                return;
            }

            try
            {
                AddJob(FileToolsService.CollectSortedImages(dialog.SelectedPath));
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        private void AddJob(IEnumerable<string> paths)
        {
            var kind = Enum.TryParse<FileActionKind>(
                (_jobKind.SelectedItem as ComboBoxItem)?.Tag as string,
                out var parsedKind)
                ? parsedKind
                : FileActionKind.Convert;
            var policy = Enum.TryParse<OverwritePolicy>(
                (_jobOverwrite.SelectedItem as ComboBoxItem)?.Tag as string,
                out var parsedPolicy)
                ? parsedPolicy
                : OverwritePolicy.Rename;
            var job = _fileActions.Add(
                paths,
                kind,
                (_jobFormat.SelectedItem as ComboBoxItem)?.Tag as string ?? "PNG",
                policy,
                _jobDryRun.IsChecked == true);
            RefreshJobs();
            _jobList.SelectedItem = job;
        }

        private async Task RunFileActionsAsync()
        {
            var recycleCount = _fileActions.Jobs
                .Where(job => job.Kind == FileActionKind.Recycle && !job.DryRun &&
                              job.Status is FileActionStatus.Pending or FileActionStatus.Failed)
                .Sum(job => job.InputPaths.Count);
            if (recycleCount > 0 && MessageBox.Show(
                    this,
                    LocalizationService.Format("PowerTools_RecycleQueueConfirm", recycleCount),
                    LocalizationService.Get("PowerTools_RecycleTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            await _fileActions.RunAsync();
            RefreshJobs();
        }

        private async Task RetrySelectedJobAsync()
        {
            if (_jobList.SelectedItem is FileActionJob job)
            {
                await _fileActions.RetryAsync(job);
                RefreshJobs();
            }
        }

        private void RefreshJobs()
        {
            if (_jobList == null)
            {
                return;
            }
            _jobList.ItemsSource = _fileActions.Jobs;
            UpdateJobPreview();
        }

        private void UpdateJobPreview()
        {
            if (_jobPreview == null)
            {
                return;
            }

            if (_jobList.SelectedItem is FileActionJob job && job.InputPaths.Count > 0)
            {
                try
                {
                    _jobPreview.Source = ImageIO.LoadThumbnail(job.InputPaths[0], 500);
                }
                catch
                {
                    _jobPreview.Source = null;
                }
            }
            else
            {
                _jobPreview.Source = null;
            }
        }

        private UIElement BuildColorTab()
        {
            var panel = new StackPanel();
            panel.Children.Add(Muted("PowerTools_ColorHint"));

            var actions = new WrapPanel();
            actions.Children.Add(Btn("PowerTools_ExtractImage", (_, _) => ExtractPaletteFromFile()));
            actions.Children.Add(Btn("PowerTools_ExtractScreen", (_, _) => ExtractPaletteFromScreen()));
            actions.Children.Add(Btn("PowerTools_ExportCss", (_, _) => CopyPaletteExport(ColorStudioService.ExportCss(_palette))));
            actions.Children.Add(Btn("PowerTools_ExportJson", (_, _) => CopyPaletteExport(ColorStudioService.ExportJson(_palette))));
            actions.Children.Add(Btn("PowerTools_ExportTailwind", (_, _) => CopyPaletteExport(ColorStudioService.ExportTailwind(_palette))));
            panel.Children.Add(actions);

            _palettePanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
            panel.Children.Add(Card(_palettePanel, "PowerTools_Palette"));

            var blindRow = new StackPanel();
            blindRow.Children.Add(Label("PowerTools_ColorBlindness"));
            _blindness = Combo(
                (LocalizationService.Get("PowerTools_BlindNone"), "None"),
                ("Protanopia", "Protanopia"),
                ("Deuteranopia", "Deuteranopia"),
                ("Tritanopia", "Tritanopia"),
                ("Achromatopsia", "Achromatopsia"));
            _blindness.SelectionChanged += (_, _) => RefreshColorBlindness();
            blindRow.Children.Add(_blindness);
            _blindPanel = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
            blindRow.Children.Add(_blindPanel);
            panel.Children.Add(Card(blindRow, "PowerTools_ColorBlindnessPreview"));

            var pairPanel = new StackPanel();
            pairPanel.Children.Add(Label("PowerTools_PairName"));
            _pairName = Input();
            pairPanel.Children.Add(_pairName);
            var pairColors = new Grid();
            pairColors.ColumnDefinitions.Add(new ColumnDefinition());
            pairColors.ColumnDefinitions.Add(new ColumnDefinition());
            var fgPanel = new StackPanel();
            fgPanel.Children.Add(Label("PowerTools_Foreground"));
            _pairForeground = Input("#FFFFFF");
            fgPanel.Children.Add(_pairForeground);
            var bgPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            bgPanel.Children.Add(Label("PowerTools_Background"));
            _pairBackground = Input("#1E6FAE");
            bgPanel.Children.Add(_pairBackground);
            Grid.SetColumn(fgPanel, 0);
            Grid.SetColumn(bgPanel, 1);
            pairColors.Children.Add(fgPanel);
            pairColors.Children.Add(bgPanel);
            pairPanel.Children.Add(pairColors);
            var pairActions = new WrapPanel();
            pairActions.Children.Add(Btn("PowerTools_SavePair", (_, _) => SaveContrastPair(), primary: true));
            pairActions.Children.Add(Btn("PowerTools_DeletePair", (_, _) => DeleteContrastPair()));
            pairPanel.Children.Add(pairActions);

            _pairList = new ListBox
            {
                Style = (Style)FindResource("CryoListView"),
                Height = 180,
                DisplayMemberPath = "Summary"
            };
            pairPanel.Children.Add(_pairList);
            panel.Children.Add(Card(pairPanel, "PowerTools_ContrastPairs"));
            return Scroll(panel);
        }

        private void ExtractPaletteFromFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.jfif|All files|*.*"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                using var stream = File.OpenRead(dialog.FileName);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                _palette = ColorStudioService.ExtractPalette(frame).ToList();
                RefreshPalette();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        private void ExtractPaletteFromScreen()
        {
            try
            {
                var bitmap = ScreenCaptureService.CaptureRegion(ScreenCaptureService.GetVirtualBounds());
                int longest = Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
                if (longest > 1400)
                {
                    bitmap = ImageIO.Resize(bitmap, 1400.0 / longest);
                }
                _palette = ColorStudioService.ExtractPalette(bitmap).ToList();
                RefreshPalette();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        private void RefreshPalette()
        {
            if (_palettePanel == null)
            {
                return;
            }

            _palettePanel.Children.Clear();
            foreach (var color in _palette)
            {
                _palettePanel.Children.Add(CreateSwatch(color.Hex, color.Hex));
            }
            if (_palette.Count == 0)
            {
                _palettePanel.Children.Add(Muted("PowerTools_NoPalette"));
            }
            RefreshColorBlindness();
        }

        private Border CreateSwatch(string hex, string label, string? backgroundHex = null)
        {
            var color = ColorStudioService.ParseColor(backgroundHex ?? hex) ?? Colors.Transparent;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var stack = new StackPanel();
            stack.Children.Add(new Border
            {
                Width = 72,
                Height = 42,
                CornerRadius = new CornerRadius(3),
                Background = brush,
                BorderBrush = ThemeService.GetBrush("BorderBrush"),
                BorderThickness = new Thickness(1)
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = ThemeService.GetBrush("TextMutedBrush"),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            return new Border { Margin = new Thickness(0, 0, 8, 8), Child = stack };
        }

        private void RefreshColorBlindness()
        {
            if (_blindPanel == null)
            {
                return;
            }

            _blindPanel.Children.Clear();
            var type = Enum.TryParse<ColorBlindnessType>(
                (_blindness.SelectedItem as ComboBoxItem)?.Tag as string,
                out var parsed)
                ? parsed
                : ColorBlindnessType.None;
            foreach (var color in _palette)
            {
                string simulated = ColorStudioService.Simulate(color.Hex, type);
                _blindPanel.Children.Add(CreateSwatch(color.Hex, simulated, simulated));
            }
        }

        private void CopyPaletteExport(string content)
        {
            if (_palette.Count == 0)
            {
                SetStatus(LocalizationService.Get("PowerTools_NoPalette"));
                return;
            }
            Clipboard.SetText(content);
            SetStatus(LocalizationService.Get("PowerTools_ExportCopied"));
        }

        private void RefreshPairs()
        {
            if (_pairList != null)
            {
                _pairList.ItemsSource = ColorStudioService.Instance.Pairs;
            }
        }

        private void SaveContrastPair()
        {
            try
            {
                ColorStudioService.Instance.AddPair(_pairName.Text, _pairForeground.Text, _pairBackground.Text);
                _pairName.Clear();
                RefreshPairs();
                SetStatus(LocalizationService.Get("PowerTools_PairSaved"));
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }

        private void DeleteContrastPair()
        {
            if (_pairList.SelectedItem is SavedContrastPair pair)
            {
                ColorStudioService.Instance.RemovePair(pair.Id);
                RefreshPairs();
            }
        }

        private UIElement BuildMediaTab()
        {
            var panel = new StackPanel();
            panel.Children.Add(Muted("PowerTools_MediaHint"));

            var pdf = new StackPanel();
            pdf.Children.Add(Label("PowerTools_PdfSource"));
            var sourceRow = new Grid();
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition());
            sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _pdfSource = Input();
            sourceRow.Children.Add(_pdfSource);
            var browsePdf = Btn("Common_Browse", (_, _) => BrowsePdfSource());
            Grid.SetColumn(browsePdf, 1);
            sourceRow.Children.Add(browsePdf);
            pdf.Children.Add(sourceRow);

            pdf.Children.Add(Label("PowerTools_PdfOutput"));
            var outputRow = new Grid();
            outputRow.ColumnDefinitions.Add(new ColumnDefinition());
            outputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _pdfOutput = Input();
            outputRow.Children.Add(_pdfOutput);
            var browseOutput = Btn("Common_Browse", (_, _) => BrowsePdfOutput());
            Grid.SetColumn(browseOutput, 1);
            outputRow.Children.Add(browseOutput);
            pdf.Children.Add(outputRow);

            var ranges = new Grid();
            ranges.ColumnDefinitions.Add(new ColumnDefinition());
            ranges.ColumnDefinitions.Add(new ColumnDefinition());
            var rangePanel = new StackPanel();
            rangePanel.Children.Add(Label("PowerTools_PageRange"));
            _pdfRange = Input("1-3,5");
            rangePanel.Children.Add(_pdfRange);
            var orderPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            orderPanel.Children.Add(Label("PowerTools_PageOrder"));
            _pdfOrder = Input("3,1,2");
            orderPanel.Children.Add(_pdfOrder);
            Grid.SetColumn(rangePanel, 0);
            Grid.SetColumn(orderPanel, 1);
            ranges.Children.Add(rangePanel);
            ranges.Children.Add(orderPanel);
            pdf.Children.Add(ranges);

            var pdfActions = new WrapPanel();
            pdfActions.Children.Add(Btn("PowerTools_ExtractPages", async (_, _) => await ExtractPdfAsync()));
            pdfActions.Children.Add(Btn("PowerTools_MergePdfs", async (_, _) => await MergePdfsAsync()));
            pdfActions.Children.Add(Btn("PowerTools_SplitPdf", async (_, _) => await SplitPdfAsync()));
            pdfActions.Children.Add(Btn("PowerTools_ReorderPdf", async (_, _) => await ReorderPdfAsync()));
            pdfActions.Children.Add(Btn("PowerTools_RotatePdf", async (_, _) => await RotatePdfAsync()));
            pdfActions.Children.Add(Btn("PowerTools_OcrPdf", async (_, _) => await OcrPdfAsync(), primary: true));
            pdf.Children.Add(pdfActions);
            panel.Children.Add(Card(pdf, "PowerTools_PdfTitle"));

            var video = new StackPanel();
            video.Children.Add(Label("PowerTools_VideoSource"));
            var videoSourceRow = new Grid();
            videoSourceRow.ColumnDefinitions.Add(new ColumnDefinition());
            videoSourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _videoSource = Input();
            videoSourceRow.Children.Add(_videoSource);
            var browseVideo = Btn("Common_Browse", (_, _) => BrowseVideoSource());
            Grid.SetColumn(browseVideo, 1);
            videoSourceRow.Children.Add(browseVideo);
            video.Children.Add(videoSourceRow);

            video.Children.Add(Label("PowerTools_VideoOutput"));
            _videoOutput = Input();
            video.Children.Add(_videoOutput);

            var times = new Grid();
            times.ColumnDefinitions.Add(new ColumnDefinition());
            times.ColumnDefinitions.Add(new ColumnDefinition());
            var startPanel = new StackPanel();
            startPanel.Children.Add(Label("PowerTools_StartSeconds"));
            _videoStart = Input("0");
            startPanel.Children.Add(_videoStart);
            var endPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            endPanel.Children.Add(Label("PowerTools_EndSeconds"));
            _videoEnd = Input("0");
            endPanel.Children.Add(_videoEnd);
            Grid.SetColumn(startPanel, 0);
            Grid.SetColumn(endPanel, 1);
            times.Children.Add(startPanel);
            times.Children.Add(endPanel);
            video.Children.Add(times);

            var gifFrames = new Grid();
            gifFrames.ColumnDefinitions.Add(new ColumnDefinition());
            gifFrames.ColumnDefinitions.Add(new ColumnDefinition());
            var gifStartPanel = new StackPanel();
            gifStartPanel.Children.Add(Label("PowerTools_GifStartFrame"));
            _gifStartFrame = Input("0");
            gifStartPanel.Children.Add(_gifStartFrame);
            var gifEndPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            gifEndPanel.Children.Add(Label("PowerTools_GifEndFrame"));
            _gifEndFrame = Input("0");
            gifEndPanel.Children.Add(_gifEndFrame);
            Grid.SetColumn(gifStartPanel, 0);
            Grid.SetColumn(gifEndPanel, 1);
            gifFrames.Children.Add(gifStartPanel);
            gifFrames.Children.Add(gifEndPanel);
            video.Children.Add(gifFrames);

            var videoActions = new WrapPanel();
            videoActions.Children.Add(Btn("PowerTools_TrimVideo", async (_, _) => await TrimVideoAsync(), primary: true));
            videoActions.Children.Add(Btn("PowerTools_GifRange", async (_, _) => await GifRangeAsync()));
            video.Children.Add(videoActions);
            panel.Children.Add(Card(video, "PowerTools_VideoTitle"));

            _mediaResult = Input("", 130, multiline: true);
            _mediaResult.IsReadOnly = true;
            panel.Children.Add(Card(_mediaResult, "PowerTools_MediaOutput"));
            panel.Children.Add(Muted("PowerTools_MediaLimits"));
            return Scroll(panel);
        }

        private void BrowsePdfSource()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PDF|*.pdf" };
            if (dialog.ShowDialog(this) == true)
            {
                _pdfSource.Text = dialog.FileName;
            }
        }

        private void BrowsePdfOutput()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true
            };
            if (dialog.ShowDialog(this) == true)
            {
                _pdfOutput.Text = dialog.FileName;
            }
        }

        private void BrowseVideoSource()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Videos|*.mp4;*.mov;*.wmv;*.avi;*.mkv;*.webm|All files|*.*"
            };
            if (dialog.ShowDialog(this) == true)
            {
                _videoSource.Text = dialog.FileName;
            }
        }

        private async Task ExtractPdfAsync()
        {
            await RunMediaAsync(async () =>
            {
                string output = EnsurePdfOutput();
                int pages = await PdfToolsService.ExtractPdfPagesAsync(
                    _pdfSource.Text,
                    output,
                    _pdfRange.Text);
                return LocalizationService.Format("PowerTools_PdfPagesDone", pages, output);
            });
        }

        private async Task MergePdfsAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF|*.pdf",
                Multiselect = true
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            await RunMediaAsync(async () =>
            {
                string output = EnsurePdfOutput();
                int pages = await PdfToolsService.MergePdfsAsync(dialog.FileNames, output);
                return LocalizationService.Format("PowerTools_PdfPagesDone", pages, output);
            });
        }

        private async Task SplitPdfAsync()
        {
            using var folder = new WinForms.FolderBrowserDialog();
            if (folder.ShowDialog() != WinForms.DialogResult.OK)
            {
                return;
            }
            await RunMediaAsync(async () =>
            {
                var ranges = _pdfRange.Text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .ToList();
                int parts = await PdfToolsService.SplitPdfAsync(_pdfSource.Text, folder.SelectedPath, ranges);
                return LocalizationService.Format("PowerTools_PdfSplitDone", parts, folder.SelectedPath);
            });
        }

        private async Task ReorderPdfAsync()
        {
            await RunMediaAsync(async () =>
            {
                string output = EnsurePdfOutput();
                int pages = await PdfToolsService.ReorderPdfAsync(_pdfSource.Text, output, _pdfOrder.Text);
                return LocalizationService.Format("PowerTools_PdfPagesDone", pages, output);
            });
        }

        private async Task RotatePdfAsync()
        {
            await RunMediaAsync(async () =>
            {
                string output = EnsurePdfOutput();
                await PdfToolsService.RotatePdfAsync(_pdfSource.Text, output, 90);
                return LocalizationService.Format("PowerTools_PdfRotated", output);
            });
        }

        private async Task OcrPdfAsync()
        {
            await RunMediaAsync(async () =>
            {
                string text = await PdfToolsService.OcrPdfAsync(_pdfSource.Text, _pdfRange.Text);
                _mediaResult.Text = text;
                try { Clipboard.SetText(text); } catch { }
                return LocalizationService.Get("PowerTools_OcrDone");
            });
        }

        private string EnsurePdfOutput()
        {
            if (!string.IsNullOrWhiteSpace(_pdfOutput.Text))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_pdfOutput.Text)) ?? ".");
                return _pdfOutput.Text;
            }
            string path = Path.Combine(
                SettingsService.Instance.EffectiveSaveFolder,
                $"CryoUtils_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            Directory.CreateDirectory(SettingsService.Instance.EffectiveSaveFolder);
            _pdfOutput.Text = path;
            return path;
        }

        private async Task TrimVideoAsync()
        {
            await RunMediaAsync(async () =>
            {
                string output = EnsureVideoOutput();
                double start = ParseDouble(_videoStart.Text);
                double end = ParseDouble(_videoEnd.Text);
                await MediaConversionService.TrimVideoAsync(_videoSource.Text, output, start, end);
                return LocalizationService.Format("PowerTools_VideoDone", output);
            });
        }

        private async Task GifRangeAsync()
        {
            await RunMediaAsync(async () =>
            {
                string output = Path.ChangeExtension(EnsureVideoOutput(), ".gif");
                int fps = Math.Clamp(SettingsService.Instance.Current.GifFps, 5, 30);
                double start = ParseInt(_gifStartFrame.Text) / (double)fps;
                double end = ParseInt(_gifEndFrame.Text) / (double)fps;
                int frames = await MediaConversionService.VideoToGifAsync(
                    _videoSource.Text,
                    output,
                    fps,
                    640,
                    null,
                    default,
                    start,
                    end);
                return LocalizationService.Format("PowerTools_GifDone", frames, output);
            });
        }

        private string EnsureVideoOutput()
        {
            if (!string.IsNullOrWhiteSpace(_videoOutput.Text))
            {
                return _videoOutput.Text;
            }
            string path = Path.Combine(
                SettingsService.Instance.EffectiveSaveFolder,
                $"CryoUtils_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            Directory.CreateDirectory(SettingsService.Instance.EffectiveSaveFolder);
            _videoOutput.Text = path;
            return path;
        }

        private static double ParseDouble(string text) =>
            double.TryParse(text, out double value) ? Math.Max(0, value) : 0;

        private static int ParseInt(string text) =>
            int.TryParse(text, out int value) ? Math.Max(0, value) : 0;

        private async Task RunMediaAsync(Func<Task<string>> operation)
        {
            try
            {
                SetStatus(LocalizationService.Get("PowerTools_Working"));
                string message = await operation();
                SetStatus(message);
            }
            catch (Exception ex)
            {
                DiagnosticsService.Log("Media workbench operation failed", ex);
                SetStatus(ex.Message);
            }
        }

        private UIElement BuildPinnerTab()
        {
            var panel = new StackPanel();
            panel.Children.Add(Muted("PowerTools_PinnerHint"));
            _pinList = new ListBox
            {
                Style = (Style)FindResource("CryoListView"),
                Height = 280,
                DisplayMemberPath = "Display"
            };
            _pinList.SelectionChanged += (_, _) => LoadSelectedPin();
            panel.Children.Add(_pinList);

            var actions = new WrapPanel();
            actions.Children.Add(Btn("PowerTools_UnpinSelected", (_, _) => UnpinSelectedPin()));
            actions.Children.Add(Btn("PowerTools_UnpinAll", (_, _) => TopmostService.Instance.Unpin(), primary: true));
            actions.Children.Add(Btn("PowerTools_Refresh", (_, _) => RefreshPins()));
            panel.Children.Add(actions);

            var appearance = new StackPanel();
            var opacityRow = new Grid();
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            opacityRow.ColumnDefinitions.Add(new ColumnDefinition());
            opacityRow.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("PowerTools_PinOpacity"),
                Style = (Style)FindResource("FieldLabel"),
                VerticalAlignment = VerticalAlignment.Center
            });
            _pinOpacity = new Slider
            {
                Style = (Style)FindResource("CryoSlider"),
                Minimum = 20,
                Maximum = 100,
                Value = 100,
                IsSnapToTickEnabled = true,
                TickFrequency = 5
            };
            _pinOpacity.ValueChanged += (_, _) => ApplySelectedPinAppearance();
            Grid.SetColumn(_pinOpacity, 1);
            opacityRow.Children.Add(_pinOpacity);
            appearance.Children.Add(opacityRow);
            _pinClickThrough = Check("PowerTools_PinClickThrough");
            _pinClickThrough.Checked += (_, _) => ApplySelectedPinAppearance();
            _pinClickThrough.Unchecked += (_, _) => ApplySelectedPinAppearance();
            appearance.Children.Add(_pinClickThrough);
            panel.Children.Add(Card(appearance, "PowerTools_PinAppearance"));

            var optionsCard = new StackPanel();
            _pinRestore = Check("PowerTools_PinRestore", SettingsService.Instance.Current.PinnerRestoreEnabled);
            _pinRestore.Checked += (_, _) => SavePinOptions();
            _pinRestore.Unchecked += (_, _) => SavePinOptions();
            optionsCard.Children.Add(_pinRestore);
            optionsCard.Children.Add(Label("PowerTools_PinExclusions"));
            _pinExclusions = Input(string.Join(Environment.NewLine, SettingsService.Instance.Current.PinnerExclusions), 70, multiline: true);
            optionsCard.Children.Add(_pinExclusions);
            optionsCard.Children.Add(Muted("PowerTools_PinExclusionsHint"));
            optionsCard.Children.Add(Btn("PowerTools_SaveExclusions", (_, _) => SavePinOptions(), primary: true));
            panel.Children.Add(Card(optionsCard, "PowerTools_PinOptions"));
            return Scroll(panel);
        }

        private void RefreshPins()
        {
            if (_pinList == null)
            {
                return;
            }

            _loadingPins = true;
            IntPtr selectedHandle = (_pinList.SelectedItem as PinnedWindowInfo)?.Handle ?? IntPtr.Zero;
            var pins = TopmostService.Instance.PinnedWindows;
            _pinList.ItemsSource = pins;
            if (selectedHandle != IntPtr.Zero)
            {
                _pinList.SelectedItem = pins
                    .FirstOrDefault(item => item.Handle == selectedHandle);
            }
            if (_pinList.SelectedItem == null && _pinList.Items.Count > 0)
            {
                _pinList.SelectedIndex = 0;
            }
            _loadingPins = false;
            LoadSelectedPin();
        }

        private void LoadSelectedPin()
        {
            if (_loadingPins || _pinOpacity == null)
            {
                return;
            }

            _loadingPins = true;
            if (_pinList.SelectedItem is PinnedWindowInfo info)
            {
                _pinOpacity.Value = info.Opacity;
                _pinClickThrough.IsChecked = info.ClickThrough;
            }
            _loadingPins = false;
        }

        private void ApplySelectedPinAppearance()
        {
            if (_loadingPins || _pinList?.SelectedItem is not PinnedWindowInfo info)
            {
                return;
            }

            TopmostService.Instance.SetWindowAppearance(
                info.Handle,
                (int)Math.Round(_pinOpacity.Value),
                _pinClickThrough.IsChecked == true);
        }

        private void UnpinSelectedPin()
        {
            if (_pinList.SelectedItem is PinnedWindowInfo info)
            {
                TopmostService.Instance.UnpinWindow(info.Handle);
                RefreshPins();
            }
        }

        private void SavePinOptions()
        {
            if (_pinRestore == null)
            {
                return;
            }

            var settings = SettingsService.Instance.Current;
            settings.PinnerRestoreEnabled = _pinRestore.IsChecked == true;
            settings.PinnerExclusions = _pinExclusions.Text
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            SettingsService.Instance.Save();
            SetStatus(LocalizationService.Get("PowerTools_SettingsSaved"));
        }

        private UIElement BuildDiagnosticsTab()
        {
            var panel = new StackPanel();
            panel.Children.Add(Muted("PowerTools_DiagnosticsHint"));
            var actions = new WrapPanel();
            actions.Children.Add(Btn("PowerTools_RefreshLog", (_, _) => RefreshLog()));
            actions.Children.Add(Btn("PowerTools_OpenLogFolder", (_, _) => DiagnosticsService.OpenLogFolder()));
            actions.Children.Add(Btn("PowerTools_ClearLog", (_, _) => { DiagnosticsService.ClearLog(); RefreshLog(); }));
            actions.Children.Add(Btn("PowerTools_SafeMode", (_, _) => EnterSafeMode(), primary: true));
            panel.Children.Add(actions);

            _logText = Input("", 470, multiline: true);
            _logText.IsReadOnly = true;
            _logText.FontFamily = (FontFamily)FindResource("CryoMono");
            panel.Children.Add(Card(_logText, "PowerTools_LogViewer"));
            panel.Children.Add(Muted("PowerTools_SafeModeHint"));
            return Scroll(panel);
        }

        private void RefreshLog()
        {
            if (_logText != null)
            {
                _logText.Text = DiagnosticsService.ReadLog();
                _logText.ScrollToEnd();
            }
        }

        private void EnterSafeMode()
        {
            try
            {
                TopmostService.Instance.ForceClearAll();
                ClipboardService.Instance.Clear();
                var settings = SettingsService.Instance.Current;
                settings.PinnedClickThrough = false;
                settings.PinnedOpacity = 100;
                settings.ShowPinnedBorder = false;
                settings.PinnerRestoreEnabled = false;
                SettingsService.Instance.Save();
                DiagnosticsService.Log("Safe mode enabled from Workbench.");
                SetStatus(LocalizationService.Get("PowerTools_SafeModeDone"));
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }
    }

    internal static class ButtonExtensions
    {
        public static Button WithClick(this Button button, RoutedEventHandler handler)
        {
            button.Click += handler;
            return button;
        }
    }
}
