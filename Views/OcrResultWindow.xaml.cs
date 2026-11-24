using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CryoUtils.Services;
using Microsoft.Win32;

namespace CryoUtils.Views
{
    public partial class OcrResultWindow : Window
    {
        private const int MaxColorSwatches = 16;
        private bool _suppressTextChange;
        private string? _filePath;

        public OcrResultWindow(string text)
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            LoadText(text, null);
            Loaded += (_, _) =>
            {
                Activate();
                FitTextHeight();
                TxtResult.Focus();
            };
        }

        private void LoadText(string text, string? path)
        {
            _suppressTextChange = true;
            try
            {
                _filePath = path;
                string content = text ?? "";
                TxtResult.Text = content;

                bool code = TextAnalysisService.LooksLikeCode(content, path);
                ChkCodeMode.IsChecked = code;
                ChkWrap.IsChecked = !code;

                if (string.IsNullOrWhiteSpace(path))
                {
                    TxtSource.Text = LocalizationService.Get("Ocr_FromSelection");
                    TxtSource.ToolTip = null;
                    TxtSource.FlowDirection = LocalizationService.Instance.FlowDirection;
                }
                else
                {
                    TxtSource.Text = Path.GetFileName(path);
                    TxtSource.ToolTip = path;
                    TxtSource.FlowDirection = FlowDirection.LeftToRight;
                }
            }
            finally
            {
                _suppressTextChange = false;
            }

            ApplyTextStyle();
            UpdateTextDetails();
            FitTextHeight();
        }

        private void ApplyTextStyle()
        {
            bool code = ChkCodeMode.IsChecked == true;
            bool wrap = ChkWrap.IsChecked != false;
            TxtResult.FontFamily = (FontFamily)FindResource(code ? "CryoMono" : "SegoeUIVariableText");
            TxtResult.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            TxtResult.HorizontalScrollBarVisibility = wrap
                ? ScrollBarVisibility.Disabled
                : ScrollBarVisibility.Auto;

            bool rtl = !code && ContainsArabic(TxtResult.Text);
            TxtResult.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            TxtResult.TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left;
        }

        private void UpdateTextDetails()
        {
            string text = TxtResult.Text ?? "";
            int lines = text.Length == 0 ? 0 : text.Count(character => character == '\n') + 1;
            TxtMeta.Text = lines == 1
                ? LocalizationService.Format("Ocr_MetaLine", text.Length)
                : LocalizationService.Format("Ocr_MetaLines", lines, text.Length);

            ColorSwatchesPanel.Children.Clear();
            var colors = TextAnalysisService.FindColorCodes(text);
            ColorPanel.Visibility = colors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var color in colors.Take(MaxColorSwatches))
            {
                ColorSwatchesPanel.Children.Add(CreateColorButton(color));
            }
        }

        private Button CreateColorButton(TextColorCode color)
        {
            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(color.Color),
                BorderThickness = new Thickness(1),
                BorderBrush = ThemeService.GetBrush("BorderStrongBrush"),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new TextBlock
            {
                Text = color.Text,
                FontFamily = (FontFamily)FindResource("CryoMono"),
                FontSize = 10.5,
                Foreground = ThemeService.GetBrush("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatch);
            content.Children.Add(label);

            var button = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Content = content,
                Padding = new Thickness(6, 3, 7, 3),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = color.Text
            };
            AutomationProperties.SetName(button, color.Text);
            button.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(color.Text);
                    (Application.Current as App)?.ShowToast(
                        LocalizationService.Format("Color_Copied", color.Text));
                }
                catch
                {
                }
            };
            return button;
        }

        private void FitTextHeight()
        {
            string text = TxtResult.Text ?? "";
            bool noWrap = ChkWrap.IsChecked == false;
            int visualLines;

            if (noWrap)
            {
                visualLines = Math.Max(1, text.Split('\n').Length);
            }
            else
            {
                double contentWidth = Math.Max(280, Width - 76);
                int charsPerLine = Math.Max(16, (int)(contentWidth / (TxtResult.FontSize * 0.56)));
                visualLines = 0;
                foreach (string line in text.Split('\n'))
                {
                    string clean = line.TrimEnd('\r');
                    visualLines += Math.Max(1, (int)Math.Ceiling(clean.Length / (double)charsPerLine));
                }
                visualLines = Math.Max(1, visualLines);
            }

            double lineHeight = TxtResult.FontSize * (ChkCodeMode.IsChecked == true ? 1.6 : 1.7);
            TxtResult.Height = Math.Clamp(visualLines * lineHeight + 28, 72, 360);
        }

        private static bool ContainsArabic(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (char character in text)
            {
                if (character >= '\u0600' && character <= '\u06FF')
                {
                    return true;
                }
            }

            return false;
        }

        private void ChkCodeMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressTextChange)
            {
                return;
            }

            if (ChkCodeMode.IsChecked == true)
            {
                _suppressTextChange = true;
                ChkWrap.IsChecked = false;
                _suppressTextChange = false;
            }

            ApplyTextStyle();
            FitTextHeight();
        }

        private void ChkWrap_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressTextChange)
            {
                return;
            }

            ApplyTextStyle();
            FitTextHeight();
        }

        private void TxtResult_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextChange)
            {
                return;
            }

            ApplyTextStyle();
            UpdateTextDetails();
            FitTextHeight();
        }

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Get("Common_Open"),
                Filter = LocalizationService.Get("Ocr_FileFilter"),
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
            {
                LoadFile(dialog.FileName);
            }
        }

        private void LoadFile(string path)
        {
            try
            {
                string text = File.ReadAllText(path);
                LoadText(text, path);
            }
            catch
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Ocr_OpenFailed"));
            }
        }

        private void BtnSaveFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Common_SaveAs"),
                Filter = LocalizationService.Get("Ocr_FileFilter"),
                DefaultExt = ".txt",
                AddExtension = true,
                FilterIndex = 2,
                FileName = string.IsNullOrWhiteSpace(_filePath)
                    ? "CryoUtils_text.txt"
                    : Path.GetFileName(_filePath)
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                File.WriteAllText(dialog.FileName, TxtResult.Text, new UTF8Encoding(false));
                _filePath = dialog.FileName;
                TxtSource.Text = Path.GetFileName(dialog.FileName);
                TxtSource.ToolTip = dialog.FileName;
                TxtSource.FlowDirection = FlowDirection.LeftToRight;
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Ocr_Saved"));
            }
            catch
            {
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Error"));
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtResult.Text);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
                Close();
            }
            catch
            {
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_PreviewDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                LoadFile(files[0]);
            }
            e.Handled = true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.S)
            {
                BtnSaveFile_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.O)
            {
                BtnOpenFile_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }
}
