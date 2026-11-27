using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CryoUtils.Services;
using Microsoft.Win32;

namespace CryoUtils.Views
{
    public partial class QrResultWindow : Window
    {
        private readonly string _content;
        private readonly BitmapSource? _qrImage;
        private string _wifiPassword = "";

        public QrResultWindow(string content) : this(content, "QR_CODE", null)
        {
        }

        public QrResultWindow(string content, string formatName) : this(content, formatName, null)
        {
        }

        public QrResultWindow(string content, string formatName, BitmapSource? qrImage)
        {
            InitializeComponent();
            _content = content ?? "";
            FlowDirection = LocalizationService.Instance.FlowDirection;

            TxtContent.Text = _content;
            TxtFormat.Text = string.IsNullOrWhiteSpace(formatName) ? "QR_CODE" : formatName;
            TxtCharCount.Text = LocalizationService.Format("Clip_Length", _content.Length);

            _qrImage = qrImage ?? GenerateQrThumbnail(_content);
            if (_qrImage != null)
            {
                ImgQrThumbnail.Source = _qrImage;
            }

            bool isUrl = Uri.TryCreate(_content, UriKind.Absolute, out var uri) &&
                         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            BtnOpenUrl.Visibility = isUrl ? Visibility.Visible : Visibility.Collapsed;

            if (TryParseWifiPayload(_content, out string ssid, out string wifiPassword, out _))
            {
                if (!string.IsNullOrWhiteSpace(ssid))
                {
                    TxtWifiInfo.Text = LocalizationService.Format("Qr_WifiNetwork", ssid);
                    TxtWifiInfo.Visibility = Visibility.Visible;
                }
                if (!string.IsNullOrEmpty(wifiPassword))
                {
                    _wifiPassword = wifiPassword;
                    BtnCopyWifiPassword.Visibility = Visibility.Visible;
                }
            }

            Loaded += (_, _) =>
            {
                TxtContent.Focus();
                TxtContent.SelectAll();
                Activate();
            };
        }

        private static BitmapSource? GenerateQrThumbnail(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return QrCodeService.Encode(text, 128);
            }
            catch
            {
                return null;
            }
        }

        private void BtnSaveQrImage_Click(object sender, RoutedEventArgs e)
        {
            SaveQrImage();
        }

        private void ImgQrThumbnail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SaveQrImage();
        }

        private void SaveQrImage()
        {
            if (_qrImage == null) return;
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Tools_QrSave"),
                Filter = LocalizationService.Get("Common_PngFilter"),
                DefaultExt = ".png",
                AddExtension = true,
                FileName = $"CryoUtils_QR_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    ImageIO.SaveBitmap(_qrImage, dialog.FileName);
                    RecentCaptureService.Instance.Add(_qrImage, dialog.FileName, LocalizationService.Get("Tools_QrTitle"));
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_Saved"));
                }
                catch
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Error"));
                }
            }
        }

        private void BtnOpenUrl_Click(object sender, RoutedEventArgs e)
        {
            if (Uri.TryCreate(_content, UriKind.Absolute, out var uri))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    Close();
                }
                catch
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Error"));
                }
            }
        }

        private void BtnCopyWifiPassword_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_wifiPassword))
            {
                return;
            }
            try
            {
                Clipboard.SetText(_wifiPassword);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
            }
            catch
            {
            }
        }

        internal static bool TryParseWifiPayload(string? content, out string ssid, out string password, out string authType)
        {
            ssid = "";
            password = "";
            authType = "";
            if (string.IsNullOrWhiteSpace(content) ||
                !content.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string body = content.Substring(5);
            var parts = new System.Collections.Generic.List<string>();
            var current = new StringBuilder();
            bool escaped = false;
            foreach (char ch in body)
            {
                if (escaped)
                {
                    current.Append(ch);
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == ';')
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }
            if (current.Length > 0)
            {
                parts.Add(current.ToString());
            }

            foreach (string part in parts)
            {
                int colon = part.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }
                string key = part[..colon].ToUpperInvariant();
                string value = part[(colon + 1)..];
                switch (key)
                {
                    case "S": ssid = value; break;
                    case "P": password = value; break;
                    case "T": authType = value; break;
                }
            }

            return !string.IsNullOrEmpty(ssid) || !string.IsNullOrEmpty(password);
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_content);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
                Close();
            }
            catch
            {
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Common_SaveAs"),
                Filter = LocalizationService.Get("Common_TextFilter"),
                DefaultExt = ".txt",
                AddExtension = true,
                FileName = $"CryoUtils_QR_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, _content, new UTF8Encoding(false));
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Capture_Saved"));
                }
                catch
                {
                    (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Error"));
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
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
                e.Handled = true;
            }
        }
    }
}
