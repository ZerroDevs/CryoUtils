using System.Windows;
using System.Windows.Input;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class HotkeyCaptureWindow : Window
    {
        public string ModifiersToken { get; private set; } = "";
        public string KeyToken { get; private set; } = "";

        public HotkeyCaptureWindow(string modifiers, string key)
        {
            InitializeComponent();
            ModifiersToken = modifiers ?? "";
            KeyToken = key ?? "";
            UpdateDisplay();
            Loaded += (_, _) => Activate();
        }

        private void UpdateDisplay()
        {
            string text = GlobalHotkeyService.FormatShortcutDisplay(ModifiersToken, KeyToken);
            TxtShortcut.Text = string.IsNullOrWhiteSpace(text)
                ? LocalizationService.Get("Hotkeys_Press")
                : text;
            BtnSave.IsEnabled = !string.IsNullOrWhiteSpace(KeyToken);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                return;
            }

            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(KeyToken))
            {
                DialogResult = true;
                Close();
                return;
            }

            if (GlobalHotkeyService.TryCaptureShortcut(
                    e.Key,
                    e.SystemKey,
                    Keyboard.Modifiers,
                    out string modifiers,
                    out string key))
            {
                ModifiersToken = modifiers;
                KeyToken = key;
                TxtError.Visibility = Visibility.Collapsed;
                UpdateDisplay();
                e.Handled = true;
            }
            else if (!IsModifierKey(e.Key))
            {
                TxtError.Text = LocalizationService.Get("Hotkey_CaptureFailed");
                TxtError.Visibility = Visibility.Visible;
                e.Handled = true;
            }
        }

        private static bool IsModifierKey(Key key)
        {
            return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System
                or Key.None;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
