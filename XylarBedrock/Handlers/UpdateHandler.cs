using System;
using System.Windows;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Diagnostics;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Collections.Generic;
using Newtonsoft.Json;
using XylarBedrock;
using System.Runtime.InteropServices;
using XylarBedrock.ViewModels;
using System.Linq;
using System.Threading;
using XylarBedrock.Core;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using XylarBedrock.Pages.General;

namespace XylarBedrock.Handlers
{
    public class UpdateHandler
    {
        #region Definitions


        public List<GithubReleaseInfo> Notes
        {
            get
            {
                var list = new List<GithubReleaseInfo>();
                list.AddRange(ReleaseNotes);
                list.AddRange(PrereleaseNotes);
                list.AddRange(BetaNotes);
                list.Sort((x, y) => y.published_at.CompareTo(x.published_at));
                return list;
            }
        }
        private List<GithubReleaseInfo> ReleaseNotes { get; set; } = new List<GithubReleaseInfo>();
        private List<GithubReleaseInfo> PrereleaseNotes { get; set; } = new List<GithubReleaseInfo>();
        private List<GithubReleaseInfo> BetaNotes { get; set; } = new List<GithubReleaseInfo>();



        #endregion

        #region Accessors

        public bool isLatestBeta()
        {
            var list = Notes;
            if (list.Count == 0) return false;
            if (Properties.LauncherSettings.Default.UseBetaBuilds) return list[0].prerelease;
            else return false;
        }

        public string GetLatestTag()
        {
            var list = Notes;
            if (list.Count == 0) return string.Empty;

            if (Properties.LauncherSettings.Default.UseBetaBuilds) return list[0].tag_name;
            else if (list.Exists(x => !x.prerelease)) return list.First(x => !x.prerelease).tag_name;
            else return string.Empty;
        }

        public string GetLatestTagBody()
        {
            var list = Notes;
            if (list.Count == 0) return string.Empty;

            if (Properties.LauncherSettings.Default.UseBetaBuilds) return list[0].body;
            else if (list.Exists(x => !x.prerelease)) return list.First(x => x.prerelease == false).body;
            else return string.Empty;
        }

        public GithubReleaseInfo GetLatestRelease()
        {
            var list = Notes;
            if (list.Count == 0) return null;

            if (Properties.LauncherSettings.Default.UseBetaBuilds) return list[0];
            return list.FirstOrDefault(x => !x.prerelease);
        }

        #endregion

        #region Init

        public UpdateHandler()
        {

        }

        #endregion

        #region Update Checking

        public void CheckForUpdates()
        {
            Task.Run(async () =>
            {
                await CheckForUpdatesAsync();
            });

        }
        public async Task<bool> CheckForUpdatesAsync(bool onLoad = false)
        {
            if (onLoad && Debugger.IsAttached && !Constants.Debugging.CheckForUpdatesOnLoad) return false;
            System.Diagnostics.Trace.WriteLine("Checking for updates");
            try
            {
                ReleaseNotes.Clear();
                BetaNotes.Clear();
                PrereleaseNotes.Clear();

                await Beta_GetJSON();
                await Release_GetJSON();
                return CompareUpdate();
            }
            catch (Exception err)
            {
                System.Diagnostics.Trace.WriteLine("Check for updates failed\nError:" + err.Message);
                return false;
            }
        }
        private async Task Release_GetJSON()
        {
            var url = GithubAPI.RELEASE_URL;
            var notes = await GetUpdateNotes(url);

            foreach (var note in notes)
            {
                if (note.prerelease) PrereleaseNotes.Add(note);
                else ReleaseNotes.Add(note);
            }

            foreach (var entry in ReleaseNotes) entry.isBeta = false;
            foreach (var entry in PrereleaseNotes) entry.isBeta = false;

        }
        private async Task Beta_GetJSON()
        {
            var url = GithubAPI.BETA_URL;
            BetaNotes = await GetUpdateNotes(url);
            foreach (var entry in BetaNotes) entry.isBeta = true;
        }


        #endregion

        #region Button
        public void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (isLatestBeta()) JemExtensions.WebExtensions.LaunchWebLink(Constants.UPDATES_BETA_PAGE);
            else JemExtensions.WebExtensions.LaunchWebLink(Constants.UPDATES_RELEASE_PAGE);
        }

