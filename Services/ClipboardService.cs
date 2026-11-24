using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CryoUtils.Services
{
    public sealed class ClipItem
    {
        public string? Text { get; init; }
        public BitmapSource? Image { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.Now;
        public bool Sensitive { get; init; }
        public int CharacterCount => Text?.Length ?? 0;

        public string DisplayText => string.IsNullOrEmpty(Text)
            ? LocalizationService.Get("Clip_ImageClip")
            : Text!.Replace("\r", " ").Replace("\n", " ").Trim();

        public string Meta => Text == null
            ? $"{Image?.PixelWidth ?? 0} x {Image?.PixelHeight ?? 0}  {CreatedAt:HH:mm}"
            : LocalizationService.Format("Clip_Length", Text.Length) + $"  {CreatedAt:HH:mm}";
    }

    public sealed class ClipboardService : IDisposable
    {
        private static readonly Regex SensitivePattern = new(
            @"(?i)(password|passwd|pwd|token|secret|api[\s_-]?key|bearer|authorization|cvv|ssn|credit\s*card)",
            RegexOptions.Compiled);

        private static ClipboardService? _instance;
        public static ClipboardService Instance => _instance ??= new ClipboardService();

        private readonly ObservableCollection<ClipItem> _items = new();
        private HwndSource? _source;
        private bool _enabled;
        private bool _ignoreNext;
        private string _lastText = "";

        public ObservableCollection<ClipItem> Items => _items;
        public event Action? Changed;

        private ClipboardService()
        {
        }

        public void SetEnabled(bool enabled)
        {
            if (enabled == _enabled)
            {
                return;
            }

            _enabled = enabled;
            if (enabled)
            {
                EnsureListener();
            }
            else
            {
                StopListener();
                Clear();
            }
        }

        private void EnsureListener()
        {
            if (_source != null)
            {
                return;
            }

            var parameters = new HwndSourceParameters("CryoUtilsClipboard")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = 0,
                ParentWindow = new IntPtr(-3)
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
            NativeMethods.AddClipboardFormatListener(_source.Handle);
        }

        private void StopListener()
        {
            if (_source == null)
            {
                return;
            }

            NativeMethods.RemoveClipboardFormatListener(_source.Handle);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_CLIPBOARDUPDATE && _enabled)
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(ReadClipboard));
            }
            return IntPtr.Zero;
        }

        private void ReadClipboard()
        {
            if (!_enabled || _ignoreNext)
            {
                _ignoreNext = false;
                return;
            }

            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(text) || string.Equals(text, _lastText, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _lastText = text;
                    AddText(text);
                    return;
                }

                if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource image)
                {
                    if (_items.Count > 0 && _items[0].Image != null)
                    {
                        return;
                    }
                    AddImage(image);
                }
            }
            catch
            {
            }
        }

        private void AddText(string text)
        {
            var existing = _items.FirstOrDefault(item =>
                string.Equals(item.Text, text, StringComparison.Ordinal));
            if (existing != null)
            {
                _items.Remove(existing);
                _items.Insert(0, existing);
                Changed?.Invoke();
                return;
            }

            bool sensitive = SensitivePattern.IsMatch(text);
            var item = new ClipItem { Text = text, Sensitive = sensitive };
            _items.Insert(0, item);
            Trim();
            Changed?.Invoke();
            if (sensitive && SettingsService.Instance.Current.ClipboardAutoClearSensitive)
            {
                ScheduleSensitiveRemoval(item, SettingsService.Instance.Current.ClipboardAutoClearSeconds);
            }
        }

        private void AddImage(BitmapSource image)
        {
            _items.Insert(0, new ClipItem { Image = image });
            Trim();
            Changed?.Invoke();
        }

        private void Trim()
        {
            int limit = Math.Clamp(SettingsService.Instance.Current.ClipboardHistoryLimit, 10, 100);
            while (_items.Count > limit)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }

        private void ScheduleSensitiveRemoval(ClipItem item, int seconds)
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 10, 600))
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_items.Contains(item))
                {
                    _items.Remove(item);
                    Changed?.Invoke();
                    (Application.Current as App)?.ShowToast(
                        LocalizationService.Format("Clip_SensitiveRemoved", seconds));
                }
            };
            timer.Start();
        }

        public void CopyItem(ClipItem item)
        {
            try
            {
                _ignoreNext = true;
                if (item.Text != null)
                {
                    Clipboard.SetText(item.Text);
                    _lastText = item.Text;
                }
                else if (item.Image != null)
                {
                    Clipboard.SetImage(item.Image);
                }
            }
            catch
            {
                _ignoreNext = false;
            }
        }

        public void Remove(ClipItem item)
        {
            _items.Remove(item);
            Changed?.Invoke();
        }

        public void Clear()
        {
            _items.Clear();
            Changed?.Invoke();
        }

        public void PlainPaste()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    return;
                }

                string text = Clipboard.GetText();
                _ignoreNext = true;
                Clipboard.SetText(text);
                _lastText = text;
                NativeMethods.SendPlainPasteShortcut();
            }
            catch
            {
                _ignoreNext = false;
            }
        }

        public void Dispose()
        {
            StopListener();
            Clear();
        }
    }
}
