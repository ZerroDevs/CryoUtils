using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CryoUtils.Services;
using Microsoft.Win32;

namespace CryoUtils.Views
{
    public partial class RecordingTrimWindow : Window
    {
        private readonly string _sourcePath;
        private readonly string _suggestedPath;
        private double _duration;
        private bool _syncing;
        private bool _saving;
        private bool _playing;

        public string SavedPath { get; private set; } = "";

        public RecordingTrimWindow(string sourcePath, string suggestedPath)
        {
            InitializeComponent();
            _sourcePath = sourcePath;
            _suggestedPath = suggestedPath;
            FlowDirection = LocalizationService.Instance.FlowDirection;
            Loaded += (_, _) =>
            {
                try
                {
                    Preview.Source = new Uri(Path.GetFullPath(sourcePath));
                    Preview.Play();
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = ex.Message;
                }
            };
        }

        private void Preview_MediaOpened(object sender, RoutedEventArgs e)
        {
            _duration = Preview.NaturalDuration.HasTimeSpan
                ? Preview.NaturalDuration.TimeSpan.TotalSeconds
                : 0;
            if (_duration <= 0)
            {
                TxtStatus.Text = LocalizationService.Get("Rec_TrimInvalid");
                return;
            }

            _syncing = true;
            SldStart.Maximum = _duration;
            SldEnd.Maximum = _duration;
            SldStart.Value = 0;
            SldEnd.Value = _duration;
            _syncing = false;
            PreviewEmpty.Visibility = Visibility.Collapsed;
            UpdateLabels();
            Preview.Position = TimeSpan.Zero;
            Preview.Pause();
        }

        private void Preview_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            TxtStatus.Text = e.ErrorException?.Message
                ?? LocalizationService.Get("Rec_TrimInvalid");
            PreviewEmpty.Visibility = Visibility.Visible;
        }

        private void Trim_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Sliders raise ValueChanged while XAML is still being loaded; the
            // label fields below do not exist yet at that point.
            if (_syncing || !IsInitialized)
            {
                return;
            }

            if (SldStart.Value >= SldEnd.Value)
            {
                if (ReferenceEquals(sender, SldStart))
                {
                    SldEnd.Value = Math.Min(SldEnd.Maximum, SldStart.Value + 0.1);
                }
                else
                {
                    SldStart.Value = Math.Max(0, SldEnd.Value - 0.1);
                }
            }

            if (!_playing && _duration > 0)
            {
                Preview.Position = TimeSpan.FromSeconds(SldStart.Value);
            }
            UpdateLabels();
        }

        private void UpdateLabels()
        {
            TxtStart.Text = FormatTime(SldStart.Value);
            TxtEnd.Text = FormatTime(SldEnd.Value);
            TxtDuration.Text = FormatTime(_duration);
        }

        private static string FormatTime(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}";
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_duration <= 0)
            {
                return;
            }

            if (_playing)
            {
                Preview.Pause();
                _playing = false;
                return;
            }

            Preview.Position = TimeSpan.FromSeconds(SldStart.Value);
            Preview.Play();
            _playing = true;
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_saving || _duration <= 0)
            {
                return;
            }

            double start = SldStart.Value;
            double end = SldEnd.Value;
            if (end - start < 0.1)
            {
                TxtStatus.Text = LocalizationService.Get("Rec_TrimInvalid");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Rec_TrimSave"),
                Filter = LocalizationService.Get("Common_SaveRecordingFilter"),
                FilterIndex = 2,
                DefaultExt = ".mp4",
                AddExtension = true,
                FileName = Path.GetFileName(_suggestedPath),
                InitialDirectory = Path.GetDirectoryName(_suggestedPath)
                    ?? SettingsService.Instance.EffectiveSaveFolder
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _saving = true;
            BtnSave.IsEnabled = false;
            TxtStatus.Text = LocalizationService.Get("Rec_TrimSaving");
            try
            {
                await MediaConversionService.TrimVideoAsync(
                    _sourcePath,
                    dialog.FileName,
                    start,
                    end);
                SavedPath = dialog.FileName;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = ex.Message;
            }
            finally
            {
                _saving = false;
                BtnSave.IsEnabled = true;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
