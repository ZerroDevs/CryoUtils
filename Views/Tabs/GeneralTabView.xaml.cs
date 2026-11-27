using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CryoUtils.Services;
using Microsoft.Win32;

namespace CryoUtils.Views.Tabs
{
    public partial class GeneralTabView : UserControl
    {
        private bool _loading;
        private bool _searchingUpdates;
        private bool _downloadingUpdate;
        private UpdateInfo? _availableUpdate;

        public event Action? FullReloadRequested;
        public event Action? SavedLimitChanged;
        public event Action? AnchorToTrayChanged;

        public GeneralTabView()
        {
            _loading = true;
            try
            {
                InitializeComponent();
            }
            finally
            {
                _loading = false;
            }
        }

        public void LoadFromSettings()
        {
            _loading = true;
            try
            {
                var settings = SettingsService.Instance.Current;
                MainTabHelpers.SelectTag(CmbTheme, settings.Theme);
                MainTabHelpers.SelectTag(CmbAccent, ThemeService.NormalizeAccent(settings.AccentPreset));
                MainTabHelpers.SelectTag(CmbLanguage, settings.Language);
                ChkStartWithWindows.IsChecked = settings.StartWithWindows;
                ChkLaunchBackground.IsChecked = settings.LaunchInBackground;
                ChkAnchorToTray.IsChecked = settings.AnchorToTray;
                ChkDisableNotifications.IsChecked = !settings.NotificationsEnabled;
                ChkScreenshotOnly.IsChecked = settings.ScreenshotOnlyMode;
                MainTabHelpers.SelectTag(
                    CmbTrayClick,
                    string.Equals(settings.TrayLeftClickAction, "Capture", StringComparison.OrdinalIgnoreCase)
                        ? "Capture"
                        : "Dashboard");
                ChkPinnedFrame.IsChecked = settings.ShowPinnedBorder;
                ChkPinnedSound.IsChecked = settings.PinnedBorderSound;
                ChkContextMenu.IsChecked = settings.ContextMenuEnabled;
                TxtSavedLimit.Text = Math.Clamp(settings.SavedCapturesLimit, 0, 10000).ToString(CultureInfo.InvariantCulture);
                ChkAutoUpdates.IsChecked = settings.AutomaticUpdateChecks;
                ChkSaveRollback.IsChecked = settings.SaveRollbackOnUpdate;
            }
            finally
            {
                _loading = false;
            }

            string version = $"v{UpdaterService.CurrentVersion}";
            TxtAboutVersion.Text = LocalizationService.Format("About_Version", version);
            TxtLastChecked.Text = SettingsService.Instance.Current.LastUpdateCheckUtc is DateTime checkedUtc
                ? checkedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : LocalizationService.Get("About_Never");
            if (!string.IsNullOrEmpty(SettingsService.Instance.Current.LastUpdateVersion) &&
                SettingsService.Instance.Current.LastUpdateOutcome == "available")
            {
                TxtUpdateStatus.Text = LocalizationService.Format(
                    "About_UpdateAvailable",
                    SettingsService.Instance.Current.LastUpdateVersion);
            }
            RefreshRollbackUi();
            ApplyScreenshotOnlyUi();
        }

