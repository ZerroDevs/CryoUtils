using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace CryoUtils.Services
{
    public static class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CryoUtils";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value &&
                    !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null)
                {
                    return;
                }

                if (enabled)
                {
                    string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(exe))
                    {
                        key.SetValue(ValueName, $"\"{exe}\" --background");
                    }
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch
            {
                // A locked-down registry simply leaves the toggle off.
            }
        }
    }
}
