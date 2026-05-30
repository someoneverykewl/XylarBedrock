using JemExtensions;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using XylarBedrock.Classes;
using XylarBedrock.Handlers;
using XylarBedrock.UpdateProcessor.Extensions;
using XylarBedrock.ViewModels;

namespace XylarBedrock.Pages.Preview.Installation
{
    /// <summary>
    /// Interaction logic for InstallationVersionSelectScreen.xaml
    /// </summary>
    public partial class EditInstallationVersionSelectScreen : Page
    {
        private EditInstallationVersionSelectViewModel MainContext => (EditInstallationVersionSelectViewModel)DataContext;

        private readonly TaskCompletionSource<string> selectionCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string CurrentSelectedVersionUUID;
        private bool IsDone;
        private string SelectedVersionUUID = string.Empty;

        public EditInstallationVersionSelectScreen(string currentSelectedVersionUuid = "")
        {
            CurrentSelectedVersionUUID = currentSelectedVersionUuid ?? string.Empty;
            DataContext = new EditInstallationVersionSelectViewModel
            {
                SelectedVersionUUID = CurrentSelectedVersionUUID
            };

            InitializeComponent();
        }

        private void CollectionViewSource_Filter(object sender, FilterEventArgs e)
        {
            e.Accepted = Filter(e.Item);
        }

        private bool Filter(object obj)
        {
            MCVersion version = obj as MCVersion;
            if (version == null)
            {
                return false;
            }

            MainContext.Update();

            if (!VersionChooserPolicy.IsVisibleInChooser(version, CurrentSelectedVersionUUID))
            {
                return false;
            }

            bool matchesArchitecture = VersionDbExtensions.DoesVerionArchMatch(Constants.CurrentArchitecture, version.Architecture);
            bool isCurrentSelection = string.Equals(version.UUID, CurrentSelectedVersionUUID, StringComparison.OrdinalIgnoreCase);
            if (!matchesArchitecture && !isCurrentSelection)
            {
                return false;
            }

            string filter = MainContext.FilterString ?? string.Empty;
            string displayName = version.DisplayName ?? version.Name ?? string.Empty;
            return displayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsInitialized && IsLoaded)
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            Dispatcher.Invoke(() =>
            {
                FilterSortingHandler.Refresh(VersionsList.ItemsSource);
                RestoreSelectionIfPossible();
            });
        }

        private void RestoreSelectionIfPossible()
        {
            MCVersion targetVersion = null;

            if (!string.IsNullOrWhiteSpace(MainContext.SelectedVersionUUID))
            {
                targetVersion = MainDataModel.Default.Versions.FirstOrDefault(version =>
                    string.Equals(version.UUID, MainContext.SelectedVersionUUID, StringComparison.OrdinalIgnoreCase) &&
                    Filter(version));
            }

            if (targetVersion == null)
            {
                targetVersion = MainDataModel.Default.Versions.FirstOrDefault(Filter);
            }

            VersionsList.SelectedItem = targetVersion;

            if (targetVersion != null)
            {
                MainContext.SelectedVersion = targetVersion;
                MainContext.SelectedVersionUUID = targetVersion.UUID ?? string.Empty;
                VersionsList.ScrollIntoView(targetVersion);
            }
            else
            {
                MainContext.SelectedVersion = null;
                MainContext.SelectedVersionUUID = string.Empty;
            }
        }

        private void Finish(bool update = false)
        {
            if (IsDone)
            {
                return;
            }

            if (update)
            {
                MCVersion selectedVersion = VersionsList.SelectedItem as MCVersion ?? MainContext.SelectedVersion;
                SelectedVersionUUID = selectedVersion?.UUID ?? string.Empty;
            }

            IsDone = true;
            selectionCompletion.TrySetResult(update ? SelectedVersionUUID : string.Empty);
            MainViewModel.Default.SetDialogFrame(null);
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            Finish(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Finish();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Finish();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await EnsureChooserVersionsLoadedAsync();
            Refresh();
        }

        private async Task EnsureChooserVersionsLoadedAsync()
        {
            if (MainDataModel.Default.Versions.Count == 0)
            {
                await MainDataModel.Default.LoadVersions();
            }

            bool hasVisibleVersion = MainDataModel.Default.Versions.Any(Filter);
            if (!hasVisibleVersion)
            {
                await MainDataModel.Default.LoadVersions(forceStoreCheck: true);
            }
        }

        private void VersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MCVersion selectedVersion = VersionsList.SelectedItem as MCVersion;
            MainContext.SelectedVersion = selectedVersion;
            MainContext.SelectedVersionUUID = selectedVersion?.UUID ?? string.Empty;
        }

        private void VersionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (VersionsList.SelectedItem is MCVersion)
            {
                Finish(true);
            }
        }

        private void VersionsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && VersionsList.SelectedItem is MCVersion)
            {
                e.Handled = true;
                Finish(true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish();
            }
        }

        public Task<string> GetVersionUUID()
        {
            return selectionCompletion.Task;
        }
    }
}