        public void ApplyScreenshotOnlyUi()
        {
            bool lite = SettingsService.Instance.Current.ScreenshotOnlyMode;
            CmbTrayClick.IsEnabled = !lite;
            CmbTrayClick.Opacity = lite ? 0.55 : 1.0;
            ChkPinnedFrame.IsEnabled = !lite;
            ChkPinnedSound.IsEnabled = !lite;
            ChkAutoUpdates.IsEnabled = !lite;
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            MemoryTrimService.PauseForInteraction();
            var settings = SettingsService.Instance.Current;
            bool anchorChanged = settings.AnchorToTray != (ChkAnchorToTray.IsChecked == true);
            settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;
            settings.LaunchInBackground = ChkLaunchBackground.IsChecked == true;
            settings.AnchorToTray = ChkAnchorToTray.IsChecked == true;
            settings.NotificationsEnabled = ChkDisableNotifications.IsChecked != true;
            settings.ScreenshotOnlyMode = ChkScreenshotOnly.IsChecked == true;
            settings.TrayLeftClickAction = MainTabHelpers.SelectedTag(CmbTrayClick) ?? "Dashboard";
            settings.ShowPinnedBorder = ChkPinnedFrame.IsChecked == true;
            settings.PinnedBorderSound = ChkPinnedSound.IsChecked == true;
            settings.ContextMenuEnabled = ChkContextMenu.IsChecked == true;
            settings.AutomaticUpdateChecks = ChkAutoUpdates.IsChecked == true;
            settings.SaveRollbackOnUpdate = ChkSaveRollback.IsChecked == true;

            StartupService.SetEnabled(settings.StartWithWindows);
            SettingsService.Instance.Save();
            TopmostService.Instance.ApplySettings();
            ApplyScreenshotOnlyUi();
            if (anchorChanged && settings.AnchorToTray)
            {
                AnchorToTrayChanged?.Invoke();
            }
        }

        private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            string theme = MainTabHelpers.SelectedTag(CmbTheme) ?? "Dark";
            SettingsService.Instance.Current.Theme = theme;
            ThemeService.Apply(theme, force: true, accentPreset: SettingsService.Instance.Current.AccentPreset);
            SettingsService.Instance.Save();
        }

