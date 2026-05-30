using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using XylarBedrock.Classes;
using XylarBedrock.ViewModels;
using System.Collections.ObjectModel;
using XylarBedrock.UpdateProcessor.Extensions;
using XylarBedrock.Handlers;

namespace XylarBedrock.Pages.Preview.Installation
{


    public partial class EditInstallationScreen : Page
    {

        private bool IsEditMode = false;

        public EditInstallationsPageViewModel ViewModel { get; set; } = new EditInstallationsPageViewModel();


        public EditInstallationScreen(BLInstallation i = null)
        {
            this.DataContext = ViewModel;
            InitializeComponent();
            if (i != null) UpdateEditingFields(i);
            else UpdateAddingFields();
        }
        private void UpdateAddingFields()
        {
            InstallationIconSelect.Init();
        }

        private void UpdateEditingFields(BLInstallation i)
        {
            IsEditMode = true;

            ViewModel.SelectedVersionUUID = i.VersionUUID;
            UpdateSelectedVersionDisplayName();
            ViewModel.InstallationName = i.DisplayName;
            ViewModel.SelectedUUID = i.InstallationUUID;

            InstallationIconSelect.Init(i);

            Header.SetResourceReference(TextBlock.TextProperty, "EditInstallationScreen_AltTitle");
            CreateButton.SetResourceReference(Button.ContentProperty, "EditInstallationScreen_AltCreateButton");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModels.MainViewModel.Default.SetOverlayFrame(null);
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsEditMode) UpdateInstallation();
            else CreateInstallation();
        }

        private MCVersion GetVersion(string uuid)
        {
            return MainDataModel.Default.Versions.Where(x => x.UUID == uuid).FirstOrDefault();
        }

        private void UpdateInstallation()
        {
            MainDataModel.Default.Config.Installation_Edit(ViewModel.SelectedUUID, ViewModel.InstallationName, GetVersion(ViewModel.SelectedVersionUUID), ViewModel.InstallationName, InstallationIconSelect.IconPath, InstallationIconSelect.IsIconCustom);
            MainViewModel.Default.SetOverlayFrame(null);
        }

        private void CreateInstallation()
        {
            MainDataModel.Default.Config.Installation_Create(ViewModel.InstallationName, GetVersion(ViewModel.SelectedVersionUUID), ViewModel.InstallationName, InstallationIconSelect.IconPath, InstallationIconSelect.IsIconCustom);
            MainViewModel.Default.SetOverlayFrame(null);
        }



        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModels.MainViewModel.Default.SetOverlayFrame(null);
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await EnsureChooserVersionsLoadedAsync();

            EnsureVersionSelection();
            RefreshVersions();
        }

        private async Task EnsureChooserVersionsLoadedAsync()
        {
            if (MainDataModel.Default.Versions.Count == 0)
            {
                await MainDataModel.Default.LoadVersions();
            }

            bool hasVisibleVersion = MainDataModel.Default.Versions.Any(version =>
                VersionChooserPolicy.IsVisibleInChooser(version) &&
                VersionDbExtensions.DoesVerionArchMatch(Constants.CurrentArchitecture, version.Architecture));

            if (!hasVisibleVersion)
            {
                await MainDataModel.Default.LoadVersions(forceStoreCheck: true);
            }
        }

        private void RefreshVersions()
        {
            UpdateSelectedVersionDisplayName();
        }

        private async void MoreVersionsButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenVersionChooserAsync();
        }

        private async Task OpenVersionChooserAsync()
        {
            var window = new EditInstallationVersionSelectScreen(ViewModel.SelectedVersionUUID);
            ViewModels.MainViewModel.Default.SetDialogFrame(window);
            string result = await window.GetVersionUUID();
            if (!string.IsNullOrWhiteSpace(result))
            {
                ViewModel.SelectedVersionUUID = result;
                RefreshVersions();
            }
        }

        private async void VersionChooserHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            await OpenVersionChooserAsync();
        }

        private void EnsureVersionSelection()
        {
            if (!string.IsNullOrWhiteSpace(ViewModel.SelectedVersionUUID))
            {
                UpdateSelectedVersionDisplayName();
                return;
            }

            MCVersion firstAvailableVersion = MainDataModel.Default.Versions
                .FirstOrDefault(version =>
                    VersionChooserPolicy.IsVisibleInChooser(version) &&
                    VersionDbExtensions.DoesVerionArchMatch(Constants.CurrentArchitecture, version.Architecture));

            if (firstAvailableVersion == null)
            {
                ViewModel.SelectedVersionUUID = Constants.LATEST_RELEASE_UUID;
                UpdateSelectedVersionDisplayName();
                return;
            }

            ViewModel.SelectedVersionUUID = firstAvailableVersion.UUID;
            UpdateSelectedVersionDisplayName();
        }

        private void UpdateSelectedVersionDisplayName()
        {
            var version = GetVersion(ViewModel.SelectedVersionUUID);
            if (version != null)
            {
                ViewModel.SelectedVersionDisplayName = version.DisplayName;
                return;
            }

            if (ViewModel.SelectedVersionUUID == Constants.LATEST_RELEASE_UUID)
            {
                ViewModel.SelectedVersionDisplayName = "Minecraft for Windows";
                return;
            }

            ViewModel.SelectedVersionDisplayName = "Choose a version";
        }
    }
}

