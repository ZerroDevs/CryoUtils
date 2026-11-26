using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using CryoUtils.Services;
using System.Security.Cryptography;

namespace CryoUtils.Views
{
    public partial class DeveloperToolboxWindow : Window
    {
        public DeveloperToolboxWindow()
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
        }

        private void BtnPrettyJson_Click(object sender, RoutedEventArgs e) => TransformJson(indented: true);

        private void BtnMinifyJson_Click(object sender, RoutedEventArgs e) => TransformJson(indented: false);

        private void TransformJson(bool indented)
        {
            try
            {
                using var document = JsonDocument.Parse(TxtInput.Text);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = indented
                };
                TxtOutput.Text = JsonSerializer.Serialize(document.RootElement, options);
                SetStatus("OK");
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        private void BtnXml_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtOutput.Text = XDocument.Parse(TxtInput.Text).ToString();
                SetStatus("OK");
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        private void BtnBase64Encode_Click(object sender, RoutedEventArgs e)
        {
            TxtOutput.Text = Convert.ToBase64String(Encoding.UTF8.GetBytes(TxtInput.Text));
            SetStatus("OK");
        }

        private void BtnBase64Decode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtOutput.Text = Encoding.UTF8.GetString(Convert.FromBase64String(TxtInput.Text.Trim()));
                SetStatus("OK");
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        private void BtnUrlEncode_Click(object sender, RoutedEventArgs e)
        {
            TxtOutput.Text = Uri.EscapeDataString(TxtInput.Text);
            SetStatus("OK");
        }

        private void BtnUrlDecode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtOutput.Text = Uri.UnescapeDataString(TxtInput.Text);
                SetStatus("OK");
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        private void BtnHash_Click(object sender, RoutedEventArgs e)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(TxtInput.Text));
            TxtOutput.Text = Convert.ToHexString(hash).ToLowerInvariant();
            SetStatus("SHA-256");
        }

        private void BtnGuid_Click(object sender, RoutedEventArgs e)
        {
            TxtOutput.Text = Guid.NewGuid().ToString();
            SetStatus("GUID");
        }

        private void BtnRegex_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var matches = Regex.Matches(TxtInput.Text, TxtPattern.Text);
                var builder = new StringBuilder();
                foreach (Match match in matches)
                {
                    builder.Append(match.Index)
                        .Append(": ")
                        .AppendLine(match.Value);
                }
                TxtOutput.Text = builder.ToString();
                SetStatus(LocalizationService.Format("Dev_MatchCount", matches.Count));
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtOutput.Text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(TxtOutput.Text);
                SetStatus(LocalizationService.Get("Common_Copied"));
            }
            catch
            {
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtInput.Clear();
            TxtOutput.Clear();
            TxtPattern.Clear();
            SetStatus(LocalizationService.Get("Dev_Ready"));
        }

        private void Report(Exception ex)
        {
            TxtOutput.Text = ex.Message;
            SetStatus(LocalizationService.Get("Dev_Error"));
        }

        private void SetStatus(string status)
        {
            TxtStatus.Text = status;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
