using System;
using System.Windows.Controls;

namespace CryoUtils.Views.Tabs
{
    internal static class MainTabHelpers
    {
        public static void SelectTag(ComboBox comboBox, string? tag)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(comboItem.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }
            }
        }

        public static string? SelectedTag(ComboBox comboBox) =>
            (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;
    }
}