        private void CmbAccent_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            string accent = ThemeService.NormalizeAccent(MainTabHelpers.SelectedTag(CmbAccent));
            SettingsService.Instance.Current.AccentPreset = accent;
            ThemeService.Apply(SettingsService.Instance.Current.Theme, force: true, accentPreset: accent);
            SettingsService.Instance.Save();
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            string language = MainTabHelpers.SelectedTag(CmbLanguage) ?? "English";
            SettingsService.Instance.Current.Language = language;
            LocalizationService.Instance.Apply(language);
            SettingsService.Instance.Save();
        }

        private void TxtSavedLimit_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var owner = Window.GetWindow(this);
            int previousLimit = SettingsService.Instance.Current.SavedCapturesLimit;
            if (!int.TryParse(TxtSavedLimit.Text, out int limit)) limit = 50;
            limit = Math.Clamp(limit, 0, 10000);
            TxtSavedLimit.Text = limit.ToString(CultureInfo.InvariantCulture);

            if (limit > 0)
            {
                int deleteCount = Math.Max(0, ScreenCaptureService.GetManagedCaptureFiles().Count - limit);
                if (deleteCount > 0)
                {
                    var choice = owner != null
                        ? MessageBox.Show(owner, LocalizationService.Format("SavedLimit_ConfirmFmt", deleteCount), LocalizationService.Get("General_SavedLimit"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                        : MessageBox.Show(LocalizationService.Format("SavedLimit_ConfirmFmt", deleteCount), LocalizationService.Get("General_SavedLimit"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (choice != MessageBoxResult.Yes)
                    {
                        TxtSavedLimit.Text = previousLimit.ToString(CultureInfo.InvariantCulture);
                        return;
                    }
                }
            }

            SettingsService.Instance.Current.SavedCapturesLimit = limit;
            SettingsService.Instance.SaveWithoutNotification();
            ScreenCaptureService.EnforceSavedCaptureLimit();
            SavedLimitChanged?.Invoke();
        }

        private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("General_Export"),
                Filter = LocalizationService.Get("Common_JsonFilter"),
                DefaultExt = ".json",
                AddExtension = true,
                FileName = $"CryoUtils_settings_{DateTime.Now:yyyy-MM-dd}.json"
            };
            bool? ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (ok != true) return;
            try
            {
                SettingsService.Instance.Export(dialog.FileName);
                App.CurrentApp?.ShowToast(LocalizationService.Get("General_ExportDone"));
            }
            catch (Exception ex) { App.CurrentApp?.ShowToast(LocalizationService.Format("Error_Body", ex.Message)); }
        }

        private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Get("General_Import"),
                Filter = LocalizationService.Get("Common_JsonFilter"),
                CheckFileExists = true
            };
            bool? ok = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (ok != true) return;

            var confirm = owner != null
                ? MessageBox.Show(owner, LocalizationService.Get("General_ImportConfirm"), LocalizationService.Get("General_Import"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                : MessageBox.Show(LocalizationService.Get("General_ImportConfirm"), LocalizationService.Get("General_Import"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                SettingsService.Instance.Import(dialog.FileName);
                App.CurrentApp?.ApplyAllSettings();
                FullReloadRequested?.Invoke();
                App.CurrentApp?.ShowToast(LocalizationService.Get("General_ImportDone"));
            }
            catch (Exception ex) { App.CurrentApp?.ShowToast(LocalizationService.Format("Error_Body", ex.Message)); }
        }

        private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var confirm = owner != null
                ? MessageBox.Show(owner, LocalizationService.Get("General_ResetConfirm"), LocalizationService.Get("General_Reset"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(LocalizationService.Get("General_ResetConfirm"), LocalizationService.Get("General_Reset"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            SettingsService.Instance.Reset();
            App.CurrentApp?.ApplyAllSettings();
            FullReloadRequested?.Invoke();
            App.CurrentApp?.ShowToast(LocalizationService.Get("General_ResetDone"));
        }

        private void BtnRepo_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/ZerroDevs/CryoUtils") { UseShellExecute = true }); }
            catch { }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e) =>
            await CheckForUpdatesAsync(silent: false);

        private async Task CheckForUpdatesAsync(bool silent)
        {
            if (_searchingUpdates || _downloadingUpdate) return;
            _searchingUpdates = true;
            BtnCheckUpdates.IsEnabled = false;
            if (!silent) TxtUpdateStatus.Text = LocalizationService.Get("About_Checking");

            var settings = SettingsService.Instance.Current;
            try
            {
                _availableUpdate = await UpdaterService.CheckAsync();
                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                if (_availableUpdate == null)
                {
                    settings.LastUpdateOutcome = "current";
                    settings.LastUpdateVersion = UpdaterService.CurrentVersion;
                    TxtUpdateStatus.Text = LocalizationService.Get("About_UpToDate");
                    UpdateNotesPanel.Visibility = Visibility.Collapsed;
                    BtnDownloadUpdate.Visibility = Visibility.Collapsed;
                }
                else
                {
                    settings.LastUpdateOutcome = "available";
                    settings.LastUpdateVersion = _availableUpdate.Version.ToString();
                    TxtUpdateStatus.Text = LocalizationService.Format("About_UpdateAvailable", _availableUpdate.Version);
                    TxtUpdateNotes.Text = string.IsNullOrWhiteSpace(_availableUpdate.Notes)
                        ? LocalizationService.Get("About_WhatsNew")
                        : _availableUpdate.Notes;
                    UpdateNotesPanel.Visibility = Visibility.Visible;
                    BtnDownloadUpdate.Visibility = Visibility.Visible;
                    BtnDownloadUpdate.IsEnabled = _availableUpdate.HasAsset;
                    if (!_availableUpdate.HasAsset) TxtUpdateStatus.Text = LocalizationService.Get("Update_NoAsset");
                    if (silent) App.CurrentApp?.ShowToast(LocalizationService.Format("About_UpdateAvailable", _availableUpdate.Version));
                }
                SettingsService.Instance.SaveWithoutNotification();
            }
            catch
            {
                settings.LastUpdateOutcome = "failed";
                TxtUpdateStatus.Text = LocalizationService.Get("About_CheckFailed");
                SettingsService.Instance.SaveWithoutNotification();
            }
            finally
            {
                TxtLastChecked.Text = settings.LastUpdateCheckUtc is DateTime checkedUtc
                    ? checkedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    : LocalizationService.Get("About_Never");
                BtnCheckUpdates.IsEnabled = true;
                _searchingUpdates = false;
                RefreshRollbackUi();
                MemoryTrimService.ScheduleTrim(600);
            }
        }

        private async void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate == null || _downloadingUpdate) return;
            var owner = Window.GetWindow(this);
            var info = _availableUpdate;
            if (!info.HasAsset)
            {
                TxtUpdateStatus.Text = LocalizationService.Get("Update_NoAsset");
                BtnDownloadUpdate.IsEnabled = false;
                return;
            }
            var confirm = owner != null
                ? MessageBox.Show(owner, LocalizationService.Format("About_DownloadConfirm", info.Version), LocalizationService.Get("About_Download"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                : MessageBox.Show(LocalizationService.Format("About_DownloadConfirm", info.Version), LocalizationService.Get("About_Download"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            if (SettingsService.Instance.Current.SaveRollbackOnUpdate)
            {
                try { UpdaterService.CreateBackup(); }
                catch (Exception ex)
                {
                    if (owner != null) MessageBox.Show(owner, LocalizationService.Format("Error_Body", ex.Message), LocalizationService.Get("Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            _downloadingUpdate = true;
            BtnDownloadUpdate.IsEnabled = false;
            BtnCheckUpdates.IsEnabled = false;
            UpdateProgress.Visibility = Visibility.Visible;
            UpdateProgress.Value = 0;
            try
            {
                var progress = new Progress<double>(value =>
                {
                    UpdateProgress.Value = Math.Clamp(value * 100, 0, 100);
                    TxtUpdateStatus.Text = LocalizationService.Format("About_Downloading", (int)Math.Round(value * 100));
                });
                string installer = await UpdaterService.DownloadAsync(info, progress);
                TxtUpdateStatus.Text = LocalizationService.Get("Update_Ready");
                UpdaterService.ApplyUpdate(installer);
            }
            catch (Exception ex)
            {
                TxtUpdateStatus.Text = LocalizationService.Get("Update_InstallFailed");
                if (owner != null) MessageBox.Show(owner, LocalizationService.Format("Error_Body", ex.Message), LocalizationService.Get("Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                BtnDownloadUpdate.IsEnabled = true;
                BtnCheckUpdates.IsEnabled = true;
                _downloadingUpdate = false;
                UpdateProgress.Visibility = Visibility.Collapsed;
            }
        }

        public void RefreshRollbackUi()
        {
            bool ready = UpdaterService.IsBackupReady;
            BtnRestore.IsEnabled = ready;
            BtnDeleteBackup.IsEnabled = ready;
            TxtRollbackStatus.Text = ready
                ? LocalizationService.Format("About_BackupReady", string.IsNullOrEmpty(UpdaterService.BackupVersion) ? "?" : UpdaterService.BackupVersion)
                : LocalizationService.Get("About_NoBackup");
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!UpdaterService.IsBackupReady) return;
            var owner = Window.GetWindow(this);
            var confirm = owner != null
                ? MessageBox.Show(owner, LocalizationService.Get("About_RestoreConfirm"), LocalizationService.Get("About_Restore"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(LocalizationService.Get("About_RestoreConfirm"), LocalizationService.Get("About_Restore"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;
            try { UpdaterService.RestoreBackup(); }
            catch (Exception ex) { if (owner != null) MessageBox.Show(owner, LocalizationService.Format("Error_Body", ex.Message), LocalizationService.Get("Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnDeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            if (!UpdaterService.IsBackupReady) return;
            var owner = Window.GetWindow(this);
            var confirm = owner != null
                ? MessageBox.Show(owner, LocalizationService.Get("About_DeleteBackup"), LocalizationService.Get("About_DeleteBackup"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(LocalizationService.Get("About_DeleteBackup"), LocalizationService.Get("About_DeleteBackup"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;
            try
            {
                UpdaterService.DeleteBackup();
                RefreshRollbackUi();
                App.CurrentApp?.ShowToast(LocalizationService.Get("About_BackupDeleted"));
            }
            catch (Exception ex) { if (owner != null) MessageBox.Show(owner, LocalizationService.Format("Error_Body", ex.Message), LocalizationService.Get("Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}
