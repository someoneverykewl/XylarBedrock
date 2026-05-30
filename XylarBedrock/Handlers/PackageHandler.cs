using XylarBedrock.Classes;
using XylarBedrock.Downloaders;
using JemExtensions;
using SymbolicLinkSupport;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Management.Deployment;
using Windows.System;
using ZipProgress = JemExtensions.ZipFileExtensions.ZipProgress;
using XylarBedrock.Enums;
using System.Windows.Input;
using XylarBedrock.ViewModels;
using XylarBedrock.Exceptions;
using XylarBedrock.UpdateProcessor;
using XylarBedrock.UpdateProcessor.Authentication;
using XylarBedrock.UpdateProcessor.Handlers;
using XylarBedrock.Classes.Launcher;
using Windows.System.Diagnostics;
using XylarBedrock.UpdateProcessor.Enums;
using JemExtensions.WPF.Commands;
using XylarBedrock.UI.Pages.Common;
using System.Collections;
using XylarBedrock.UpdateProcessor.Classes;

namespace XylarBedrock.Handlers
{
    public class PackageHandler : IDisposable
    {
        private CancellationTokenSource CancelSource = new CancellationTokenSource();
        private StoreNetwork StoreNetwork = new StoreNetwork();
        private PackageManager PM = new PackageManager();
        private readonly object LaunchMethodLock = new object();

        public VersionDownloader VersionDownloader { get; private set; } = new VersionDownloader();
        public Process GameHandle { get; private set; } = null;
        public bool isGameRunning { get => GameHandle != null; }
        public string LastLaunchMethodAttempted { get; private set; } = "No launch attempted yet.";

        #region Public Methods

