using XylarBedrock.Pages.Play.Home;
using XylarBedrock.Pages.Play.Home.Components;
using XylarBedrock.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace XylarBedrock.Pages.Settings.General.Components
{
    public partial class LanguageCombobox : ComboBox
    {
        public LanguageCombobox()
        {
            InitializeComponent();
        }

        private async void LanguageCombobox_DropDownClosed(object sender, EventArgs e)
        {
            var item = this.SelectedItem as XylarBedrock.Localization.Language.LanguageDefinition;
            if (item == null) return;
            XylarBedrock.Localization.Language.LanguageManager.SetLanguage(item.Locale);
            MainDataModel.Default.ProgressBarState.PlayButtonLanguageChanged = !MainDataModel.Default.ProgressBarState.PlayButtonLanguageChanged;
            await Program.OnApplicationRefresh();
        }


        private void ReloadLang()
        {
            var items = XylarBedrock.Localization.Language.LanguageManager.GetResourceDictonaries();
            this.ItemsSource = items;
            string language = XylarBedrock.Localization.Properties.Settings.Default.Language;

            // Set chosen language in language combobox
            if (items.Exists(x => x.Locale.ToString() == language))
            {
                this.SelectedItem = items.Where(x => x.Locale.ToString() == language).FirstOrDefault();
            }
            else
            {
                this.SelectedItem = items.Where(x => x.Locale.ToString() == "en-US").FirstOrDefault();
            }
        }

        private void LanguageCombobox_Initialized(object sender, EventArgs e)
        {

        }

        private void LanguageCombobox_Loaded(object sender, RoutedEventArgs e)
        {
            if (LicenseManager.UsageMode != LicenseUsageMode.Designtime) ReloadLang();
        }
    }
}

