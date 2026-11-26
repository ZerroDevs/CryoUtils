using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class OperationProgressWindow : Window
    {
        private readonly CancellationTokenSource _cts = new();
        private bool _finished;

        public OperationProgressWindow(string operation)
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            TxtOperation.Text = operation;
            TxtStatus.Text = LocalizationService.Get("Progress_Starting");
        }

        public CancellationToken Token => _cts.Token;

        public void Report(int percent)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Report(percent)));
                return;
            }

            if (_finished)
            {
                return;
            }

            int value = Math.Clamp(percent, 0, 100);
            ProgressBar.Value = value;
            TxtStatus.Text = LocalizationService.Format("Progress_PercentFmt", value);
        }

        public void Complete()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Complete);
                return;
            }

            _finished = true;
            if (IsVisible)
            {
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_finished)
            {
                return;
            }

            _cts.Cancel();
            BtnCancel.IsEnabled = false;
            TxtStatus.Text = LocalizationService.Get("Progress_Cancelling");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_finished)
            {
                _cts.Cancel();
            }
        }
    }
}