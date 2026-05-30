using XylarBedrock.Classes;
using XylarBedrock.Enums;
using XylarBedrock.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XylarBedrock.Classes.Launcher;
using XylarBedrock.UpdateProcessor.Extensions;
using XylarBedrock.UpdateProcessor.Enums;
using System.Collections.Generic;
using XylarBedrock.Handlers;

namespace XylarBedrock.Pages.Play.Home
{
    public partial class PlayScreenPage : Page
    {
        private Window ownerWindow;
        private bool isSyncingPlayableVersionSelection;

        public PlayScreenPage()
        {
            InitializeComponent();
            Loaded += PlayScreenPage_Loaded;
            Unloaded += PlayScreenPage_Unloaded;
            ((INotifyPropertyChanged)MainDataModel.Default.ProgressBarState).PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainDataModel.Default.ProgressBarState.AllowPlaying) ||
                    e.PropertyName == nameof(MainDataModel.Default.ProgressBarState.IsGameRunning) ||
                    e.PropertyName == nameof(MainDataModel.Default.ProgressBarState.PlayButtonLanguageChanged) ||
                    e.PropertyName == nameof(MainDataModel.Default.ProgressBarState.Show) ||
                    e.PropertyName == nameof(MainDataModel.Default.ProgressBarState.CurrentState) ||
                    e.PropertyName == nameof(MainDataModel.Default.ProgressBarState.Description) ||
                    e.PropertyName == nameof(MainDataModel.Default.ProgressBarState.Information))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CheckVersionAvailability(s, e);
                        UpdateLaunchStatus();
                    });
                }
            };
        }

        private async void PlayScreenPage_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureLauncherConfigReady();

            ownerWindow = Window.GetWindow(this);
            if (ownerWindow != null)
            {
                ownerWindow.Activated -= OwnerWindow_Activated;
                ownerWindow.Activated += OwnerWindow_Activated;
            }

            await EnsurePlayableVersionsLoadedAsync();
            RefreshPlayableVersionSelector();
            CheckVersionAvailability(sender, e);
            UpdateLaunchStatus();
        }

        private void PlayScreenPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (ownerWindow != null)
            {
                ownerWindow.Activated -= OwnerWindow_Activated;
                ownerWindow = null;
            }
        }

        private void OwnerWindow_Activated(object sender, EventArgs e)
        {
            EnsureLauncherConfigReady();
            RefreshPlayableVersionSelector();
            CheckVersionAvailability(sender, e);
        }

        private async System.Threading.Tasks.Task EnsurePlayableVersionsLoadedAsync()
        {
            if (MainDataModel.Default.Versions.Count == 0)
            {
                await MainDataModel.Default.LoadVersions(forceStoreCheck: true);
                return;
            }

            if (!GetPlayableVersions().Any())
            {
                await MainDataModel.Default.LoadVersions(forceStoreCheck: true);
            }
        }

        private IEnumerable<MCVersion> GetPlayableVersions()
        {
            if (!MainDataModel.Default.PackageManager.IsOfficialStoreReleaseInstalled())
            {
                return Enumerable.Empty<MCVersion>();
            }

            List<MCVersion> installedStoreMatches = MainDataModel.Default.Versions.Where(version =>
                !version.IsCustom &&
                !string.Equals(version.UUID, Constants.LATEST_RELEASE_UUID, StringComparison.OrdinalIgnoreCase) &&
                VersionDbExtensions.DoesVerionArchMatch(Constants.CurrentArchitecture, version.Architecture) &&
                version.MatchesOfficialStoreRelease)
                .ToList();

            if (installedStoreMatches.Count != 0)
            {
                return installedStoreMatches;
            }

            MCVersion officialStoreFallback = GetOfficialStorePlayableVersion();
            return officialStoreFallback == null
                ? Enumerable.Empty<MCVersion>()
                : new[] { officialStoreFallback };
        }

        private void RefreshPlayableVersionSelector()
        {
            EnsureLauncherConfigReady();

            var versions = GetPlayableVersions().ToList();
            PlayVersionsList.ItemsSource = versions;

            BLInstallation selectedInstallation = ResolveSelectedInstallation();
            string selectedVersionUuid = selectedInstallation?.VersionUUID ?? string.Empty;
            MCVersion selectedVersion = versions.FirstOrDefault(version =>
                string.Equals(version.UUID, selectedVersionUuid, StringComparison.OrdinalIgnoreCase));

            if (selectedVersion == null)
            {
                selectedVersion = versions.FirstOrDefault(version => version.MatchesOfficialStoreRelease)
                                  ?? versions.FirstOrDefault();
            }

            isSyncingPlayableVersionSelection = true;
            try
            {
                PlayVersionsList.SelectedItem = selectedVersion;
            }
            finally
            {
                isSyncingPlayableVersionSelection = false;
            }
        }

        private void PlayVersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isSyncingPlayableVersionSelection)
            {
                return;
            }

            if (PlayVersionsList.SelectedItem is MCVersion selectedVersion)
            {
                MainDataModel.Default.Config.SelectInstallationForVersion(selectedVersion);
                CheckVersionAvailability(sender, e);
            }
        }

        private void CheckVersionAvailability(object _, EventArgs __)
        {
            EnsureLauncherConfigReady();

            BLInstallation selectedInstallation = ResolveSelectedInstallation();
            MCVersion playableVersion = ResolvePlayableVersion(selectedInstallation);
            BundledDllPackDiagnostics bundledDllPack = MainDataModel.Default.PackageManager.GetBundledDllPackDiagnostics(playableVersion);

            if (MainDataModel.Default.PackageManager.isGameRunning)
            {
                MainPlayButton.IsEnabled = true;
                ApplyButtonDetails(null);
                ApplyPlayButtonStyle(MainDataModel.Default.ProgressBarState.PlayButtonString, Brushes.White, 26);
            }
            else if (!bundledDllPack.IsReady)
            {
                MainPlayButton.IsEnabled = false;
                ApplyButtonDetails(bundledDllPack.DetailsText);
                ApplyStoreButtonStyle(GetBundledDllButtonText(bundledDllPack), Brushes.LightGray, 18);
            }
            else if (!MainDataModel.Default.PackageManager.IsBundledModInstalled(playableVersion))
            {
                MainPlayButton.IsEnabled = MainDataModel.Default.ProgressBarState.AllowPlaying;
                ApplyButtonDetails("The bundled mod pack is ready. Click GET MODS to copy it into your Minecraft profile.");
                ApplyModsButtonStyle("GET MODS", Brushes.White, 22);
            }
            else if (selectedInstallation is null)
            {
                MainPlayButton.IsEnabled = false;
                ApplyButtonDetails("No valid Minecraft installation is selected right now. Reopen the launcher once or pick another version.");
                ApplyStoreButtonStyle("Select Installation", Brushes.LightGray, 18);
            }
            else if (playableVersion is null)
            {
                MainPlayButton.IsEnabled = false;
                ApplyButtonDetails("The selected Minecraft version is not ready yet. Reopen the launcher once if this keeps happening.");
                ApplyStoreButtonStyle("Select Installation", Brushes.LightGray, 18);
            }
            else if (!selectedInstallation.IsInstalledVersion)
            {
                MainPlayButton.IsEnabled = MainDataModel.Default.ProgressBarState.AllowPlaying;
                ApplyButtonDetails("Install the selected Minecraft version once. After that this button will switch to PLAY.");
                ApplyInstallButtonStyle("INSTALL NOW", Brushes.White, 22);
            }
            else
            {
                MainPlayButton.IsEnabled = MainDataModel.Default.ProgressBarState.AllowPlaying;
                ApplyButtonDetails(null);
                ApplyPlayButtonStyle("PLAY", Brushes.White, 26);
            }
        }

        private void ApplyPlayButtonStyle(string text, Brush foreground, double fontSize)
        {
            MainPlayButton.Style = (Style)FindResource("BigGreenButton");
            MainPlayButton.Width = 250;
            PlayButtonText.Text = text;
            PlayButtonText.Foreground = foreground;
            PlayButtonText.FontSize = fontSize;
        }

        private void ApplyInstallButtonStyle(string text, Brush foreground, double fontSize)
        {
            MainPlayButton.Style = (Style)FindResource("BigGreenButton");
            MainPlayButton.Width = 250;
            PlayButtonText.Text = text;
            PlayButtonText.Foreground = foreground;
            PlayButtonText.FontSize = fontSize;
        }

        private void ApplyStoreButtonStyle(string text, Brush foreground, double fontSize)
        {
            MainPlayButton.Style = (Style)FindResource("BigStoreButton");
            MainPlayButton.Width = 340;
            PlayButtonText.Text = text;
            PlayButtonText.Foreground = foreground;
            PlayButtonText.FontSize = fontSize;
        }

        private void ApplyModsButtonStyle(string text, Brush foreground, double fontSize)
        {
            MainPlayButton.Style = (Style)FindResource("BigModsButton");
            MainPlayButton.Width = 250;
            PlayButtonText.Text = text;
            PlayButtonText.Foreground = foreground;
            PlayButtonText.FontSize = fontSize;
        }

        private void ApplyButtonDetails(string details)
        {
            ToolTipService.SetShowOnDisabled(MainPlayButton, true);
            MainPlayButton.ToolTip = string.IsNullOrWhiteSpace(details) ? null : details;
        }

        private void UpdateLaunchStatus()
        {
            LauncherState currentState = MainDataModel.Default.ProgressBarState.CurrentState;
            bool showLaunchStatus = MainDataModel.Default.ProgressBarState.Show &&
                                    currentState == LauncherState.isLaunching;

            LaunchStatusPanel.Visibility = showLaunchStatus ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            LaunchStatusBar.IsIndeterminate = true;
            LaunchStatusText.Text = "Opening the game...";
        }

        private BLInstallation ResolveSelectedInstallation()
        {
            EnsureLauncherConfigReady();

            if (PlayVersionsList.SelectedItem is MCVersion selectedVersion)
            {
                BLInstallation selectedFromVersion = MainDataModel.Default.Config.SelectInstallationForVersion(selectedVersion);
                if (selectedFromVersion != null)
                {
                    return selectedFromVersion;
                }
            }

            MCVersion activeVersion = GetPlayableVersions().FirstOrDefault() ?? GetOfficialStorePlayableVersion();
            return activeVersion == null ? null : MainDataModel.Default.Config.SelectInstallationForVersion(activeVersion);
        }

        private MCVersion ResolvePlayableVersion(BLInstallation installation)
        {
            if (installation == null)
            {
                return null;
            }

            if (installation.Version != null)
            {
                return installation.Version;
            }

            if (installation.IsOfficialInstallation && MainDataModel.Default.PackageManager.IsOfficialStoreReleaseInstalled())
            {
                return GetOfficialStorePlayableVersion();
            }

            return null;
        }

        private MCVersion GetOfficialStorePlayableVersion()
        {
            if (!MainDataModel.Default.PackageManager.IsOfficialStoreReleaseInstalled())
            {
                return null;
            }

            return MainDataModel.Default.PackageManager.VersionDownloader.GetVersion(
                VersioningMode.LatestRelease,
                Constants.LATEST_RELEASE_UUID);
        }

        private void EnsureLauncherConfigReady()
        {
            if (MainDataModel.Default.Config?.CurrentProfile != null &&
                MainDataModel.Default.Config?.CurrentInstallations != null &&
                MainDataModel.Default.Config.CurrentInstallations.Count != 0)
            {
                return;
            }

            MainDataModel.Default.LoadConfig();
        }

        private static string GetBundledDllButtonText(BundledDllPackDiagnostics diagnostics)
        {
            if (!diagnostics.DllDirectoryExists) return "Missing dll Folder.";
            if (!diagnostics.ModDllExists) return $"Missing {diagnostics.ModDllName}";
            if (!diagnostics.RuntimeDllExists) return $"Missing {Constants.EXTRA_DLL_NAME}";
            if (!diagnostics.ModDllReadable || !diagnostics.RuntimeDllReadable) return "DLL Pack Unreadable";
            return "Missing DLL Pack";
        }

        private string GetLatestImage()
        {
            return Constants.Themes.First().Value;
        }

        private string GetCustomImage(string result)
        {
            DirectoryInfo directoryInfo = Directory.CreateDirectory(MainDataModel.Default.FilePaths.ThemesFolder);
            foreach (var file in directoryInfo.GetFiles())
            {
                if (file.Name == result) return file.FullName;
            }

            return Constants.Themes.Where(x => x.Key == "Original").FirstOrDefault().Value;
        }

        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                string packUri = string.Empty;
                string currentTheme = Properties.LauncherSettings.Default.CurrentTheme;

                bool isBugRock = Handlers.RuntimeHandler.IsBugRockOfTheWeek();
                if (isBugRock)
                {
                    BedrockLogo.Visibility = Visibility.Collapsed;
                    BugrockLogo.Visibility = Visibility.Visible;
                    BugrockOfTheWeekLogo.Visibility = Visibility.Visible;
                }
                else
                {
                    BedrockLogo.Visibility = Visibility.Visible;
                    BugrockLogo.Visibility = Visibility.Collapsed;
                    BugrockOfTheWeekLogo.Visibility = Visibility.Collapsed;
                }

                if (currentTheme.StartsWith(Constants.ThemesCustomPrefix))
                {
                    packUri = GetCustomImage(currentTheme.Remove(0, Constants.ThemesCustomPrefix.Length));
                }
                else
                {
                    switch (currentTheme)
                    {
                        case "LatestUpdate":
                            packUri = GetLatestImage();
                            break;
                        default:
                            if (Constants.Themes.ContainsKey(currentTheme)) packUri = Constants.Themes.Where(x => x.Key == currentTheme).FirstOrDefault().Value;
                            else packUri = Constants.Themes.Where(x => x.Key == "Original").FirstOrDefault().Value;
                            break;
                    }
                }

                try
                {
                    BitmapImage bmp = new BitmapImage(new Uri(packUri, UriKind.Absolute));
                    ImageBrush.ImageSource = bmp;
                }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                }
            });
        }

        private async void MainPlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainDataModel.Default.PackageManager.isGameRunning)
            {
                MainDataModel.Default.KillGame();
            }
            else if (!MainDataModel.Default.PackageManager.IsBundledModInstalled(ResolvePlayableVersion(ResolveSelectedInstallation())))
            {
                bool installed = await MainDataModel.Default.PackageManager.InstallBundledModAsync(ResolvePlayableVersion(ResolveSelectedInstallation()));
                if (installed)
                {
                    CheckVersionAvailability(sender, e);
                }
            }
            else
            {
                BLInstallation i = ResolveSelectedInstallation();
                if (i == null)
                {
                    MessageBox.Show(
                        "No valid Minecraft installation is selected right now. Reopen the launcher once and try Play again.",
                        App.DisplayName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                MCVersion playableVersion = ResolvePlayableVersion(i);
                if (playableVersion == null)
                {
                    MessageBox.Show(
                        "The selected Minecraft version is not ready yet. Reopen the launcher once, then try again.",
                        App.DisplayName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                bool keepLauncherOpen = Properties.LauncherSettings.Default.KeepLauncherOpen;

                if (!i.IsInstalledVersion)
                {
                    bool installed = await MainDataModel.Default.Install(
                        MainDataModel.Default.Config.CurrentProfile,
                        i,
                        launchAfterInstall: true,
                        keepLauncherOpen: keepLauncherOpen,
                        launchEditor: false);
                    if (installed)
                    {
                        CheckVersionAvailability(sender, e);
                    }

                    return;
                }

                MainDataModel.Default.Play(MainDataModel.Default.Config.CurrentProfile, i, keepLauncherOpen, false);
            }
        }

    }
}
