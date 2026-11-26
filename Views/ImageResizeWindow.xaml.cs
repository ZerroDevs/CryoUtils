using System;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;

namespace CryoUtils.Views
{
    public partial class ImageResizeWindow : Window
    {
        private readonly double _aspect;
        private bool _updating;

        public int TargetWidth { get; private set; }
        public int TargetHeight { get; private set; }

        public ImageResizeWindow(int width, int height)
        {
            InitializeComponent();
            TargetWidth = Math.Max(1, width);
            TargetHeight = Math.Max(1, height);
            _aspect = height <= 0 ? 1 : width / (double)height;
            _updating = true;
            TxtWidth.Text = TargetWidth.ToString(CultureInfo.InvariantCulture);
            TxtHeight.Text = TargetHeight.ToString(CultureInfo.InvariantCulture);
            _updating = false;
        }

        private void Size_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating || ChkAspect.IsChecked != true)
            {
                return;
            }

            _updating = true;
            try
            {
                if (ReferenceEquals(sender, TxtWidth) &&
                    int.TryParse(TxtWidth.Text, out int width) && width > 0)
                {
                    TxtHeight.Text = Math.Max(1, (int)Math.Round(width / _aspect))
                        .ToString(CultureInfo.InvariantCulture);
                }
                else if (ReferenceEquals(sender, TxtHeight) &&
                         int.TryParse(TxtHeight.Text, out int height) && height > 0)
                {
                    TxtWidth.Text = Math.Max(1, (int)Math.Round(height * _aspect))
                        .ToString(CultureInfo.InvariantCulture);
                }
            }
            finally
            {
                _updating = false;
            }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtWidth.Text, out int width) ||
                !int.TryParse(TxtHeight.Text, out int height) ||
                width < 1 || height < 1 || width > 20000 || height > 20000)
            {
                return;
            }

            TargetWidth = width;
            TargetHeight = height;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
