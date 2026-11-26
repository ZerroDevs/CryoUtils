using System.Windows;

namespace CryoUtils.Services
{
    public static class UploadConsentService
    {
        public static bool EnsureUploadAllowed(Window? owner = null)
        {
            if (UploadProviderService.IsLocalProvider)
            {
                return true;
            }

            var settings = SettingsService.Instance.Current;
            if (!settings.CloudUploadEnabled)
            {
                (Application.Current as CryoUtils.App)?.ShowToast(LocalizationService.Get("Upload_Disabled"));
                return false;
            }

            if (settings.CloudUploadConsentAccepted)
            {
                return true;
            }

            var result = owner == null
                ? MessageBox.Show(
                    LocalizationService.Get("Upload_ConsentBody"),
                    LocalizationService.Get("Upload_ConsentTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No)
                : MessageBox.Show(
                    owner,
                    LocalizationService.Get("Upload_ConsentBody"),
                    LocalizationService.Get("Upload_ConsentTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                return false;
            }

            settings.CloudUploadConsentAccepted = true;
            SettingsService.Instance.SaveWithoutNotification();
            return true;
        }
    }
}