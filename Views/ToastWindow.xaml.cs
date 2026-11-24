using System;
using System.Windows;
using System.Windows.Threading;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class ToastWindow : Window
    {
        private readonly DispatcherTimer _timer;

        public ToastWindow(string message)
        {
            InitializeComponent();
            TxtMessage.Text = message;
            FlowDirection = LocalizationService.Instance.FlowDirection;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                Close();
            };
            Loaded += (_, _) =>
            {
                var area = SystemParameters.WorkArea;
                Left = Math.Max(area.Left + 8, area.Right - ActualWidth - 16);
                Top = Math.Max(area.Top + 8, area.Bottom - ActualHeight - 16);
                _timer.Start();
            };
        }
    }
}