        public async Task LaunchPackage(MCVersion v, string dirPath, bool KeepLauncherOpen, bool LaunchEditor)
        {
            try
            {
                if (v == null)
                {
                    ShowLauncherMessage(
                        App.DisplayName,
                        "No Minecraft installation is currently selected. Pick one, then press Play again.",
                        MessageBoxImage.Warning);
                    return;
                }

                MainDataModel.Default.ProgressBarState.ResetProgressBarProgress();
                MainDataModel.Default.ProgressBarState.SetProgressBarText("Opening the game...");
                MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isLaunching);
                MainDataModel.Default.ProgressBarState.SetProgressBarVisibility(true);
                bool launchRequested = await TryLaunchMinecraftAsync(v, LaunchEditor);

                if (launchRequested)
                {
                    Trace.WriteLine("App launch finished!");

                    await GetGameHandle(GetKnownMinecraftProcessNames(v.Type));
                    EndTask();

                    if (!KeepLauncherOpen)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => Application.Current.MainWindow.Close());
                    }
                }
                else
                {
                    EndTask();
                    string message = LaunchEditor
                        ? $"XylarBedrock could not open the Minecraft Editor through the {Constants.GetUri(v.Type)} protocol."
                        : "Minecraft for Windows could not be started from the selected version. Reinstall that version once, then press PLAY again.";
                    Trace.WriteLine(message);
                    ShowLauncherMessage(App.DisplayName, message, MessageBoxImage.Warning);
                }
            }
            catch (Exception e)
            {
                EndTask();
                Trace.WriteLine($"LaunchPackage failed: {e}");
                ShowLauncherMessage(
                    App.DisplayName,
                    "The selected Minecraft version could not be started this time. Close the launcher once, reopen it, and try again.",
                    MessageBoxImage.Warning);
            }
        }

        public bool IsOfficialStoreReleaseInstalled()
        {
            return GetInstalledMinecraftPackages(VersionType.Release).Any();
        }

        public string GetOfficialStorePackageVersionString()
        {
            var package = GetInstalledMinecraftPackages(VersionType.Release)
                .OrderByDescending(pkg => pkg.Id.Version.Major)
                .ThenByDescending(pkg => pkg.Id.Version.Minor)
                .ThenByDescending(pkg => pkg.Id.Version.Build)
                .ThenByDescending(pkg => pkg.Id.Version.Revision)
                .FirstOrDefault();

            if (package == null) return string.Empty;

            PackageVersion version = package.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }

        public IReadOnlyList<string> GetInstalledMinecraftDirectories(VersionType type)
        {
            HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Package package in GetInstalledMinecraftPackages(type))
            {
                AddMinecraftDirectory(directories, GetInstalledLocationPath(package));
            }

            if (type == VersionType.Release)
            {
                AddMinecraftDirectory(directories, @"C:\XboxGames\Minecraft for Windows\Content");
            }

            return directories.ToList();
        }

        public IReadOnlyList<string> GetPreviewOrLocalMinecraftDirectories()
        {
            HashSet<string> paths = new HashSet<string>(GetInstalledMinecraftDirectories(VersionType.Preview), StringComparer.OrdinalIgnoreCase);
            string versionsFolder = MainDataModel.Default.FilePaths.VersionsFolder;

            try
            {
                if (Directory.Exists(versionsFolder))
                {
                    foreach (string directory in Directory.GetDirectories(versionsFolder))
                    {
                        if (File.Exists(Path.Combine(directory, "AppxManifest.xml")) ||
                            File.Exists(Path.Combine(directory, "Minecraft.Windows.exe")) ||
                            File.Exists(Path.Combine(directory, "Minecraft.WindowsBeta.exe")))
                        {
                            paths.Add(directory);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not inspect local Minecraft directories in '{versionsFolder}': {ex}");
            }

            return paths.ToList();
        }

        public LauncherSupportDiagnostics GetSupportDiagnostics()
        {
            return new LauncherSupportDiagnostics
            {
                OfficialStoreReleaseDetected = IsOfficialStoreReleaseInstalled(),
                OfficialStoreReleaseDirectories = GetInstalledMinecraftDirectories(VersionType.Release),
                PreviewOrLocalDirectories = GetPreviewOrLocalMinecraftDirectories(),
                BundledDllPack = GetBundledDllPackDiagnostics(),
                LastLaunchMethodAttempted = LastLaunchMethodAttempted
            };
        }

        public void ShowOfficialStoreRequirementMessage()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    "XylarBedrock now works only with the original Minecraft for Windows app from Microsoft Store. Install it first, even with the free trial, then reopen the launcher.",
                    App.DisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
        }

        public async Task OpenOfficialStorePage()
        {
            Uri storeUri = new Uri(Constants.MINECRAFT_STORE_URI);
            bool opened = false;

            try
            {
                opened = await Launcher.LaunchUriAsync(storeUri);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Unable to open Microsoft Store URI: {ex}");
            }

            if (!opened)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Constants.MINECRAFT_STORE_WEB_URL,
                    UseShellExecute = true
                });
            }
        }

        public string GetModsDirectoryPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Minecraft Bedrock",
                Constants.MODS_FOLDER_NAME);
        }

        public string GetBundledDllDirectoryPath()
        {
            return Path.Combine(MainDataModel.Default.FilePaths.ExecutableDirectory, "dll");
        }

        public string GetBundledModSourcePath()
        {
            return GetBundledModSourcePath(GetDetectedMinecraftVersionForDllSelection());
        }

        public string GetBundledModSourcePath(MCVersion version)
        {
            return Path.Combine(GetBundledDllDirectoryPath(), GetBundledModDllNameForVersion(version));
        }

        public string GetBundledExtraDllSourcePath()
        {
            return Path.Combine(GetBundledDllDirectoryPath(), Constants.EXTRA_DLL_NAME);
        }

        public string GetInstalledModPath()
        {
            return GetInstalledModPath(GetDetectedMinecraftVersionForDllSelection());
        }

        public string GetInstalledModPath(MCVersion version)
        {
            return Path.Combine(GetModsDirectoryPath(), GetBundledModDllNameForVersion(version));
        }

        public string GetInstalledExtraDllPath()
        {
            return Path.Combine(GetModsDirectoryPath(), Constants.EXTRA_DLL_NAME);
        }

        public bool IsBundledModInstalled()
        {
            return IsBundledModInstalled(GetDetectedMinecraftVersionForDllSelection());
        }

        public bool IsBundledModInstalled(MCVersion version)
        {
            return IsBundledFileInstalled(GetBundledModSourcePath(version), GetInstalledModPath(version))
                && IsBundledFileInstalled(GetBundledExtraDllSourcePath(), GetInstalledExtraDllPath())
                && !GetInactiveInstalledModPaths(version).Any(File.Exists);
        }

        public bool HasBundledModSource()
        {
            return GetBundledDllPackDiagnostics().IsReady;
        }

        public BundledDllPackDiagnostics GetBundledDllPackDiagnostics()
        {
            return GetBundledDllPackDiagnostics(GetDetectedMinecraftVersionForDllSelection());
        }

        public BundledDllPackDiagnostics GetBundledDllPackDiagnostics(MCVersion version)
        {
            string executableDirectoryPath = MainDataModel.Default.FilePaths.ExecutableDirectory;
            string dllDirectoryPath = GetBundledDllDirectoryPath();
            string modDllName = GetBundledModDllNameForVersion(version);
            string modDllPath = GetBundledModSourcePath(version);
            string runtimeDllPath = GetBundledExtraDllSourcePath();

            bool dllDirectoryExists = Directory.Exists(dllDirectoryPath);
            bool modDllExists = File.Exists(modDllPath);
            bool runtimeDllExists = File.Exists(runtimeDllPath);
            bool modDllReadable = modDllExists && CanReadFile(modDllPath);
            bool runtimeDllReadable = runtimeDllExists && CanReadFile(runtimeDllPath);

            BundledDllPackDiagnostics diagnostics = new BundledDllPackDiagnostics
            {
                ExecutableDirectoryPath = executableDirectoryPath,
                DllDirectoryPath = dllDirectoryPath,
                ModDllName = modDllName,
                ModDllPath = modDllPath,
                RuntimeDllPath = runtimeDllPath,
                DllDirectoryExists = dllDirectoryExists,
                ModDllExists = modDllExists,
                RuntimeDllExists = runtimeDllExists,
                ModDllReadable = modDllReadable,
                RuntimeDllReadable = runtimeDllReadable
            };

            diagnostics.StatusText = GetBundledDllStatusText(diagnostics);
            diagnostics.DetailsText = GetBundledDllDetailsText(diagnostics);

            return diagnostics;
        }

        public async Task<bool> InstallBundledModAsync()
        {
            return await InstallBundledModAsync(GetDetectedMinecraftVersionForDllSelection());
        }

        public async Task<bool> InstallBundledModAsync(MCVersion version)
        {
            return await InstallBundledModInternalAsync(showMessage: true, forceInstall: true, version: version);
        }

        public async Task<bool> AutoRefreshBundledModAsync()
        {
            string currentMinecraftVersion = GetOfficialStorePackageVersionString();
            bool versionChanged = !string.IsNullOrWhiteSpace(currentMinecraftVersion) &&
                                  !string.Equals(Properties.LauncherSettings.Default.LastPatchedMinecraftVersion, currentMinecraftVersion, StringComparison.OrdinalIgnoreCase);

            return await InstallBundledModInternalAsync(
                showMessage: false,
                forceInstall: versionChanged,
                version: CreateDllSelectionVersion(currentMinecraftVersion));
        }

        private async Task<bool> InstallBundledModInternalAsync(bool showMessage, bool forceInstall, MCVersion version)
        {
            BundledDllPackDiagnostics dllPack = GetBundledDllPackDiagnostics(version);
            string sourcePath = dllPack.ModDllPath;
            string extraDllSourcePath = dllPack.RuntimeDllPath;
            string installedPath = GetInstalledModPath(version);
            string installedExtraDllPath = GetInstalledExtraDllPath();

            if (!dllPack.IsReady)
            {
                if (showMessage)
                {
                    ShowLauncherMessage(App.DisplayName, dllPack.DetailsText, MessageBoxImage.Error);
                }

                return false;
            }

            if (!forceInstall && IsBundledModInstalled(version)) return true;

            await Task.Run(() =>
            {
                Directory.CreateDirectory(GetModsDirectoryPath());
                File.Copy(sourcePath, installedPath, true);
                File.Copy(extraDllSourcePath, installedExtraDllPath, true);
                RemoveInactiveInstalledModFiles(version);

                // finally here its to make minecraft crack work, finally...
                var uwpDirs = GetInstalledMinecraftDirectories(VersionType.Release);
                foreach (string uwpDir in uwpDirs)
                {
                    string uwpExtraDllPath = Path.Combine(uwpDir, Constants.EXTRA_DLL_NAME);
                    File.Copy(extraDllSourcePath, uwpExtraDllPath, true);
                }
            });

            bool installed = IsBundledModInstalled(version);
            string currentMinecraftVersion = GetOfficialStorePackageVersionString();
            if (installed && !string.IsNullOrWhiteSpace(currentMinecraftVersion))
            {
                Properties.LauncherSettings.Default.LastPatchedMinecraftVersion = currentMinecraftVersion;
                Properties.LauncherSettings.Default.Save();
            }

            if (!showMessage) return installed;

            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    installed
                        ? "Both bundled DLLs are now in your Minecraft Bedrock mods folder. You can press PLAY now."
                        : "The launcher could not finish copying the bundled DLLs to your Minecraft Bedrock mods folder.",
                    App.DisplayName,
                    MessageBoxButton.OK,
                    installed ? MessageBoxImage.Information : MessageBoxImage.Warning);
            });

            return installed;
        }

        public async Task<bool> InstallPackage(MCVersion v, string dirPath)
        {
            try
            {
                StartTask();
                bool useInstalledStoreRelease = ShouldUseInstalledStoreRelease(v);
                bool installedStorePackageFromCatalog = false;

                if (!v.IsInstalled && !useInstalledStoreRelease)
                {
                    if (VersionDownloader.CanDownload(v))
                    {
                        installedStorePackageFromCatalog = await DownloadAndExtractPackage(v);
                    }
                    else
                    {
                        throw new NoVersionAccessibleException();
                    }
                }

                if (installedStorePackageFromCatalog || ShouldUseInstalledStoreRelease(v))
                {
                    await RedirectSaveData(dirPath, v.Type);
                    Trace.WriteLine($"Using installed Microsoft Store release for version '{v.Name}' without local re-registration.");
                    return true;
                }

                await UnregisterPackage(v, true);
                await RegisterPackage(v);
                await RedirectSaveData(dirPath, v.Type);
                return true;
            }
            catch (PackageManagerException e)
            {
                SetException(e);
                return false;
            }
            catch (NoVersionAccessibleException e)
            {
                SetException(e);
                return false;
            }
            catch (Exception e)
            {
                SetException(new AppInstallFailedException(e));
                return false;
            }
            finally
            {
                EndTask();
            }
        }

        public async Task ClosePackage()
        {
            if (GameHandle != null)
            {
                string title = XylarBedrock.Localization.Language.LanguageManager.GetResource("Dialog_KillGame_Title") as string;
                string content = XylarBedrock.Localization.Language.LanguageManager.GetResource("Dialog_KillGame_Text") as string;
                var result = await DialogPrompt.ShowDialog_YesNo(title, content);

                if (result == System.Windows.Forms.DialogResult.Yes) GameHandle.Kill();
            }
        }

        public async Task RemovePackage(MCVersion v)
        {
            try
            {
                StartTask();

                MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isUninstalling);
                await UnregisterPackage(v, false, true);
                MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isUninstalling);
                await DirectoryExtensions.DeleteAsync(v.GameDirectory, (x, y, phase) => ProgressWrapper(x, y, phase), "Files", "Folders");
                if (Directory.Exists(v.GameDirectory)) Directory.Delete(v.GameDirectory, true);
                v.UpdateFolderSize();
                await Task.Run(Program.OnApplicationRefresh);
                foreach (var ver in MainDataModel.Default.Versions) ver.UpdateFolderSize();
            }
            catch (PackageManagerException e)
            {
                SetException(e);
            }
            catch (Exception ex)
            {
                SetException(new PackageRemovalFailedException(ex));
            }
            finally
            {
                EndTask();
            }
        }

        public async Task AddPackage(string packagePath)
        {
            try
            {
                if (!File.Exists(packagePath)) return;
                StartTask();
                var outputDirectoryName = FileExtensions.GetAvaliableFileName(Path.GetFileNameWithoutExtension(packagePath), MainDataModel.Default.FilePaths.VersionsFolder);
                var outputDirectoryPath = Path.Combine(MainDataModel.Default.FilePaths.VersionsFolder, outputDirectoryName);
                var appxBackupsPath = Path.Combine(MainDataModel.Default.FilePaths.VersionsFolder, "AppxBackups");
                var backupFilePath = Path.Combine(appxBackupsPath, Path.GetFileName(packagePath));
                Trace.WriteLine("Extraction started");
                MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isExtracting);
                if (Directory.Exists(outputDirectoryPath)) Directory.Delete(outputDirectoryPath, true);
                Directory.CreateDirectory(appxBackupsPath);
                using var fileStream = File.OpenRead(packagePath);
                var progress = new Progress<ZipProgress>();
                progress.ProgressChanged += (s, z) => MainDataModel.Default.ProgressBarState.SetProgressBarProgress(currentProgress: z.Processed, totalProgress: z.Total);
                await Task.Run(() => new ZipArchive(fileStream).ExtractToDirectory(outputDirectoryPath, progress, CancelSource));
                string appxSignaturePath = Path.Combine(outputDirectoryPath, "AppxSignature.p7x");
                if (File.Exists(appxSignaturePath)) File.Delete(appxSignaturePath);
                if (File.Exists(backupFilePath)) File.Delete(backupFilePath);
                File.Move(packagePath, backupFilePath);
                Trace.WriteLine("Extracted successfully");
                await Task.Run(Program.OnApplicationRefresh);
                foreach (var ver in MainDataModel.Default.Versions) ver.UpdateFolderSize();
            }
            catch (PackageManagerException e)
            {
                SetException(e);
            }
            catch (Exception e)
            {
                SetException(new PackageAddFailedException(e));
            }
            finally
            {
                EndTask();
            }
        }

        public async Task<bool> DownloadPackage(MCVersion v)
        {
            try
            {
                if (!VersionDownloader.CanDownload(v))
                {
                    ShowLauncherMessage(
                        App.DisplayName,
                        "This Minecraft version does not have a downloadable package available right now.",
                        MessageBoxImage.Warning);
                    return false;
                }

                StartTask();
                await DownloadAndExtractPackage(v);
                await MainDataModel.Default.LoadVersions(forceStoreCheck: true);
                return true;
            }
            catch (PackageManagerException e)
            {
                SetException(e);
                return false;
            }
            catch (Exception e)
            {
                SetException(new PackageDownloadAndExtractFailedException(e));
                return false;
            }
            finally
            {
                EndTask();
            }
        }

        public void Cancel()
        {
            if (CancelSource != null && !CancelSource.IsCancellationRequested) CancelSource.Cancel();
        }

        #endregion

        #region Private Throwable Methods

        private async Task<bool> TryLaunchMinecraftAsync(MCVersion version, bool launchEditor)
        {
            VersionType type = version?.Type ?? VersionType.Release;
            bool forceSelectedVersion = ShouldForceSelectedVersionLaunch(version, launchEditor);

            if (forceSelectedVersion)
            {
                Trace.WriteLine($"Forcing launch of selected Minecraft version from '{version.GameDirectory}'.");
                if (await TryLaunchSpecificRegisteredVersionAsync(version))
                {
                    return true;
                }

                if (TryLaunchLocalVersionExecutable(version))
                {
                    return true;
                }

                Trace.WriteLine("Forced launch for selected version failed. Refusing to fall back to a different installed package.");
                return false;
            }

            bool preferPackagedLaunch = !launchEditor &&
                                       type == VersionType.Release &&
                                       IsOfficialStoreReleaseInstalled();

            if (preferPackagedLaunch)
            {
                Trace.WriteLine("Official Minecraft for Windows package detected. Trying packaged launch before minecraft: URI.");
                if (await TryLaunchInstalledMinecraftAsync(type))
                {
                    return true;
                }

                Trace.WriteLine("Packaged launch did not start Minecraft. Falling back to minecraft: URI.");
            }

            if (await TryLaunchMinecraftByUriAsync(type, launchEditor))
            {
                return true;
            }

            if (launchEditor)
            {
                return false;
            }

            Trace.WriteLine($"Failed to open {Constants.GetUri(type)} URI. Trying installed Minecraft packages instead.");
            return await TryLaunchInstalledMinecraftAsync(type);
        }

        private bool ShouldForceSelectedVersionLaunch(MCVersion version, bool launchEditor)
        {
            if (launchEditor || version == null)
            {
                return false;
            }

            if (ShouldUseInstalledStoreRelease(version))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(version.DirectDownloadUrl))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(version.GameDirectory) &&
                Directory.Exists(version.GameDirectory) &&
                File.Exists(version.ManifestPath))
            {
                return true;
            }

            return false;
        }

        private async Task<bool> TryLaunchSpecificRegisteredVersionAsync(MCVersion version)
        {
            if (version == null || string.IsNullOrWhiteSpace(version.GameDirectory))
            {
                return false;
            }

            string targetPath = Path.GetFullPath(version.GameDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            List<Package> matchingPackages = GetInstalledMinecraftPackages(version.Type)
                .Where(package =>
                {
                    string packagePath = GetInstalledLocationPath(package)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return string.Equals(packagePath, targetPath, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            foreach (Package package in matchingPackages)
            {
                if (await TryLaunchPackageEntriesAsync(package, version.Type))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryLaunchLocalVersionExecutable(MCVersion version)
        {
            if (version == null || string.IsNullOrWhiteSpace(version.GameDirectory))
            {
                return false;
            }

            foreach (string executablePath in GetExecutablePathsFromManifest(version.GameDirectory))
            {
                if (!IsLaunchableMinecraftExecutable(executablePath))
                {
                    continue;
                }

                if (TryLaunchExecutablePath(executablePath, Path.GetDirectoryName(executablePath) ?? version.GameDirectory))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<bool> TryLaunchMinecraftByUriAsync(VersionType type, bool launchEditor)
        {
            try
            {
                RememberLaunchMethod($"{Constants.GetUri(type)} URI");
                return await Launcher.LaunchUriAsync(new Uri($"{Constants.GetUri(type)}:?Editor={launchEditor}"));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Minecraft URI launch failed: {ex}");
                return false;
            }
        }

        private async Task<bool> TryLaunchInstalledMinecraftAsync(VersionType type)
        {
            foreach (Package package in GetInstalledMinecraftPackages(type))
            {
                if (await TryLaunchPackageEntriesAsync(package, type))
                {
                    return true;
                }

                if (TryLaunchInstalledPackageExecutables(package))
                {
                    return true;
                }
            }

            try
            {
                var diagnosticPackages =
                    await AppDiagnosticInfo.RequestInfoForPackageAsync(Constants.GetPackageFamily(type));

                AppDiagnosticInfo[] orderedDiagnosticPackages = OrderDiagnosticPackages(diagnosticPackages, type).ToArray();
                foreach (AppDiagnosticInfo diagnosticPackage in orderedDiagnosticPackages)
                {
                    try
                    {
                        RememberLaunchMethod($"Packaged activation {diagnosticPackage.AppInfo.AppUserModelId}");
                        AppActivationResult activationResult = await diagnosticPackage.LaunchAsync();
                        if (activationResult != null)
                        {
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Diagnostic launch failed for package '{diagnosticPackage.AppInfo.AppUserModelId}': {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AppDiagnosticInfo launch fallback failed: {ex}");
            }

            return TryLaunchDetectedMinecraftDirectories(type);
        }

        private async Task<bool> TryLaunchPackageEntriesAsync(Package package, VersionType type)
        {
            try
            {
                var appEntries = await package.GetAppListEntriesAsync();
                foreach (var appEntry in OrderPackageEntries(appEntries, type))
                {
                    RememberLaunchMethod($"AppListEntry {appEntry.AppUserModelId}");
                    if (await appEntry.LaunchAsync())
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AppListEntry launch failed for package '{package.Id.FullName}': {ex}");
            }

            return false;
        }

        private bool TryLaunchInstalledPackageExecutables(Package package)
        {
            foreach (string executablePath in GetInstalledExecutablePaths(package))
            {
                if (!IsLaunchableMinecraftExecutable(executablePath))
                {
                    continue;
                }

                if (TryLaunchExecutablePath(executablePath, Path.GetDirectoryName(executablePath)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryLaunchDetectedMinecraftDirectories(VersionType type)
        {
            foreach (string directory in GetInstalledMinecraftDirectories(type))
            {
                foreach (string executablePath in GetExecutablePathsForLaunch(directory))
                {
                    if (TryLaunchExecutablePath(executablePath, directory))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private async Task GetGameHandle(IEnumerable<string> processNames)
        {
            await Task.Run(() =>
            {
                try
                {
                    string[] candidateNames = processNames?
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray() ?? Array.Empty<string>();

                    if (candidateNames.Length == 0)
                    {
                        candidateNames = new[] { Constants.MINECRAFT_PROCESS_NAME };
                    }

                    Process[] minecraftProcesses = Array.Empty<Process>();
                    Stopwatch waitTimer = Stopwatch.StartNew();

                    while (waitTimer.Elapsed < TimeSpan.FromSeconds(35))
                    {
                        minecraftProcesses = candidateNames
                            .SelectMany(Process.GetProcessesByName)
                            .GroupBy(process => process.Id)
                            .Select(group => group.First())
                            .ToArray();

                        if (minecraftProcesses.Length > 0)
                        {
                            break;
                        }

                        Thread.Sleep(500);
                    }

                    if (minecraftProcesses.Length >= 1)
                    {
                        MainDataModel.Default.ProgressBarState.SetGameRunningStatus(true);
                        GameHandle = minecraftProcesses[0];
                        GameHandle.EnableRaisingEvents = true;
                        GameHandle.Exited += OnPackageExit;

                        void OnPackageExit(object sender, EventArgs e)
                        {
                            Process p = sender as Process;
                            p.Exited -= OnPackageExit;
                            GameHandle = null;
                            MainDataModel.Default.ProgressBarState.SetGameRunningStatus(false);
                        }

                        Trace.WriteLine("Successfully attached Minecraft process");
                    }
                    else
                    {
                        Trace.WriteLine("Minecraft launch request was sent, but no game process was found before timeout.");
                        GameHandle = null;
                        MainDataModel.Default.ProgressBarState.SetGameRunningStatus(false);
                        ShowLauncherMessage(
                            App.DisplayName,
                            "The selected Minecraft version did not finish opening in time. Press PLAY again after the installation completes.",
                            MessageBoxImage.Warning);
                    }
                }
                catch (Exception e)
                {
                    Trace.WriteLine($"Minecraft process hook failed: {e}");
                    GameHandle = null;
                    MainDataModel.Default.ProgressBarState.SetGameRunningStatus(false);
                }
            });
        }

        private IEnumerable<Package> GetInstalledMinecraftPackages(VersionType type)
        {
            string expectedPackageFamily = Constants.GetPackageFamily(type);
            string expectedPackageName = GetExpectedMinecraftPackageName(type);
            Dictionary<string, Package> packages = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (Package package in PM.FindPackagesForUser(string.Empty, expectedPackageFamily))
                {
                    AddDetectedMinecraftPackage(packages, package, expectedPackageFamily, expectedPackageName);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"FindPackagesForUser failed for '{expectedPackageFamily}': {ex}");
            }

            try
            {
                foreach (Package package in PM.FindPackages())
                {
                    AddDetectedMinecraftPackage(packages, package, expectedPackageFamily, expectedPackageName);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"FindPackages fallback failed for '{expectedPackageFamily}': {ex}");
            }

            return packages.Values
                .OrderByDescending(pkg => pkg.Id.Version.Major)
                .ThenByDescending(pkg => pkg.Id.Version.Minor)
                .ThenByDescending(pkg => pkg.Id.Version.Build)
                .ThenByDescending(pkg => pkg.Id.Version.Revision)
                .ToList();
        }

        private static string GetExpectedMinecraftPackageName(VersionType type)
        {
            string packageFamily = Constants.GetPackageFamily(type);
            int separatorIndex = packageFamily.IndexOf('_');
            return separatorIndex > 0 ? packageFamily.Substring(0, separatorIndex) : packageFamily;
        }

        private static void AddDetectedMinecraftPackage(
            IDictionary<string, Package> packages,
            Package package,
            string expectedPackageFamily,
            string expectedPackageName)
        {
            if (!IsMatchingMinecraftPackage(package, expectedPackageFamily, expectedPackageName))
            {
                return;
            }

            string packageKey = package.Id?.FullName ?? package.Id?.FamilyName ?? Guid.NewGuid().ToString("N");
            packages[packageKey] = package;
        }

        private static bool IsMatchingMinecraftPackage(Package package, string expectedPackageFamily, string expectedPackageName)
        {
            if (package == null || package.Id == null)
            {
                return false;
            }

            if (string.Equals(package.Id.FamilyName, expectedPackageFamily, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(package.Id.Name, expectedPackageName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(package.Id.FullName) &&
                   package.Id.FullName.StartsWith(expectedPackageName + "_", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetInstalledLocationPath(Package package)
        {
            try
            {
                return package?.InstalledLocation?.Path ?? string.Empty;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not read package location for '{package?.Id.FullName}': {ex.Message}");
                return string.Empty;
            }
        }

        private IEnumerable<string> GetKnownMinecraftProcessNames(VersionType type)
        {
            HashSet<string> processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Constants.MINECRAFT_PROCESS_NAME
            };

            foreach (Package package in GetInstalledMinecraftPackages(type))
            {
                foreach (string executablePath in GetInstalledExecutablePaths(package))
                {
                    string processName = Path.GetFileNameWithoutExtension(executablePath);
                    if (!string.IsNullOrWhiteSpace(processName) &&
                        !string.Equals(processName, "GameLaunchHelper", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(processName, "custominstallexec", StringComparison.OrdinalIgnoreCase))
                    {
                        processNames.Add(processName);
                    }
                }
            }

            return processNames;
        }

        private static IEnumerable<string> GetInstalledExecutablePaths(Package package)
        {
            string installPath = GetInstalledLocationPath(package);
            if (string.IsNullOrWhiteSpace(installPath))
            {
                yield break;
            }

            foreach (string executablePath in GetExecutablePathsFromManifest(installPath))
            {
                yield return executablePath;
            }
        }

        private static IEnumerable<string> GetExecutablePathsFromManifest(string installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath))
            {
                yield break;
            }

            string manifestPath = Path.Combine(installPath, "AppxManifest.xml");
            if (!File.Exists(manifestPath))
            {
                yield break;
            }

            XDocument manifestDocument;
            try
            {
                manifestDocument = XDocument.Load(manifestPath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not read package manifest '{manifestPath}': {ex}");
                yield break;
            }

            foreach (XElement applicationElement in manifestDocument.Descendants().Where(x => x.Name.LocalName == "Application"))
            {
                string executableRelativePath = applicationElement.Attribute("Executable")?.Value;
                if (string.IsNullOrWhiteSpace(executableRelativePath))
                {
                    continue;
                }

                string executablePath = Path.Combine(installPath, executableRelativePath);
                if (File.Exists(executablePath))
                {
                    yield return executablePath;
                }
            }
        }

        private static IEnumerable<string> GetExecutablePathsForLaunch(string installPath)
        {
            HashSet<string> executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string executablePath in GetExecutablePathsFromManifest(installPath))
            {
                if (IsLaunchableMinecraftExecutable(executablePath))
                {
                    executablePaths.Add(executablePath);
                }
            }

            foreach (string fileName in new[] { "Minecraft.Windows.exe", "Minecraft.WindowsBeta.exe" })
            {
                string candidatePath = Path.Combine(installPath, fileName);
                if (File.Exists(candidatePath) && IsLaunchableMinecraftExecutable(candidatePath))
                {
                    executablePaths.Add(candidatePath);
                }
            }

            return executablePaths;
        }

        private static IEnumerable<AppListEntry> OrderPackageEntries(IEnumerable<AppListEntry> appEntries, VersionType type)
        {
            List<AppListEntry> entries = appEntries?.ToList() ?? new List<AppListEntry>();
            if (entries.Count == 0)
            {
                return Enumerable.Empty<AppListEntry>();
            }

            string preferredSuffix = GetPreferredAppUserModelIdSuffix(type);
            List<AppListEntry> preferredEntries = entries
                .Where(appEntry => IsPreferredAppUserModelId(appEntry.AppUserModelId, preferredSuffix))
                .ToList();

            if (preferredEntries.Count > 0)
            {
                return preferredEntries
                    .Concat(entries.Where(appEntry => !preferredEntries.Contains(appEntry)))
                    .ToList();
            }

            return entries;
        }

        private static IEnumerable<AppDiagnosticInfo> OrderDiagnosticPackages(IEnumerable<AppDiagnosticInfo> diagnosticPackages, VersionType type)
        {
            List<AppDiagnosticInfo> packages = diagnosticPackages?.ToList() ?? new List<AppDiagnosticInfo>();
            if (packages.Count == 0)
            {
                return Enumerable.Empty<AppDiagnosticInfo>();
            }

            string preferredSuffix = GetPreferredAppUserModelIdSuffix(type);
            List<AppDiagnosticInfo> preferredPackages = packages
                .Where(package => IsPreferredAppUserModelId(package?.AppInfo?.AppUserModelId, preferredSuffix))
                .ToList();

            if (preferredPackages.Count > 0)
            {
                return preferredPackages
                    .Concat(packages.Where(package => !preferredPackages.Contains(package)))
                    .ToList();
            }

            return packages;
        }

        private static string GetPreferredAppUserModelIdSuffix(VersionType type)
        {
            return type == VersionType.Preview ? "!App" : "!Game";
        }

        private static bool IsPreferredAppUserModelId(string appUserModelId, string preferredSuffix)
        {
            return !string.IsNullOrWhiteSpace(appUserModelId) &&
                   appUserModelId.EndsWith(preferredSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private void RememberLaunchMethod(string launchMethod)
        {
            if (string.IsNullOrWhiteSpace(launchMethod))
            {
                return;
            }

            lock (LaunchMethodLock)
            {
                LastLaunchMethodAttempted = launchMethod;
            }
        }

        private MCVersion GetDetectedMinecraftVersionForDllSelection()
        {
            return CreateDllSelectionVersion(GetOfficialStorePackageVersionString());
        }

        private static MCVersion CreateDllSelectionVersion(string versionName)
        {
            return string.IsNullOrWhiteSpace(versionName) ? null : new MCVersion(versionName);
        }

        private static string GetBundledModDllNameForVersion(MCVersion version)
        {
            return ShouldUseToolsDllForVersion(version)
                ? Constants.BUNDLED_TOOLS_DLL_NAME
                : Constants.BUNDLED_MOD_DLL_NAME;
        }

        private static bool ShouldUseToolsDllForVersion(MCVersion version)
        {
            string normalizedVersion = NormalizeDllSelectionVersion(version?.Name);
            if (string.IsNullOrWhiteSpace(normalizedVersion) ||
                !MinecraftVersion.TryParse(normalizedVersion, out MinecraftVersion selectedVersion))
            {
                return false;
            }

            MinecraftVersion toolsMinimumVersion = MinecraftVersion.Parse("1.21.130.4");
            MinecraftVersion toolsMaximumVersion = MinecraftVersion.Parse("26.10");

            return selectedVersion.CompareTo(toolsMinimumVersion) >= 0 &&
                   selectedVersion.CompareTo(toolsMaximumVersion) <= 0;
        }

        private static string NormalizeDllSelectionVersion(string versionName)
        {
            if (string.IsNullOrWhiteSpace(versionName))
            {
                return string.Empty;
            }

            string normalizedVersion = versionName.Trim();
            if (MinecraftVersion.TryParse(normalizedVersion, out MinecraftVersion parsedVersion) &&
                parsedVersion.Major == 1 &&
                (parsedVersion.Minor >= 26 || parsedVersion.Patch >= 1000))
            {
                normalizedVersion = parsedVersion.ToRealString();
            }

            return normalizedVersion;
        }

        private IEnumerable<string> GetInactiveInstalledModPaths(MCVersion version)
        {
            string activeModDllName = GetBundledModDllNameForVersion(version);

            foreach (string bundledModDllName in new[] { Constants.BUNDLED_MOD_DLL_NAME, Constants.BUNDLED_TOOLS_DLL_NAME })
            {
                if (!string.Equals(bundledModDllName, activeModDllName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return Path.Combine(GetModsDirectoryPath(), bundledModDllName);
                }
            }
        }

        private void RemoveInactiveInstalledModFiles(MCVersion version)
        {
            foreach (string inactiveModPath in GetInactiveInstalledModPaths(version))
            {
                try
                {
                    if (File.Exists(inactiveModPath))
                    {
                        File.Delete(inactiveModPath);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Could not remove inactive bundled DLL '{inactiveModPath}': {ex.Message}");
                }
            }
        }

        private static bool CanReadFile(string filePath)
        {
            try
            {
                using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return stream.Length >= 0;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not read file '{filePath}': {ex.Message}");
                return false;
            }
        }

        private static string GetBundledDllStatusText(BundledDllPackDiagnostics diagnostics)
        {
            if (!diagnostics.DllDirectoryExists) return "Missing dll folder";
            if (!diagnostics.ModDllExists) return $"Missing {diagnostics.ModDllName}";
            if (!diagnostics.RuntimeDllExists) return $"Missing {Constants.EXTRA_DLL_NAME}";
            if (!diagnostics.ModDllReadable || !diagnostics.RuntimeDllReadable) return "DLL pack unreadable";
            return "DLL pack ready";
        }

        private static string GetBundledDllDetailsText(BundledDllPackDiagnostics diagnostics)
        {
            List<string> lines = new List<string>
            {
                "XylarBedrock expects this release layout next to the launcher:",
                $"  {Path.Combine(diagnostics.ExecutableDirectoryPath, "XylarBedrock.exe")}",
                $"  {diagnostics.DllDirectoryPath}\\{diagnostics.ModDllName}",
                $"  {diagnostics.DllDirectoryPath}\\{Constants.EXTRA_DLL_NAME}",
                string.Empty
            };

            if (!diagnostics.DllDirectoryExists)
            {
                lines.Add($"The dll folder was not found at:");
                lines.Add(diagnostics.DllDirectoryPath);
            }
            else if (!diagnostics.ModDllExists && !diagnostics.RuntimeDllExists)
            {
                lines.Add("Both required DLL files are missing from:");
                lines.Add(diagnostics.DllDirectoryPath);
            }
            else if (!diagnostics.ModDllExists)
            {
                lines.Add($"Missing file:");
                lines.Add(diagnostics.ModDllPath);
            }
            else if (!diagnostics.RuntimeDllExists)
            {
                lines.Add("Missing file:");
                lines.Add(diagnostics.RuntimeDllPath);
            }
            else if (!diagnostics.ModDllReadable || !diagnostics.RuntimeDllReadable)
            {
                lines.Add("The launcher found the DLL pack, but one of the files could not be read.");
                if (!diagnostics.ModDllReadable) lines.Add(diagnostics.ModDllPath);
                if (!diagnostics.RuntimeDllReadable) lines.Add(diagnostics.RuntimeDllPath);
            }
            else
            {
                lines.Add("The DLL pack is ready.");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static void ShowLauncherMessage(string title, string text, MessageBoxImage icon)
        {
            if (Application.Current?.Dispatcher == null)
            {
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(text, title, MessageBoxButton.OK, icon);
            });
        }

        private async Task<bool> DownloadAndExtractPackage(MCVersion v)
        {
            try
            {
                Trace.WriteLine($"Download start: {v.PackageID}");
                SetCancelation(true);

                string subDirectory = Path.Combine(MainDataModel.Default.FilePaths.VersionsFolder, "AppxBackups");
                if (!Directory.Exists(subDirectory))
                {
                    Directory.CreateDirectory(subDirectory);
                }

                if (VersionDownloader.IsMsixvcPackage(v))
                {
                    await DownloadAndInstallStorePackage(v, subDirectory);
                    return true;
                }

                string dlPath = "Minecraft-" + v.Name + ".Appx";
                string bkpsPath = Path.Combine(subDirectory, dlPath);
                string pkgPath = File.Exists(bkpsPath) ? bkpsPath : dlPath;

                if (!File.Exists(bkpsPath)) await DownloadPackage(v, dlPath, CancelSource);
                await ExtractPackage(v, dlPath, bkpsPath, pkgPath, CancelSource);

                v.UpdateFolderSize();
                return false;
            }
            catch (PackageManagerException e)
            {
                ResetTask();
                throw;
            }
            catch (Exception ex)
            {
                ResetTask();
                throw new Exception("DownloadAndExtractPackage Failed", ex);
            }
            finally
            {
                ResetTask();
                SetCancelation(false);
                CancelSource = null;
            }
        }

        private async Task DownloadAndInstallStorePackage(MCVersion v, string backupsDirectory)
        {
            string extension = VersionDownloader.GetPackageFileExtension(v);
            string safeVersionName = string.Join("_", v.Name.Split(Path.GetInvalidFileNameChars()));
            string packageFileName = $"Minecraft-{safeVersionName}{extension}";
            string backupPath = Path.Combine(backupsDirectory, packageFileName);
            string packagePath = backupPath;
            bool usingBackup = File.Exists(backupPath);

            if (!usingBackup)
            {
                packagePath = Path.Combine(Path.GetTempPath(), $"XylarBedrock-{Guid.NewGuid():N}{extension}");
                await DownloadPackage(v, packagePath, CancelSource);
            }

            try
            {
                await RegisterStorePackage(packagePath);

                if (!await WaitForStorePackageVersionAsync(v, TimeSpan.FromSeconds(20)))
                {
                    Trace.WriteLine("PackageManager completed, but the selected Minecraft version is not active. Trying Add-AppxPackage fallback.");
                    await RegisterStorePackageWithPowerShell(packagePath);
                }

                if (!await WaitForStorePackageVersionAsync(v, TimeSpan.FromSeconds(30)))
                {
                    string currentVersion = GetOfficialStorePackageVersionString();
                    throw new PackageRegistrationFailedException(
                        $"Windows did not activate Minecraft {v.Name}. Active package is still {(string.IsNullOrWhiteSpace(currentVersion) ? "not detected" : currentVersion)}.",
                        new InvalidOperationException($"Expected {v.Name}, active {currentVersion}."));
                }

                if (!usingBackup)
                {
                    if (Properties.LauncherSettings.Default.KeepAppx)
                    {
                        Directory.CreateDirectory(backupsDirectory);
                        File.Move(packagePath, backupPath, true);
                    }
                    else
                    {
                        TryDeletePackageFile(packagePath);
                    }
                }
            }
            catch
            {
                if (!usingBackup)
                {
                    TryDeletePackageFile(packagePath);
                }

                throw;
            }
        }

        private async Task DownloadPackage(MCVersion v, string dlPath, CancellationTokenSource cancelSource)
        {
            try
            {
                if (v.IsBeta) await AuthenticateBetaUser();
                MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isDownloading);
                Trace.WriteLine("Download starting");
                await VersionDownloader.DownloadVersion(v, dlPath, (x, y) => ProgressWrapper(x, y), cancelSource.Token);
                Trace.WriteLine("Download complete");
            }
            catch (PackageManagerException e)
            {
                ResetTask();
                throw;
            }
            catch (TaskCanceledException e)
            {
                ResetTask();
                throw new PackageDownloadCanceledException(e);
            }
            catch (Exception e)
            {
                ResetTask();
                throw new PackageDownloadFailedException(e);
            }
            finally
            {
                ResetTask();
            }
        }

        private async Task RegisterStorePackage(string packagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
                {
                    throw new FileNotFoundException("Downloaded Minecraft package was not found.", packagePath);
                }

                Trace.WriteLine($"Installing Store package: {packagePath}");
                MainDataModel.Default.ProgressBarState.SetProgressBarText("Installing Minecraft package...");
                MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isRegisteringPackage);

                DeploymentOptions options =
                    DeploymentOptions.ForceApplicationShutdown |
                    DeploymentOptions.ForceTargetApplicationShutdown |
                    DeploymentOptions.ForceUpdateFromAnyVersion;

                await DeploymentProgressWrapper(PM.AddPackageAsync(new Uri(Path.GetFullPath(packagePath)), null, options));
                Trace.WriteLine("Store package install done.");
            }
            catch (PackageManagerException e)
            {
                ResetTask();
                throw;
            }
            catch (Exception e)
            {
                ResetTask();
                throw new PackageRegistrationFailedException(e);
            }
            finally
            {
                ResetTask();
            }
        }

        private async Task RegisterStorePackageWithPowerShell(string packagePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
                {
                    throw new FileNotFoundException("Downloaded Minecraft package was not found.", packagePath);
                }

                Trace.WriteLine($"Installing Store package with Add-AppxPackage: {packagePath}");
                MainDataModel.Default.ProgressBarState.SetProgressBarText("Finalizing Minecraft package...");
                MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isRegisteringPackage);

                using Process process = new Process();
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.ArgumentList.Add("-NoProfile");
                process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
                process.StartInfo.ArgumentList.Add("Bypass");
                process.StartInfo.ArgumentList.Add("-Command");
                process.StartInfo.ArgumentList.Add("$ErrorActionPreference='Stop'; Add-AppxPackage -Path $args[0] -ForceApplicationShutdown -ForceTargetApplicationShutdown -ForceUpdateFromAnyVersion");
                process.StartInfo.ArgumentList.Add(Path.GetFullPath(packagePath));

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    Trace.WriteLine("Add-AppxPackage output: " + output);
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? $"Add-AppxPackage failed with exit code {process.ExitCode}."
                        : error);
                }
            }
            catch (PackageManagerException)
            {
                ResetTask();
                throw;
            }
            catch (Exception e)
            {
                ResetTask();
                throw new PackageRegistrationFailedException(e);
            }
            finally
            {
                ResetTask();
            }
        }

        private async Task<bool> WaitForStorePackageVersionAsync(MCVersion version, TimeSpan timeout)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                if (DoesInstalledStorePackageMatch(version))
                {
                    return true;
                }

                await Task.Delay(1000);
            }

            return DoesInstalledStorePackageMatch(version);
        }

        private bool DoesInstalledStorePackageMatch(MCVersion version)
        {
            if (version == null)
            {
                return false;
            }

            return version.MatchesOfficialStoreRelease;
        }

        private static void TryDeletePackageFile(string packagePath)
        {
            try
            {
                if (File.Exists(packagePath))
                {
                    File.Delete(packagePath);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not delete temporary package '{packagePath}': {ex}");
            }
        }

        private async Task RegisterPackage(MCVersion v)
        {
            try
            {
                Trace.WriteLine("Registering package");
                MainDataModel.Default.ProgressBarState.SetProgressBarText(v.GetPackageNameFromMainifest());
                MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isRegisteringPackage);
                await DeploymentProgressWrapper(PM.RegisterPackageAsync(new Uri(v.ManifestPath), null, Constants.PackageDeploymentOptions));
                Trace.WriteLine("App re-register done!");
            }
            catch (PackageManagerException e)
            {
                ResetTask();
                throw;
            }
            catch (Exception e)
            {
                ResetTask();
                throw new PackageRegistrationFailedException(e);
            }
            finally
            {
                ResetTask();
            }
        }

        private async Task ExtractPackage(MCVersion v, string dlPath, string bkpsPath, string pkgPath, CancellationTokenSource cancelSource)
        {
            try
            {
                Trace.WriteLine("Extraction started");
                MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isExtracting);

                if (Directory.Exists(v.GameDirectory))
                    await DirectoryExtensions.DeleteAsync(v.GameDirectory, (x, y, phase) => ProgressWrapper(x, y, phase));

                using var fileStream = File.OpenRead(pkgPath);
                var progress = new Progress<ZipProgress>();
                progress.ProgressChanged += (s, z) => MainDataModel.Default.ProgressBarState.SetProgressBarProgress(currentProgress: z.Processed, totalProgress: z.Total);
                await Task.Run(() =>
                {
                    using var zipArchive = new ZipArchive(fileStream);
                    zipArchive.ExtractToDirectory(v.GameDirectory, progress, cancelSource);
                });

                await File.WriteAllTextAsync(v.IdentificationPath, v.PackageID);
                string appxSignaturePath = Path.Combine(v.GameDirectory, "AppxSignature.p7x");
                if (File.Exists(appxSignaturePath)) File.Delete(appxSignaturePath);
                CopyBundledRuntimeToVersionDirectory(v.GameDirectory);

                if (!File.Exists(bkpsPath))
                {
                    if (Properties.LauncherSettings.Default.KeepAppx)
                        File.Move(dlPath, bkpsPath);
                    else
                        File.Delete(dlPath);
                }

                Trace.WriteLine("Extracted successfully");
            }
            catch (PackageManagerException e)
            {
                ResetTask();
                throw;
            }
            catch (TaskCanceledException e)
            {
                MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isCanceling);
                await DirectoryExtensions.DeleteAsync(v.GameDirectory, (x, y, phase) => ProgressWrapper(x, y, phase));
                ResetTask();
                throw new PackageExtractionCanceledException(e);
            }
            catch (Exception e)
            {
                ResetTask();
                throw new PackageExtractionFailedException(e);
            }
            finally
            {
                ResetTask();
            }
        }

        private async Task UnregisterPackage(MCVersion v, bool keepVersion = false, bool mustMatchVersion = false)
        {
            try
            {
                foreach (var pkg in PM.FindPackagesForUser(string.Empty, Constants.GetPackageFamily(v.Type)))
                {
                    string location;

                    try { location = pkg.InstalledLocation.Path; }
                    catch (FileNotFoundException) { location = string.Empty; }

                    if (location == v.GameDirectory && keepVersion)
                    {
                        Trace.WriteLine("Skipping package removal - same path: " + pkg.Id.FullName + " " + location);
                        continue;
                    }

                    if (location != v.GameDirectory && mustMatchVersion) continue;

                    Trace.WriteLine("Removing package: " + pkg.Id.FullName);

                    MainDataModel.Default.ProgressBarState.SetProgressBarText(pkg.Id.FullName);
                    MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isRemovingPackage);
                    await DeploymentProgressWrapper(PM.RemovePackageAsync(pkg.Id.FullName, Constants.PackageRemovalOptions));
                    Trace.WriteLine("Removal of package done: " + pkg.Id.FullName);
                }
            }
            catch (PackageManagerException e)
            {
                ResetTask();
                throw;
            }
            catch (Exception ex)
            {
                ResetTask();
                throw new PackageDeregistrationFailedException(ex);
            }
            finally
            {
                ResetTask();
            }
        }

        private async Task RedirectSaveData(string InstallationsFolderPath, VersionType type)
        {
            await Task.Run(() =>
            {
                try
                {
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                    string LocalStateFolder = Path.Combine(localAppData, "Packages", Constants.GetPackageFamily(type), "LocalState");
                    string PackageFolder = Path.Combine(localAppData, "Packages", Constants.GetPackageFamily(type), "LocalState", "games", "com.mojang");
                    string PackageBakFolder = Path.Combine(localAppData, "Packages", Constants.GetPackageFamily(type), "LocalState", "games", "com.mojang.default");
                    string ProfileFolder = Path.GetFullPath(InstallationsFolderPath);

                    string RequiredDir = Directory.GetParent(PackageFolder).FullName;
                    if (Directory.Exists(PackageFolder)) Directory.Delete(PackageFolder, true);
                    if (!Directory.Exists(RequiredDir)) Directory.CreateDirectory(RequiredDir);
                    DirectoryInfo profileDir = Directory.CreateDirectory(ProfileFolder);
                    
                    bool linkCreated = TryCreatePackageProfileLink(PackageFolder, ProfileFolder);
                    if (!linkCreated)
                    {
                        throw new SaveRedirectionFailedException(
                            new Exception("Failed to connect the Minecraft save folder to the selected profile directory."));
                    }
                    
                    DirectoryInfo pkgDir = Directory.CreateDirectory(PackageFolder);
                    DirectoryInfo lsDir = Directory.CreateDirectory(LocalStateFolder);

                    SecurityIdentifier owner = WindowsIdentity.GetCurrent().User;
                    SecurityIdentifier authenticated_users_identity = new SecurityIdentifier("S-1-5-11");

                    FileSystemAccessRule owner_access_rules = new FileSystemAccessRule(owner, FileSystemRights.FullControl, InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow);
                    FileSystemAccessRule au_access_rules = new FileSystemAccessRule(authenticated_users_identity, FileSystemRights.FullControl, InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow);

                    var lsSecurity = lsDir.GetAccessControl();
                    AuthorizationRuleCollection rules = lsSecurity.GetAccessRules(true, true, typeof(NTAccount));
                    List<FileSystemAccessRule> needed_rules = new List<FileSystemAccessRule>();
                    foreach (AccessRule rule in rules)
                    {
                        if (rule.IdentityReference is SecurityIdentifier)
                        {
                            var required_rule = new FileSystemAccessRule(rule.IdentityReference, FileSystemRights.FullControl, rule.InheritanceFlags, rule.PropagationFlags, rule.AccessControlType);
                            needed_rules.Add(required_rule);
                        }
                    }

                    var pkgSecurity = pkgDir.GetAccessControl();
                    pkgSecurity.SetOwner(owner);
                    pkgSecurity.AddAccessRule(au_access_rules);
                    pkgSecurity.AddAccessRule(owner_access_rules);
                    pkgDir.SetAccessControl(pkgSecurity);

                    var profileSecurity = profileDir.GetAccessControl();
                    profileSecurity.AddAccessRule(au_access_rules);
                    profileSecurity.AddAccessRule(owner_access_rules);
                    needed_rules.ForEach(x => profileSecurity.AddAccessRule(x));
                    profileDir.SetAccessControl(profileSecurity);
                }
                catch (PackageManagerException e)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw new SaveRedirectionFailedException(e);
                }
            });
        }

        private async Task AuthenticateBetaUser()
        {
            try
            {
                var userIndex = Properties.LauncherSettings.Default.CurrentInsiderAccountIndex;
                var token = await Task.Run(() => AuthenticationManager.Default.GetWUToken(userIndex));
                StoreNetwork.setMSAUserToken(token);
            }
            catch (PackageManagerException e)
            {
                throw;
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine("Error while Authenticating UserToken for Version Fetching:\n" + e);
                throw new BetaAuthenticationFailedException(e);
            }
        }

        private static bool TryCreatePackageProfileLink(string linkPath, string targetPath)
        {
            if (SymLinkHelper.CreateSymbolicLinkSafe(linkPath, targetPath, SymLinkHelper.SymbolicLinkType.Directory))
            {
                Trace.WriteLine("Save data redirection created with symbolic link support.");
                return true;
            }

            return TryCreateDirectoryJunction(linkPath, targetPath);
        }

        private static bool TryCreateDirectoryJunction(string linkPath, string targetPath)
        {
            try
            {
                ProcessStartInfo junctionInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(linkPath) ?? AppContext.BaseDirectory
                };

                using Process junctionProcess = Process.Start(junctionInfo);
                junctionProcess?.WaitForExit();

                bool created = junctionProcess != null && junctionProcess.ExitCode == 0 && Directory.Exists(linkPath);
                Trace.WriteLine(created
                    ? "Save data redirection created with directory junction fallback."
                    : "Directory junction fallback failed.");
                return created;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Directory junction fallback failed: {ex}");
                return false;
            }
        }

        #endregion

        #region Helpers

        protected async Task DeploymentProgressWrapper(IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> t)
        {
            TaskCompletionSource<int> src = new TaskCompletionSource<int>();
            t.Progress += (v, p) => MainDataModel.Default.ProgressBarState.SetProgressBarProgress(currentProgress: Convert.ToInt64(p.percentage), totalProgress: 100);
            t.Completed += (v, p) =>
            {
                MainDataModel.Default.ProgressBarState.ResetProgressBarProgress();

                if (p == AsyncStatus.Error)
                {
                    Trace.WriteLine("Deployment failed: " + v.GetResults().ErrorText);
                    src.SetException(new Exception("Deployment failed: " + v.GetResults().ErrorText));
                }
                else
                {
                    Trace.WriteLine("Deployment done: " + p);
                    src.SetResult(1);
                }
            };
            await src.Task;
        }

        protected void ProgressWrapper(long current, long total, string text = null)
        {
            MainDataModel.Default.ProgressBarState.SetProgressBarProgress(current, total);
            MainDataModel.Default.ProgressBarState.SetProgressBarText(text);
        }

        protected void ResetTask()
        {
            MainDataModel.Default.ProgressBarState.ResetProgressBarProgress();
            MainDataModel.Default.ProgressBarState.SetProgressBarText();
            MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.None);
        }

        protected void EndTask()
        {
            MainDataModel.Default.ProgressBarState.ResetProgressBarProgress();
            MainDataModel.Default.ProgressBarState.SetProgressBarText();
            MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.None);
            MainDataModel.Default.ProgressBarState.SetProgressBarVisibility(false);
        }

        protected void StartTask()
        {
            MainDataModel.Default.ProgressBarState.SetProgressBarState(LauncherState.isInitializing);
            MainDataModel.Default.ProgressBarState.SetProgressBarVisibility(true);
        }

        protected void SetCancelation(bool cancelState)
        {
            if (cancelState) CancelSource = new CancellationTokenSource();
            MainDataModel.Default.ProgressBarState.AllowCancel = cancelState ? true : false;
            MainDataModel.Default.ProgressBarState.CancelCommand = cancelState ? new RelayCommand((o) => Cancel()) : null;
        }

        protected void SetException(Exception e)
        {
            if (e.GetType() == typeof(PackageExtractionFailedException)) SetError(e, "Extraction failed", "Error_AppExtractionFailed_Title", "Error_AppExtractionFailed");
            else if (e.GetType() == typeof(PackageDownloadFailedException)) SetError(e, "Download failed", "Error_AppDownloadFailed_Title", "Error_AppDownloadFailed");
            else if (e.GetType() == typeof(BetaAuthenticationFailedException)) SetError(e, "Authentication failed", "Error_AuthenticationFailed_Title", "Error_AuthenticationFailed");
            else if (e.GetType() == typeof(AppLaunchFailedException)) SetError(e, "App launch failed", "Error_AppLaunchFailed_Title", "Error_AppLaunchFailed");
            else if (e.GetType() == typeof(PackageRegistrationFailedException)) SetError(e, "App registeration failed", "Error_AppReregisterFailed_Title", "Error_AppReregisterFailed");
            else if (e.GetType() == typeof(PackageRemovalFailedException)) SetError(e, "App uninstall failed", "Error_AppUninstallFailed_Title", "Error_AppUninstallFailed");
            else if (e.GetType() == typeof(SaveRedirectionFailedException)) SetError(e, "Save redirection failed", "Error_SaveDirectoryRedirectionFailed_Title", "Error_SaveDirectoryRedirectionFailed");
            else if (e.GetType() == typeof(PackageDeregistrationFailedException)) SetError(e, "App deregisteration failed", "Error_AppDeregisteringFailed_Title", "Error_AppDeregisteringFailed");
            else if (e.GetType() == typeof(PackageDownloadAndExtractFailedException)) SetGenericError(e);
            else if (e.GetType() == typeof(PackageProcessHookFailedException)) SetGenericError(e);
            else if (e.GetType() == typeof(PackageExtractionCanceledException)) CancelAction();
            else if (e.GetType() == typeof(PackageDownloadCanceledException)) CancelAction();
            else SetGenericError(e);

            void CancelAction()
            {
                SetCancelation(false);
            }

            void SetGenericError(Exception ex)
            {
                _ = MainDataModel.BackwardsCommunicationHost.exceptionmsg(ex);
            }

            void SetError(Exception ex2, string debugMessage, string dialogTitle, string dialogText)
            {
                Trace.WriteLine(debugMessage + ":\n" + ex2.ToString());
                MainDataModel.BackwardsCommunicationHost.errormsg(dialogTitle, dialogText, ex2);
            }
        }

        private static string GetFileHash(string filePath)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        private static bool IsBundledFileInstalled(string sourcePath, string installedPath)
        {
            if (!File.Exists(sourcePath) || !File.Exists(installedPath))
            {
                return false;
            }

            FileInfo sourceInfo = new FileInfo(sourcePath);
            FileInfo installedInfo = new FileInfo(installedPath);

            if (sourceInfo.Length != installedInfo.Length)
            {
                return false;
            }

            return GetFileHash(sourcePath) == GetFileHash(installedPath);
        }

        private void CopyBundledRuntimeToVersionDirectory(string versionDirectory)
        {
            try
            {
                string runtimeDllSourcePath = GetBundledExtraDllSourcePath();
                if (string.IsNullOrWhiteSpace(versionDirectory) ||
                    !Directory.Exists(versionDirectory) ||
                    !File.Exists(runtimeDllSourcePath))
                {
                    return;
                }

                string runtimeDllDestinationPath = Path.Combine(versionDirectory, Constants.EXTRA_DLL_NAME);
                File.Copy(runtimeDllSourcePath, runtimeDllDestinationPath, true);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not copy bundled runtime DLL to '{versionDirectory}': {ex}");
            }
        }

        private bool ShouldUseInstalledStoreRelease(MCVersion version)
        {
            if (version == null || version.Type != VersionType.Release)
            {
                return false;
            }

            if (!IsOfficialStoreReleaseInstalled())
            {
                return false;
            }

            return string.Equals(version.UUID, Constants.LATEST_RELEASE_UUID, StringComparison.OrdinalIgnoreCase) ||
                   version.MatchesOfficialStoreRelease;
        }

        private static void AddMinecraftDirectory(ISet<string> directories, string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            try
            {
                string normalizedPath = Path.GetFullPath(directoryPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (Directory.Exists(normalizedPath))
                {
                    directories.Add(normalizedPath);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not normalize Minecraft directory '{directoryPath}': {ex.Message}");
            }
        }

        private static bool IsLaunchableMinecraftExecutable(string executablePath)
        {
            string executableName = Path.GetFileNameWithoutExtension(executablePath);
            if (string.IsNullOrWhiteSpace(executableName))
            {
                return false;
            }

            return !string.Equals(executableName, "GameLaunchHelper", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(executableName, "custominstallexec", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryLaunchExecutablePath(string executablePath, string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return false;
            }

            try
            {
                RememberLaunchMethod($"Direct executable {executablePath}");
                using Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                        ? Path.GetDirectoryName(executablePath)
                        : workingDirectory,
                    UseShellExecute = true
                });

                return process != null;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Direct executable launch failed for '{executablePath}': {ex}");
                return false;
            }
        }

        private void CopyDllsToXboxGames()
        {
            try
            {
                string xboxGamesContentPath = @"C:\XboxGames\Minecraft for Windows\Content";
                string bundledDllDir = GetBundledDllDirectoryPath();
                string modDllPath = GetBundledModSourcePath();
                string runtimeDllPath = GetBundledExtraDllSourcePath();

                if (!Directory.Exists(bundledDllDir))
                {
                    Trace.WriteLine($"Bundled DLL directory not found: {bundledDllDir}");
                    return;
                }

                if (!Directory.Exists(xboxGamesContentPath))
                {
                    Trace.WriteLine($"Xbox Games Content directory not found: {xboxGamesContentPath}");
                    return;
                }

                foreach (string dllFile in new[] { modDllPath, runtimeDllPath }.Where(File.Exists))
                {
                    string fileName = Path.GetFileName(dllFile);
                    string destPath = Path.Combine(xboxGamesContentPath, fileName);
                    File.Copy(dllFile, destPath, true);
                    Trace.WriteLine($"Copied DLL to Xbox Games: {fileName}");
                }

                Trace.WriteLine("DLLs successfully copied to Xbox Games Content directory");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error copying DLLs to Xbox Games: {ex.Message}");
            }
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            CancelSource?.Dispose();
        }

        #endregion
    }
}
