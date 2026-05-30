using System;
using System.Threading.Tasks;
using XylarBedrock.Classes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Linq;
using PropertyChanged;
using XylarBedrock.Handlers;
using System.Windows.Threading;
using XylarBedrock.Backend.Backporting;
using XylarBedrock.Enums;

namespace XylarBedrock.ViewModels
{

    [AddINotifyPropertyChangedInterface]    //119 Lines
    public class MainDataModel
    {
        public static MainDataModel Default { get; set; } = new MainDataModel();

        public static IBackwardsCommunication BackwardsCommunicationHost { get; private set; }
        public static void SetBackwardsCommunicationHost(IBackwardsCommunication host)
        {
            BackwardsCommunicationHost = host;
        }

        #region Properties

        public static UpdateHandler Updater { get; set; } = new UpdateHandler();
        public ProgressBarModel ProgressBarState { get; set; } = new ProgressBarModel();
        public PathHandler FilePaths { get; private set; } = new PathHandler();
        public PackageHandler PackageManager { get; set; } = new PackageHandler();
        public BLProfileList Config { get; private set; } = new BLProfileList();
        public ObservableCollection<MCVersion> Versions { get; private set; } = new ObservableCollection<MCVersion>();
        private readonly object versionsLoadSync = new object();
        private Task activeVersionsLoadTask = Task.CompletedTask;


        public bool AllowedToCloseWithGameOpen { get; set; } = false;
        public bool IsVersionsUpdating { get; private set; }


        #endregion

        #region Methods

        public async Task LoadVersions(bool onLoad = false, bool forceStoreCheck = false)
        {
            Task taskToAwait;

            while (true)
            {
                lock (versionsLoadSync)
                {
                    if (activeVersionsLoadTask == null || activeVersionsLoadTask.IsCompleted)
                    {
                        activeVersionsLoadTask = LoadVersionsCore(onLoad, forceStoreCheck);
                        taskToAwait = activeVersionsLoadTask;
                        break;
                    }

                    taskToAwait = activeVersionsLoadTask;
                }

                await taskToAwait;

                if (!forceStoreCheck)
                {
                    return;
                }
            }

            await taskToAwait;
        }

        private async Task LoadVersionsCore(bool onLoad = false, bool forceStoreCheck = false)
        {
            IsVersionsUpdating = true;

            try
            {
                if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
                {
                    await PackageManager.VersionDownloader.UpdateVersionList(Versions, onLoad, forceStoreCheck);
                    Config?.SyncVisibleInstallationsFromVersions();
                    return;
                }

                await await Application.Current.Dispatcher.InvokeAsync(
                    async () =>
                    {
                        await PackageManager.VersionDownloader.UpdateVersionList(Versions, onLoad, forceStoreCheck);
                        Config?.SyncVisibleInstallationsFromVersions();
                    });
            }
            finally
            {
                IsVersionsUpdating = false;
            }
        }
        public void LoadConfig()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Config = BLProfileList.Load(FilePaths.GetProfilesFilePath(), Properties.LauncherSettings.Default.CurrentProfileUUID, Properties.LauncherSettings.Default.CurrentInstallationUUID);
                if (Versions.Count != 0)
                {
                    Config.SyncVisibleInstallationsFromVersions();
                }
            });
        }
        public async void KillGame() => await PackageManager.ClosePackage();
        public async void RepairVersion(MCVersion v)
        {
            bool installed = await PackageManager.DownloadPackage(v);
            if (!installed)
            {
                return;
            }

            await LaunchVersionAfterCatalogInstall(v);
        }
        public async void RemoveVersion(MCVersion v) => await PackageManager.RemovePackage(v);
        public async void Play(BLProfile p, BLInstallation i, bool KeepLauncherOpen, bool LaunchEditor, bool Save = true)
        {
            if (p == null)
            {
                MessageBox.Show(
                    "No launcher profile is selected right now. Reopen XylarBedrock once, then try Play again.",
                    App.DisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (i == null)
            {
                MessageBox.Show(
                    "No valid Minecraft installation is selected right now. Reopen XylarBedrock once, then try Play again.",
                    App.DisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            i.LastPlayed = DateTime.Now;
            MainDataModel.Default.Config.Installation_UpdateLP(i);

            if (Save)
            {
                Properties.LauncherSettings.Default.CurrentInstallationUUID = i.InstallationUUID;
                Properties.LauncherSettings.Default.Save();
            }

            MCVersion version = ResolvePlayableVersion(i);
            if (version == null)
            {
                MessageBox.Show(
                    "The selected Minecraft version is not available right now. Refresh the launcher once or choose another version.",
                    App.DisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string path = FilePaths.GetInstallationPackageDataPath(p.UUID, i.DirectoryName_Full);
            bool installed = await PackageManager.InstallPackage(version, path);
            if (!installed)
            {
                return;
            }

            await PackageManager.LaunchPackage(version, path, KeepLauncherOpen, LaunchEditor);
        }

        public async Task<bool> Install(
            BLProfile p,
            BLInstallation i,
            bool launchAfterInstall = false,
            bool keepLauncherOpen = false,
            bool launchEditor = false)
        {
            if (p == null || i == null) return false;

            MCVersion version = ResolvePlayableVersion(i);
            if (version == null)
            {
                return false;
            }

            Properties.LauncherSettings.Default.CurrentInstallationUUID = i.InstallationUUID;
            Properties.LauncherSettings.Default.Save();

            string path = MainDataModel.Default.FilePaths.GetInstallationPackageDataPath(p.UUID, i.DirectoryName_Full);
            bool installed = await PackageManager.InstallPackage(version, path);
            if (!installed)
            {
                return false;
            }

            await LoadVersions();

            if (launchAfterInstall)
            {
                i.LastPlayed = DateTime.Now;
                MainDataModel.Default.Config.Installation_UpdateLP(i);
                await PackageManager.LaunchPackage(version, path, keepLauncherOpen, launchEditor);
            }

            return true;
        }

        private async Task LaunchVersionAfterCatalogInstall(MCVersion requestedVersion)
        {
            await LoadVersions(forceStoreCheck: true);

            string activeStoreVersion = PackageManager.GetOfficialStorePackageVersionString();
            MCVersion versionToLaunch = Versions.FirstOrDefault(version =>
                    version.IsRelease &&
                    MCVersion.DisplayVersionsMatch(activeStoreVersion, version.Name)) ??
                Versions.FirstOrDefault(version =>
                    requestedVersion != null &&
                    string.Equals(version.UUID, requestedVersion.UUID, StringComparison.OrdinalIgnoreCase)) ??
                requestedVersion;

            if (versionToLaunch == null)
            {
                MessageBox.Show(
                    "Minecraft finished installing, but the launcher could not select the installed version automatically. Press Play once.",
                    App.DisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            BLInstallation installation = Config.SelectInstallationForVersion(versionToLaunch);
            if (installation == null)
            {
                MessageBox.Show(
                    "Minecraft finished installing, but the launcher could not create a playable entry automatically. Press Play once.",
                    App.DisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Play(Config.CurrentProfile, installation, Properties.LauncherSettings.Default.KeepLauncherOpen, false);
        }

        private MCVersion ResolvePlayableVersion(BLInstallation installation)
        {
            if (installation == null)
            {
                return null;
            }

            MCVersion version = installation.Version;
            if (version != null)
            {
                return version;
            }

            if (installation.IsOfficialInstallation && PackageManager.IsOfficialStoreReleaseInstalled())
            {
                return PackageManager.VersionDownloader.GetVersion(installation.VersioningMode, installation.VersionUUID);
            }

            return null;
        }

        #endregion
    }
}


