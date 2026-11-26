using System.Windows;
using System.Windows.Input;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class ClipboardTransformWindow : Window
    {
        private readonly string _transformed;

        public ClipboardTransformWindow(string original, string transformed)
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            _transformed = transformed;
            TxtOriginal.Text = original;
            TxtTransformed.Text = transformed;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_transformed);
                (Application.Current as App)?.ShowToast(LocalizationService.Get("Common_Copied"));
                Close();
            }
            catch
            {
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
