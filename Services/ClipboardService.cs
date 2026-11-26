using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CryoUtils.Services
{
    public enum TextTransformType
    {
        Uppercase,
        Lowercase,
        TitleCase,
        CamelCase,
        SnakeCase,
        KebabCase,
        Trim,
        UrlEncode,
        UrlDecode,
        Base64Encode,
        Base64Decode,
        JsonFormat,
        JsonMinify
    }

    public sealed class ClipItem
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string? Text { get; init; }
        public BitmapSource? Image { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.Now;
        public DateTime? ExpiresAt { get; set; }
        public bool Sensitive { get; init; }
        public bool IsPinned { get; set; }
        public bool IsSnippet { get; init; }
        public string Name { get; init; } = "";
        public int CharacterCount => Text?.Length ?? 0;

        public string DisplayText => IsSnippet
            ? $"{Name}  ·  {Truncate(Text ?? "", 120)}"
            : string.IsNullOrEmpty(Text)
                ? LocalizationService.Get("Clip_ImageClip")
                : Text!.Replace("\r", " ").Replace("\n", " ").Trim();

        public string Meta
        {
            get
            {
                string baseMeta = Text == null
                    ? $"{Image?.PixelWidth ?? 0} x {Image?.PixelHeight ?? 0}  {CreatedAt:HH:mm}"
                    : LocalizationService.Format("Clip_Length", Text.Length) + $"  {CreatedAt:HH:mm}";
                if (IsSnippet)
                {
                    baseMeta = LocalizationService.Get("Clip_Snippet") + "  " + baseMeta;
                }
                if (ExpiresAt.HasValue)
                {
                    baseMeta += "  " + LocalizationService.Format(
                        "Clip_ExpiresFmt",
                        ExpiresAt.Value.ToString("HH:mm"));
                }
                return baseMeta;
            }
        }

        private static string Truncate(string text, int length) =>
            text.Length <= length ? text.Replace("\r", " ").Replace("\n", " ") : text[..length];
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
        private bool _persistenceEnabled;
        private DispatcherTimer? _expiryTimer;

        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryoUtils");
        private static readonly string PersistenceFile = Path.Combine(DataFolder, "clipboard.bin");
        private static readonly JsonSerializerOptions PersistenceJson = new() { WriteIndented = false };

        public ObservableCollection<ClipItem> Items => _items;
        public event Action? Changed;

        private ClipboardService()
        {
            if (SettingsService.Instance.Current.ClipboardPersistenceEnabled)
            {
                SetPersistence(true);
            }
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
                ApplyExpiration(existing);
                SavePersistent();
                Changed?.Invoke();
                return;
            }

            bool sensitive = SensitivePattern.IsMatch(text);
            var item = new ClipItem { Text = text, Sensitive = sensitive };
            ApplyExpiration(item);
            _items.Insert(0, item);
            Trim();
            SavePersistent();
            EnsureExpiryTimer();
            Changed?.Invoke();
            if (sensitive && SettingsService.Instance.Current.ClipboardAutoClearSensitive)
            {
                ScheduleSensitiveRemoval(item, SettingsService.Instance.Current.ClipboardAutoClearSeconds);
            }
        }

        private void AddImage(BitmapSource image)
        {
            var item = new ClipItem { Image = image };
            ApplyExpiration(item);
            _items.Insert(0, item);
            Trim();
            SavePersistent();
            EnsureExpiryTimer();
            Changed?.Invoke();
        }

        private static void ApplyExpiration(ClipItem item)
        {
            int minutes = SettingsService.Instance.Current.ClipboardExpirationMinutes;
            item.ExpiresAt = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : null;
        }

        private void Trim()
        {
            int limit = Math.Clamp(SettingsService.Instance.Current.ClipboardHistoryLimit, 10, 100);
            while (_items.Count > limit)
            {
                int removeIndex = -1;
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    if (!_items[i].IsPinned && !_items[i].IsSnippet)
                    {
                        removeIndex = i;
                        break;
                    }
                }

                if (removeIndex >= 0)
                {
                    _items.RemoveAt(removeIndex);
                }
                else
                {
                    // Keep snippets when possible; the configured limit remains a hard cap.
                    for (int i = _items.Count - 1; i >= 0; i--)
                    {
                        if (!_items[i].IsSnippet)
                        {
                            removeIndex = i;
                            break;
                        }
                    }
                    _items.RemoveAt(removeIndex >= 0 ? removeIndex : _items.Count - 1);
                }
            }
        }

        public void TogglePin(ClipItem item)
        {
            item.IsPinned = !item.IsPinned;
            SavePersistent();
            Changed?.Invoke();
        }

        public void AddSnippet(string name, string text)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var existing = _items.FirstOrDefault(item =>
                item.IsSnippet &&
                string.Equals(item.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));
            if (existing != null)
            {
                _items.Remove(existing);
            }

            _items.Insert(0, new ClipItem
            {
                Text = text,
                Name = name.Trim(),
                IsSnippet = true,
                IsPinned = true
            });
            Trim();
            SavePersistent();
            Changed?.Invoke();
        }

        public void RemoveSnippet(ClipItem item)
        {
            if (item.IsSnippet)
            {
                Remove(item);
            }
        }

        public static string TransformText(string text, TextTransformType transform)
        {
            return transform switch
            {
                TextTransformType.Uppercase => TransformText(text, "UPPER"),
                TextTransformType.Lowercase => TransformText(text, "lower"),
                TextTransformType.TitleCase => TransformText(text, "Title"),
                TextTransformType.CamelCase => TransformText(text, "camel"),
                TextTransformType.SnakeCase => TransformText(text, "snake"),
                TextTransformType.KebabCase => TransformText(text, "kebab"),
                TextTransformType.Trim => TransformText(text, "trim"),
                TextTransformType.UrlEncode => TransformText(text, "urlencode"),
                TextTransformType.UrlDecode => TransformText(text, "urldecode"),
                TextTransformType.Base64Encode => TransformText(text, "base64"),
                TextTransformType.Base64Decode => TransformText(text, "unbase64"),
                TextTransformType.JsonFormat => TransformText(text, "json"),
                TextTransformType.JsonMinify => TransformText(text, "jsonmin"),
                _ => text
            };
        }

        public static string TransformText(string text, string transform)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return transform switch
            {
                "UPPER" => text.ToUpperInvariant(),
                "lower" => text.ToLowerInvariant(),
                "Title" => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower(System.Globalization.CultureInfo.CurrentCulture)),
                "camel" => ToCamelCase(text),
                "snake" => ToDelimitedCase(text, '_'),
                "kebab" => ToDelimitedCase(text, '-'),
                "trim" => text.Trim(),
                "urlencode" => Uri.EscapeDataString(text),
                "urldecode" => Uri.UnescapeDataString(text),
                "base64" => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)),
                "unbase64" => DecodeBase64(text),
                "json" => FormatJson(text, indented: true),
                "jsonmin" => FormatJson(text, indented: false),
                _ => text
            };
        }

        private static string DecodeBase64(string text)
        {
            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(text.Trim()));
            }
            catch
            {
                return text;
            }
        }

        private static string FormatJson(string text, bool indented)
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return JsonSerializer.Serialize(
                    document.RootElement,
                    new JsonSerializerOptions { WriteIndented = indented });
            }
            catch
            {
                return text;
            }
        }

        private static string ToCamelCase(string text)
        {
            var words = Regex.Split(text, @"[\s_\-]+").Where(w => !string.IsNullOrEmpty(w)).ToArray();
            if (words.Length == 0) return text;
            var sb = new System.Text.StringBuilder();
            sb.Append(words[0].ToLowerInvariant());
            for (int i = 1; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    sb.Append(char.ToUpperInvariant(words[i][0]));
                    if (words[i].Length > 1)
                    {
                        sb.Append(words[i][1..].ToLowerInvariant());
                    }
                }
            }
            return sb.ToString();
        }

        private static string ToDelimitedCase(string text, char delimiter)
        {
            var words = Regex.Split(text, @"[\s_\-]+").Where(w => !string.IsNullOrEmpty(w));
            return string.Join(delimiter, words.Select(w => w.ToLowerInvariant()));
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
                    SavePersistent();
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
            SavePersistent();
            Changed?.Invoke();
        }

        public void Clear()
        {
            _items.Clear();
            SavePersistent();
            Changed?.Invoke();
        }

        public void SetPersistence(bool enabled)
        {
            if (_persistenceEnabled == enabled)
            {
                return;
            }

            _persistenceEnabled = enabled;
            if (enabled)
            {
                LoadPersistent();
            }
            else
            {
                TryDeletePersistenceFile();
            }
        }

        private void LoadPersistent()
        {
            try
            {
                if (!File.Exists(PersistenceFile))
                {
                    return;
                }

                byte[] protectedBytes = File.ReadAllBytes(PersistenceFile);
                byte[] jsonBytes = DpapiService.Unprotect(protectedBytes);
                var persisted = JsonSerializer.Deserialize<List<PersistedClip>>(jsonBytes);
                if (persisted == null)
                {
                    return;
                }

                foreach (var entry in persisted)
                {
                    if (string.IsNullOrEmpty(entry.Text)
                        || (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTime.Now)
                        || _items.Any(item => item.Text == entry.Text && item.IsSnippet == entry.IsSnippet))
                    {
                        continue;
                    }

                    _items.Add(new ClipItem
                    {
                        Text = entry.Text,
                        Name = entry.Name,
                        CreatedAt = entry.CreatedAt,
                        ExpiresAt = entry.ExpiresAt,
                        Sensitive = entry.Sensitive,
                        IsPinned = entry.IsPinned,
                        IsSnippet = entry.IsSnippet
                    });
                }

                Trim();
                EnsureExpiryTimer();
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                DiagnosticsService.Log("Clipboard persistence load failed", ex);
            }
        }

        private void SavePersistent()
        {
            if (!_persistenceEnabled)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(DataFolder);
                var entries = _items
                    .Where(item => item.Text != null)
                    .Select(item => new PersistedClip
                    {
                        Text = item.Text!,
                        Name = item.Name,
                        CreatedAt = item.CreatedAt,
                        ExpiresAt = item.ExpiresAt,
                        Sensitive = item.Sensitive,
                        IsPinned = item.IsPinned,
                        IsSnippet = item.IsSnippet
                    })
                    .ToList();
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(entries, PersistenceJson);
                byte[] protectedBytes = DpapiService.Protect(json);
                string tempPath = PersistenceFile + ".tmp";
                File.WriteAllBytes(tempPath, protectedBytes);
                File.Move(tempPath, PersistenceFile, overwrite: true);
            }
            catch (Exception ex)
            {
                DiagnosticsService.Log("Clipboard persistence save failed", ex);
            }
        }

        private static void TryDeletePersistenceFile()
        {
            try
            {
                if (File.Exists(PersistenceFile))
                {
                    File.Delete(PersistenceFile);
                }
            }
            catch
            {
            }
        }

        private void EnsureExpiryTimer()
        {
            if (_expiryTimer != null)
            {
                return;
            }

            _expiryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _expiryTimer.Tick += (_, _) => ExpireItems();
            _expiryTimer.Start();
        }

        private void ExpireItems()
        {
            var expired = _items.Where(item => item.ExpiresAt.HasValue && item.ExpiresAt.Value <= DateTime.Now).ToList();
            foreach (var item in expired)
            {
                _items.Remove(item);
            }

            if (expired.Count > 0)
            {
                SavePersistent();
                Changed?.Invoke();
            }

            if (!_items.Any(item => item.ExpiresAt.HasValue))
            {
                _expiryTimer?.Stop();
                _expiryTimer = null;
            }
        }

        private sealed class PersistedClip
        {
            public string Text { get; set; } = "";
            public string Name { get; set; } = "";
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime? ExpiresAt { get; set; }
            public bool Sensitive { get; set; }
            public bool IsPinned { get; set; }
            public bool IsSnippet { get; set; }
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
            _persistenceEnabled = false;
            _expiryTimer?.Stop();
            _expiryTimer = null;
            _items.Clear();
        }
    }
}