        public async Task ShowUpdatePromptAsync(GithubReleaseInfo releaseInfo = null)
        {
            releaseInfo ??= GetLatestRelease();
            if (releaseInfo == null)
            {
                return;
            }

            bool shouldUpdate = false;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Window owner = Application.Current.MainWindow;
                UpdatePromptWindow prompt = new UpdatePromptWindow(releaseInfo)
                {
                    Owner = owner
                };

                shouldUpdate = prompt.ShowDialog() == true;
            });

            if (shouldUpdate)
            {
                await InstallUpdateFromReleaseAsync(releaseInfo);
            }
        }

        public Task ShowTestUpdatePromptAsync()
        {
            GithubReleaseInfo fakeRelease = new GithubReleaseInfo()
            {
                name = "XylarBedrock v0.0.0.6",
                tag_name = "v0.0.0.6",
                body = "Test update prompt. This is only a preview so you can see the new updater UI.",
                html_url = Constants.UPDATES_RELEASE_PAGE,
                published_at = DateTime.UtcNow,
                prerelease = false,
                isBeta = false,
                assets = Array.Empty<GithubAsset>()
            };

            return ShowUpdatePromptAsync(fakeRelease);
        }

        private async Task InstallUpdateFromReleaseAsync(GithubReleaseInfo releaseInfo)
        {
            string applyScriptPath = null;

            try
            {
                await MainViewModel.Default.ShowWaitingDialog(async () =>
                {
                    applyScriptPath = await PrepareUpdateApplyScriptAsync(releaseInfo);
                });

                if (string.IsNullOrWhiteSpace(applyScriptPath))
                {
                    throw new InvalidOperationException("The update helper could not be prepared.");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{applyScriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                Environment.Exit(0);
            }
            catch (Exception err)
            {
                Trace.WriteLine("Automatic update failed\nError:" + err);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        "XylarBedrock could not install the update automatically. The GitHub release page will open so you can update manually.",
                        App.DisplayName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
                UpdateButton_Click(this, new RoutedEventArgs());
            }
        }

        private async Task<string> PrepareUpdateApplyScriptAsync(GithubReleaseInfo releaseInfo)
        {
            GithubAsset asset = FindInstallerAsset(releaseInfo);
            string downloadUrl = GetAssetDownloadUrl(asset);

            string updateRoot = Path.Combine(Path.GetTempPath(), "XylarBedrockUpdate", Guid.NewGuid().ToString("N"));
            string downloadDirectory = Path.Combine(updateRoot, "download");
            string extractDirectory = Path.Combine(updateRoot, "extract");
            string payloadDirectory = Path.Combine(updateRoot, "payload");
            string payloadDllDirectory = Path.Combine(payloadDirectory, Constants.BUNDLED_MODS_DIRECTORY_NAME);

            Directory.CreateDirectory(downloadDirectory);
            Directory.CreateDirectory(extractDirectory);
            Directory.CreateDirectory(payloadDirectory);

            string zipPath = Path.Combine(downloadDirectory, SanitizeFileName(asset.name ?? "XylarBedrock_Update.zip"));

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.TryParseAdd(@"XylarBedrock-Updater");
                if (string.Equals(downloadUrl, asset.url, StringComparison.OrdinalIgnoreCase))
                {
                    client.DefaultRequestHeaders.Accept.TryParseAdd("application/octet-stream");
                }

                using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream downloadStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = File.Create(zipPath))
                    {
                        await downloadStream.CopyToAsync(fileStream);
                    }
                }
            }

            ZipFile.ExtractToDirectory(zipPath, extractDirectory, true);

            string stagedExePath = Directory
                .EnumerateFiles(extractDirectory, "XylarBedrock.exe", SearchOption.AllDirectories)
                .FirstOrDefault();

            string stagedDllDirectory = Directory
                .EnumerateDirectories(extractDirectory, Constants.BUNDLED_MODS_DIRECTORY_NAME, SearchOption.AllDirectories)
                .FirstOrDefault(IsCompleteDllDirectory);

            if (string.IsNullOrWhiteSpace(stagedExePath) || !File.Exists(stagedExePath))
            {
                throw new InvalidOperationException("The release ZIP does not contain XylarBedrock.exe.");
            }

            if (string.IsNullOrWhiteSpace(stagedDllDirectory))
            {
                throw new InvalidOperationException($"The release ZIP does not contain a complete {Constants.BUNDLED_MODS_DIRECTORY_NAME} folder.");
            }

            File.Copy(stagedExePath, Path.Combine(payloadDirectory, "XylarBedrock.exe"), true);
            CopyDirectory(stagedDllDirectory, payloadDllDirectory, true);

            return CreateApplyUpdateScript(updateRoot, payloadDirectory);
        }

        private static GithubAsset FindInstallerAsset(GithubReleaseInfo releaseInfo)
        {
            GithubAsset[] assets = releaseInfo?.assets ?? Array.Empty<GithubAsset>();
            List<GithubAsset> zipAssets = assets
                .Where(asset => asset != null && !string.IsNullOrWhiteSpace(asset.name) && asset.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();

            GithubAsset selectedAsset =
                zipAssets.FirstOrDefault(asset => asset.name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)) ??
                zipAssets.FirstOrDefault(asset => asset.name.Contains("release", StringComparison.OrdinalIgnoreCase)) ??
                zipAssets.FirstOrDefault();

            if (selectedAsset == null)
            {
                throw new InvalidOperationException("This release does not include a XylarBedrock ZIP asset.");
            }

            return selectedAsset;
        }

        private static string GetAssetDownloadUrl(GithubAsset asset)
        {
            if (!string.IsNullOrWhiteSpace(asset.browser_download_url))
            {
                return asset.browser_download_url;
            }

            if (!string.IsNullOrWhiteSpace(asset.url))
            {
                return asset.url;
            }

            throw new InvalidOperationException("The selected update asset has no download URL.");
        }

        private static bool IsCompleteDllDirectory(string directoryPath)
        {
            return File.Exists(Path.Combine(directoryPath, Constants.BUNDLED_MOD_DLL_NAME)) &&
                   File.Exists(Path.Combine(directoryPath, Constants.BUNDLED_TOOLS_DLL_NAME)) &&
                   File.Exists(Path.Combine(directoryPath, Constants.EXTRA_DLL_NAME));
        }

        private static string CreateApplyUpdateScript(string updateRoot, string payloadDirectory)
        {
            string currentExePath = GetCurrentExecutablePath();
            string installDirectory = Path.GetDirectoryName(currentExePath) ?? AppContext.BaseDirectory;
            string payloadExePath = Path.Combine(payloadDirectory, "XylarBedrock.exe");
            string payloadDllDirectory = Path.Combine(payloadDirectory, Constants.BUNDLED_MODS_DIRECTORY_NAME);
            string targetDllDirectory = Path.Combine(installDirectory, Constants.BUNDLED_MODS_DIRECTORY_NAME);
            string scriptPath = Path.Combine(updateRoot, "apply-update.ps1");
            int currentProcessId = Process.GetCurrentProcess().Id;

            StringBuilder script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine($"$pidToWait = {currentProcessId}");
            script.AppendLine($"$payloadExe = {PowerShellLiteral(payloadExePath)}");
            script.AppendLine($"$payloadDll = {PowerShellLiteral(payloadDllDirectory)}");
            script.AppendLine($"$targetExe = {PowerShellLiteral(currentExePath)}");
            script.AppendLine($"$targetDll = {PowerShellLiteral(targetDllDirectory)}");
            script.AppendLine($"$updateRoot = {PowerShellLiteral(updateRoot)}");
            script.AppendLine("try {");
            script.AppendLine("    while (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 250 }");
            script.AppendLine("    New-Item -ItemType Directory -Path $targetDll -Force | Out-Null");
            script.AppendLine("    Copy-Item -LiteralPath $payloadExe -Destination $targetExe -Force");
            script.AppendLine("    Copy-Item -Path (Join-Path $payloadDll '*') -Destination $targetDll -Force -Recurse");
            script.AppendLine("    Start-Process -FilePath $targetExe -WorkingDirectory (Split-Path -Parent $targetExe)");
            script.AppendLine("}");
            script.AppendLine("catch {");
            script.AppendLine("    Add-Type -AssemblyName System.Windows.Forms");
            script.AppendLine("    [System.Windows.Forms.MessageBox]::Show('XylarBedrock could not finish the automatic update. Please extract the release ZIP manually or run the launcher from a writable folder.', 'XylarBedrock Updater', 'OK', 'Warning') | Out-Null");
            script.AppendLine("    if (Test-Path -LiteralPath $targetExe) { Start-Process -FilePath $targetExe -WorkingDirectory (Split-Path -Parent $targetExe) }");
            script.AppendLine("}");
            script.AppendLine("finally {");
            script.AppendLine("    Start-Sleep -Seconds 2");
            script.AppendLine("    Remove-Item -LiteralPath $updateRoot -Recurse -Force -ErrorAction SilentlyContinue");
            script.AppendLine("}");

            File.WriteAllText(scriptPath, script.ToString(), new UTF8Encoding(false));
            return scriptPath;
        }

        private static string GetCurrentExecutablePath()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
                {
                    return Environment.ProcessPath;
                }

                string mainModulePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(mainModulePath))
                {
                    return mainModulePath;
                }
            }
            catch
            {
                // Fall back to the normal launcher name beside the executable base directory.
            }

            return Path.Combine(AppContext.BaseDirectory, "XylarBedrock.exe");
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (string filePath in Directory.EnumerateFiles(sourceDirectory))
            {
                string targetPath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
                File.Copy(filePath, targetPath, overwrite);
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(sourceDirectory))
            {
                string targetDirectory = Path.Combine(destinationDirectory, Path.GetFileName(childDirectory));
                CopyDirectory(childDirectory, targetDirectory, overwrite);
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return fileName;
        }

        private static string PowerShellLiteral(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        #endregion

        private bool CompareUpdate()
        {
            string OnlineTag = GetLatestTag();
            string LocalTag = App.Version;
            System.Diagnostics.Trace.WriteLine("Current tag: " + LocalTag);
            System.Diagnostics.Trace.WriteLine("Latest tag: " + OnlineTag);

            try
            {
                // if current tag < than latest tag
                if (IsVersionNewer(LocalTag, OnlineTag))
                {
                    System.Diagnostics.Trace.WriteLine("New version available!");
                    return true;
                }
                else return false;
            }
            catch
            {
                return false;
            }
        }
        public bool IsVersionNewer(string localVersionStr, string remoteVersionStr)
        {
            localVersionStr = NormalizeVersionText(localVersionStr);
            remoteVersionStr = NormalizeVersionText(remoteVersionStr);

            int CheckGroup(string[] local, string[] remote, int index)
            {
                var requiredLength = index + 1;
                if (local.Length >= requiredLength && remote.Length >= requiredLength)
                {
                    if (int.TryParse(local[index], out int localInt) && int.TryParse(remote[index], out int remoteInt))
                    {
                        //Debugging Only
                        //Console.WriteLine(string.Format("Local Number {0}: {1}", index, localInt));
                        //Console.WriteLine(string.Format("Local Number {0}: {1}", index, remoteInt));
                        if (localInt < remoteInt)
                        {
                            return 1;
                        }
                        else if (localInt == remoteInt)
                        {
                            return 0;
                        }
                        else if (localInt > remoteInt)
                        {
                            return -1;
                        }

                    }
                }
                return -2;
            }

            string[] localGroups = localVersionStr.Split('.');
            string[] remoteGroups = remoteVersionStr.Split('.');

            var yearResult = CheckGroup(localGroups, remoteGroups, 0);
            if (yearResult == -2 || yearResult == -1) return false;
            else if (yearResult == 1) return true;
            else if (yearResult == 0)
            {
                var monthResult = CheckGroup(localGroups, remoteGroups, 1);
                if (monthResult == -2 || monthResult == -1) return false;
                else if (monthResult == 1) return true;
                else if (monthResult == 0)
                {
                    var dayResult = CheckGroup(localGroups, remoteGroups, 2);
                    if (dayResult == -2 || dayResult == -1) return false;
                    else if (dayResult == 1) return true;
                    else if (dayResult == 0)
                    {
                        var buildResult = CheckGroup(localGroups, remoteGroups, 3);
                        if (buildResult == -2 || buildResult == -1) return false;
                        else if (buildResult == 1) return true;
                        else if (buildResult == 0)
                        {
                            return false;
                        }
                    }
                }
            }



            return false;
        }

        private static string NormalizeVersionText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            Match match = Regex.Match(value, @"\d+(?:\.\d+){0,3}", RegexOptions.CultureInvariant);
            if (!match.Success) return value.Trim();

            List<string> parts = match.Value.Split('.').ToList();
            while (parts.Count < 4)
            {
                parts.Add("0");
            }

            return string.Join(".", parts.Take(4));
        }

        private async Task<List<GithubReleaseInfo>> GetUpdateNotes(string url)
        {
            HttpClient client = new HttpClient();
            string json = string.Empty;
            client.DefaultRequestHeaders.UserAgent.TryParseAdd(@"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.106 Safari/537.36");
            var httpResponse = await client.GetStreamAsync(url);
            using (var streamReader = new StreamReader(httpResponse)) json = streamReader.ReadToEnd();
            return JsonConvert.DeserializeObject<List<GithubReleaseInfo>>(json);
        }
    }
}

