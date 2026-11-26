using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CryoUtils.Services;

namespace CryoUtils.Views
{
    public partial class CountdownWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _onCompleted;
        private readonly Action? _onCancelled;
        private int _remainingSeconds;
        private bool _completed;
        private bool _cancelRaised;

        public CountdownWindow(int seconds, Action onCompleted, Action? onCancelled = null)
        {
            InitializeComponent();
            FlowDirection = LocalizationService.Instance.FlowDirection;
            _remainingSeconds = Math.Clamp(seconds, 1, 60);
            _onCompleted = onCompleted;
            _onCancelled = onCancelled;

            TxtSeconds.Text = _remainingSeconds.ToString();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;

            Loaded += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_TOPMOST,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                _timer.Start();
            };

            Closed += (_, _) =>
            {
                _timer.Stop();
                if (!_completed)
                {
                    RaiseCancelled();
                }
            };
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _remainingSeconds--;
            if (_remainingSeconds > 0)
            {
                TxtSeconds.Text = _remainingSeconds.ToString();
                if (SettingsService.Instance.Current.CaptureSound)
                {
                    NativeMethods.MessageBeep(0);
                }
            }
            else
            {
                _timer.Stop();
                _completed = true;
                Close();
                _onCompleted();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            CancelCountdown();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelCountdown();
                e.Handled = true;
            }
        }

        private void CancelCountdown()
        {
            if (_completed)
            {
                return;
            }

            _timer.Stop();
            _completed = true;
            RaiseCancelled();
            Close();
        }

        private void RaiseCancelled()
        {
            if (_cancelRaised)
            {
                return;
            }

            _cancelRaised = true;
            _onCancelled?.Invoke();
        }
    }
}
