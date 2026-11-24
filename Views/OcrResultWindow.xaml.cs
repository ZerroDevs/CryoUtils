using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
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
                SetEditorText(content);

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
            TxtResult.Document.PageWidth = wrap ? double.NaN : 10000;
            TxtResult.HorizontalScrollBarVisibility = wrap
                ? ScrollBarVisibility.Disabled
                : ScrollBarVisibility.Auto;

            bool rtl = !code && ContainsArabic(GetEditorText());
            TxtResult.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            TxtResult.Document.TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left;
            ApplyCodeHighlighting();
        }

        private string GetEditorText()
        {
            var range = new TextRange(TxtResult.Document.ContentStart, TxtResult.Document.ContentEnd);
            string text = range.Text.Replace("\r\n", "\n").Replace('\r', '\n');
            return text.EndsWith('\n') ? text[..^1] : text;
        }

        private void SetEditorText(string? text)
        {
            bool previous = _suppressTextChange;
            _suppressTextChange = true;
            try
            {
                TxtResult.Document.Blocks.Clear();
                string value = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
                foreach (string line in value.Split('\n'))
                {
                    TxtResult.Document.Blocks.Add(new Paragraph(new Run(line))
                    {
                        Margin = new Thickness(0)
                    });
                }
            }
            finally
            {
                _suppressTextChange = previous;
            }
        }

        private void ApplyCodeHighlighting()
        {
            bool previous = _suppressTextChange;
            int caretOffset = GetTextOffset(TxtResult.CaretPosition);
            int selectionStartOffset = GetTextOffset(TxtResult.Selection.Start);
            int selectionEndOffset = GetTextOffset(TxtResult.Selection.End);
            bool hasSelection = !TxtResult.Selection.IsEmpty;
            _suppressTextChange = true;
            try
            {
                var start = TxtResult.Document.ContentStart;
                var end = TxtResult.Document.ContentEnd;
                new TextRange(start, end).ApplyPropertyValue(
                    TextElement.ForegroundProperty,
                    ThemeService.GetBrush("TextPrimaryBrush"));

                if (ChkCodeMode.IsChecked == true)
                {
                    string text = GetEditorText();
                    foreach (var token in TextAnalysisService.FindCodeTokens(text))
                    {
                        var tokenStart = GetTextPosition(token.Start);
                        var tokenEnd = GetTextPosition(token.Start + token.Length);
                        if (tokenStart == null || tokenEnd == null)
                        {
                            continue;
                        }

                        new TextRange(tokenStart, tokenEnd).ApplyPropertyValue(
                            TextElement.ForegroundProperty,
                            GetCodeTokenBrush(token.Kind));
                    }
                }
            }
            finally
            {
                var caret = GetTextPosition(caretOffset);
                if (caret != null)
                {
                    TxtResult.CaretPosition = caret;
                }

                if (hasSelection)
                {
                    var selectionStart = GetTextPosition(selectionStartOffset);
                    var selectionEnd = GetTextPosition(selectionEndOffset);
                    if (selectionStart != null && selectionEnd != null)
                    {
                        TxtResult.Selection.Select(selectionStart, selectionEnd);
                    }
                }

                _suppressTextChange = previous;
            }
        }

        private int GetTextOffset(TextPointer pointer)
        {
            var range = new TextRange(TxtResult.Document.ContentStart, pointer);
            return range.Text.Replace("\r\n", "\n").Replace('\r', '\n').Length;
        }

        private TextPointer? GetTextPosition(int offset)
        {
            var document = TxtResult.Document;
            TextPointer? pointer = document.ContentStart;
            int index = 0;

            while (pointer != null)
            {
                var next = pointer.GetPositionAtOffset(1, LogicalDirection.Forward);
                if (next == null)
                {
                    break;
                }

                var adjacent = pointer.GetAdjacentElement(LogicalDirection.Forward);
                if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementEnd &&
                    adjacent is Paragraph)
                {
                    index++;
                    if (offset == index)
                    {
                        return next;
                    }
                }

                string raw = new TextRange(pointer, next).Text;
                string segment = raw.Replace("\r\n", "\n").Replace('\r', '\n');
                if (segment.Length == 0)
                {
                    pointer = next;
                    continue;
                }

                if (offset < index + segment.Length)
                {
                    return pointer.GetPositionAtOffset(offset - index, LogicalDirection.Forward);
                }

                index += segment.Length;
                pointer = next;
                if (offset == index)
                {
                    return pointer;
                }
            }

            return index == offset ? pointer : null;
        }

        private static Brush GetCodeTokenBrush(CodeTokenKind kind)
        {
            return kind switch
            {
                CodeTokenKind.Keyword => ThemeService.GetBrush("AccentTextBrush"),
                CodeTokenKind.String => ThemeService.GetBrush("SuccessTextBrush"),
                CodeTokenKind.Number => ThemeService.GetBrush("WarningTextBrush"),
                CodeTokenKind.Comment => ThemeService.GetBrush("TextMutedBrush"),
                CodeTokenKind.Function => ThemeService.GetBrush("AccentBrush"),
                _ => ThemeService.GetBrush("TextPrimaryBrush")
            };
        }

        private void UpdateTextDetails()
        {
            string text = GetEditorText();
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
            string text = GetEditorText();
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
            double horizontalScrollBar = noWrap ? SystemParameters.HorizontalScrollBarHeight : 0;
            TxtResult.Height = Math.Clamp(
                visualLines * lineHeight + 28 + horizontalScrollBar,
                72,
                360);
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
            if (_suppressTextChange || TxtMeta == null || ChkCodeMode == null || ChkWrap == null ||
                ColorSwatchesPanel == null || ColorPanel == null)
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
                File.WriteAllText(dialog.FileName, GetEditorText(), new UTF8Encoding(false));
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
                Clipboard.SetText(GetEditorText());
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
