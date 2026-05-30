using JemExtensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using XylarBedrock.Classes;
using XylarBedrock.Enums;
using XylarBedrock.UpdateProcessor.Classes;
using XylarBedrock.UpdateProcessor.Enums;
using XylarBedrock.ViewModels;
using DownloadProgress = XylarBedrock.UpdateProcessor.Handlers.VersionManager.DownloadProgress;

namespace XylarBedrock.Downloaders
{
    public class VersionDownloader
    {
        private const string SupportedVersionsUri = "https://cdn.flarial.xyz/launcher/Supported.json";
        private const string PackageLinksUri = "https://cdn.jsdelivr.net/gh/MinecraftBedrockArchiver/GdkLinks@latest/urls.json";
        private const string SupportedVersionsCacheFileName = "supported_versions.json";
        private const string PackageLinksCacheFileName = "package_links.json";

        private static readonly Regex PackageVersionRegex = new Regex(
            @"_(?<version>\d+\.\d+\.\d+\.\d+)_",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(0.5));

        private static readonly HttpClient HttpClient = new HttpClient();

        private readonly SemaphoreSlim catalogLock = new SemaphoreSlim(1, 1);

        private List<MCVersion> cachedReleaseVersions = new List<MCVersion>();
        private MCVersion latestReleaseRef;

        public async Task DownloadVersion(
            string versionName,
            string packageID,
            int revisionNumber,
            string destination,
            DownloadProgress progress,
            CancellationToken cancellationToken,
            VersionType versionType)
        {
            MCVersion? version = cachedReleaseVersions.FirstOrDefault(candidate =>
                string.Equals(candidate.PackageID, packageID, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.UUID, packageID, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Name, versionName, StringComparison.OrdinalIgnoreCase));

            if (version == null)
            {
                throw new InvalidOperationException($"Could not resolve a downloadable package for {versionName}.");
            }

            await DownloadVersion(version, destination, progress, cancellationToken);
        }

        public async Task UpdateVersionList(ObservableCollection<MCVersion> versions, bool onLoad = false, bool forceStoreCheck = false)
        {
            await catalogLock.WaitAsync();
            try
            {
                versions.Clear();

                List<MCVersion> releases = await LoadSupportedReleaseVersionsAsync();
                cachedReleaseVersions = releases
                    .Select(CloneVersion)
                    .ToList();

                foreach (MCVersion version in cachedReleaseVersions)
                {
                    versions.Add(CloneVersion(version));
                }

                AddInstalledStoreVersionIfMissing(versions);
                versions.Sort((left, right) => left.Compare(right));

                MCVersion? newestRelease = versions.FirstOrDefault(version => version.IsRelease && !version.IsCustom);
                latestReleaseRef = newestRelease != null
                    ? CloneVersion(newestRelease)
                    : BuildLatestReleaseFallbackVersion();

                versions.Insert(0, new MCVersion(
                    Constants.LATEST_RELEASE_UUID,
                    Constants.LATEST_RELEASE_UUID,
                    GetLatestReleaseDisplayName(newestRelease),
                    VersionType.Release,
                    Constants.CurrentArchitecture));
            }
            finally
            {
                catalogLock.Release();
            }
        }

        public bool CanDownload(MCVersion version)
        {
            return version != null &&
                   (!string.IsNullOrWhiteSpace(version.DirectDownloadUrl) ||
                    (version.DirectDownloadUrls != null && version.DirectDownloadUrls.Count > 0));
        }

        public bool IsMsixvcPackage(MCVersion version)
        {
            return string.Equals(GetPackageFileExtension(version), ".msixvc", StringComparison.OrdinalIgnoreCase);
        }

        public string GetPackageFileExtension(MCVersion version)
        {
            string downloadUrl = version?.DirectDownloadUrls?
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)) ??
                version?.DirectDownloadUrl;

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return ".appx";
            }

            try
            {
                string extension = Path.GetExtension(new Uri(downloadUrl).AbsolutePath);
                return string.IsNullOrWhiteSpace(extension) ? ".appx" : extension;
            }
            catch
            {
                string extension = Path.GetExtension(downloadUrl);
                return string.IsNullOrWhiteSpace(extension) ? ".appx" : extension;
            }
        }

        public async Task DownloadVersion(MCVersion version, string destination, DownloadProgress progress, CancellationToken cancellationToken)
        {
            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            List<string> candidateUrls = version.DirectDownloadUrls?
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (candidateUrls.Count == 0 && !string.IsNullOrWhiteSpace(version.DirectDownloadUrl))
            {
                candidateUrls.Add(version.DirectDownloadUrl);
            }

            if (candidateUrls.Count == 0)
            {
                throw new InvalidOperationException($"No downloadable package is available for {version.Name}.");
            }

            Exception? lastException = null;
            foreach (string candidateUrl in candidateUrls)
            {
                try
                {
                    await DownloadDirectVersionAsync(candidateUrl, destination, progress, cancellationToken);
                    if (DoesDownloadedPackageMatchVersion(destination, version, candidateUrl))
                    {
                        version.DirectDownloadUrl = candidateUrl;
                        return;
                    }

                    TryDeleteFile(destination);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    TryDeleteFile(destination);
                }
            }

            throw new InvalidDataException(
                $"The selected Minecraft version ({version.Name}) was downloaded, but none of the packages matched the chosen release.",
                lastException);
        }

        public MCVersion GetVersion(VersioningMode versioningMode, string versionUUID)
        {
            if (versioningMode != VersioningMode.None)
            {
                if (versioningMode == VersioningMode.LatestRelease)
                {
                    MCVersion? officialStoreVersion = BuildOfficialStoreFallbackVersion();
                    if (officialStoreVersion != null)
                    {
                        return officialStoreVersion;
                    }

                    return latestReleaseRef != null ? CloneVersion(latestReleaseRef) : BuildLatestReleaseFallbackVersion();
                }

                return null;
            }

            MCVersion? version = MainDataModel.Default.Versions.FirstOrDefault(candidate =>
                string.Equals(candidate.UUID, versionUUID, StringComparison.OrdinalIgnoreCase));

            if (version != null)
            {
                return version;
            }

            string legacyCompatibleUuid = GetLegacyCompatibleVersionId(versionUUID);
            return MainDataModel.Default.Versions.FirstOrDefault(candidate =>
                string.Equals(candidate.UUID, legacyCompatibleUuid, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<List<MCVersion>> LoadSupportedReleaseVersionsAsync()
        {
            Dictionary<string, bool> supportedVersions = await LoadSupportedVersionsAsync();
            Dictionary<string, List<string>> releaseLinks = await LoadReleasePackageLinksAsync();
            List<MCVersion> versions = new List<MCVersion>();

            foreach ((string supportedKey, bool supported) in supportedVersions)
            {
                if (!supported)
                {
                    continue;
                }

                KeyValuePair<string, List<string>>? newestPackage = releaseLinks
                    .Where(entry => string.Equals(GetSupportedVersionKey(entry.Key), supportedKey, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(entry => ParseVersionOrDefault(entry.Key))
                    .FirstOrDefault();

                if (newestPackage == null ||
                    newestPackage.Value.Value == null ||
                    newestPackage.Value.Value.Count == 0)
                {
                    continue;
                }

                string displayName = GetDisplayVersion(supportedKey, newestPackage.Value.Key);
                string architecture = newestPackage.Value.Value
                    .Select(GetArchitectureFromUrl)
                    .FirstOrDefault(architectureName => !string.IsNullOrWhiteSpace(architectureName)) ?? "x64";

                string stableVersionId = GetStableVersionId(supportedKey, architecture);
                MCVersion version = new MCVersion(
                    stableVersionId,
                    stableVersionId,
                    displayName,
                    VersionType.Release,
                    architecture)
                {
                    DirectDownloadUrl = newestPackage.Value.Value.First(),
                    DirectDownloadUrls = newestPackage.Value.Value.ToList()
                };

                versions.Add(version);
            }

            return versions;
        }

        private async Task<Dictionary<string, bool>> LoadSupportedVersionsAsync()
        {
            string rawJson = await LoadJsonWithCacheAsync(
                SupportedVersionsUri,
                Path.Combine(GetCatalogCacheDirectory(), SupportedVersionsCacheFileName));

            return JsonConvert.DeserializeObject<Dictionary<string, bool>>(rawJson) ??
                   new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<string, List<string>>> LoadReleasePackageLinksAsync()
        {
            string rawJson = await LoadJsonWithCacheAsync(
                PackageLinksUri,
                Path.Combine(GetCatalogCacheDirectory(), PackageLinksCacheFileName));

            JObject root = JObject.Parse(rawJson);
            JObject? releaseRoot = root["release"] as JObject;
            Dictionary<string, List<string>> links = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (releaseRoot == null)
            {
                return links;
            }

            foreach (JProperty property in releaseRoot.Properties())
            {
                List<string> urls = property.Value
                    .Values<string>()
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (urls.Count == 0)
                {
                    continue;
                }

                links[property.Name] = urls;
            }

            return links;
        }

        private static async Task<string> LoadJsonWithCacheAsync(string uri, string cachePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            try
            {
                string content = await HttpClient.GetStringAsync(uri);
                File.WriteAllText(cachePath, content);
                return content;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Version catalog fetch failed for '{uri}': {ex.Message}");
                if (File.Exists(cachePath))
                {
                    return await File.ReadAllTextAsync(cachePath);
                }

                throw;
            }
        }

        private static async Task DownloadDirectVersionAsync(string downloadUrl, string destination, DownloadProgress progress, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long totalSize = response.Content.Headers.ContentLength ?? 0;
            progress(0, totalSize);

            using Stream inputStream = await response.Content.ReadAsStreamAsync();
            using FileStream outputStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[1024 * 1024];
            long transferred = 0;

            while (true)
            {
                int bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await outputStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                transferred += bytesRead;
                progress(transferred, totalSize);
            }
        }

        private static bool DoesDownloadedPackageMatchVersion(string packagePath, MCVersion expectedVersion, string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath) || expectedVersion == null)
            {
                return false;
            }

            try
            {
                if (string.Equals(Path.GetExtension(packagePath), ".msixvc", StringComparison.OrdinalIgnoreCase))
                {
                    return DoesMsixvcPackageNameMatchVersion(sourceUrl, expectedVersion);
                }

                using FileStream fileStream = File.OpenRead(packagePath);
                using ZipArchive zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read, false);
                ZipArchiveEntry? manifestEntry = zipArchive.GetEntry(MCVersionExtensions.MainifestFileName);
                if (manifestEntry == null)
                {
                    return false;
                }

                using Stream manifestStream = manifestEntry.Open();
                XDocument manifest = XDocument.Load(manifestStream);
                XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                XElement? identityNode = manifest.Root?.Element(ns + "Identity");
                string? manifestVersion = identityNode?.Attribute("Version")?.Value;

                if (!MinecraftVersion.TryParse(manifestVersion, out MinecraftVersion? packageVersion))
                {
                    return false;
                }

                return DoesDisplayVersionMatch(packageVersion.ToRealString(), expectedVersion.Name);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Downloaded package validation failed for '{expectedVersion.Name}': {ex}");
                return false;
            }
        }

        private static bool DoesMsixvcPackageNameMatchVersion(string packageUrl, MCVersion expectedVersion)
        {
            Match match = PackageVersionRegex.Match(GetPackageFileName(packageUrl));
            if (!match.Success)
            {
                Trace.WriteLine($"Could not read version from MSIXVC package name '{packageUrl}'.");
                return false;
            }

            if (!MinecraftVersion.TryParse(match.Groups["version"].Value, out MinecraftVersion? packageVersion))
            {
                return false;
            }

            return DoesDisplayVersionMatch(packageVersion.ToRealString(), expectedVersion.Name);
        }

        private static string GetPackageFileName(string packageUrl)
        {
            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileName(new Uri(packageUrl).AbsolutePath);
            }
            catch
            {
                return Path.GetFileName(packageUrl);
            }
        }

        private static bool DoesDisplayVersionMatch(string packageDisplayVersion, string expectedDisplayVersion)
        {
            if (string.IsNullOrWhiteSpace(packageDisplayVersion) ||
                string.IsNullOrWhiteSpace(expectedDisplayVersion))
            {
                return false;
            }

            return MCVersion.DisplayVersionsMatch(packageDisplayVersion, expectedDisplayVersion);
        }

        private static void AddInstalledStoreVersionIfMissing(ICollection<MCVersion> versions)
        {
            var packageManager = MainDataModel.Default?.PackageManager;
            if (packageManager == null || !packageManager.IsOfficialStoreReleaseInstalled())
            {
                return;
            }

            string officialStoreVersion = packageManager.GetOfficialStorePackageVersionString();
            if (!MinecraftVersion.TryParse(officialStoreVersion, out MinecraftVersion? parsedVersion))
            {
                return;
            }

            string displayVersion = parsedVersion.ToRealString();
            string architecture = Constants.CurrentArchitecture;

            if (versions.Any(version =>
                    version.Type == VersionType.Release &&
                    string.Equals(version.Name, displayVersion, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(version.Architecture, architecture, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            versions.Add(new MCVersion(
                $"installed-store-{displayVersion.Replace('.', '-')}-{architecture}",
                $"installed-store-{displayVersion.Replace('.', '-')}-{architecture}",
                displayVersion,
                VersionType.Release,
                architecture));
        }

        private MCVersion? BuildOfficialStoreFallbackVersion()
        {
            var packageManager = MainDataModel.Default?.PackageManager;
            if (packageManager == null || !packageManager.IsOfficialStoreReleaseInstalled())
            {
                return null;
            }

            string officialStoreVersion = packageManager.GetOfficialStorePackageVersionString();
            string versionName = officialStoreVersion;
            if (MinecraftVersion.TryParse(officialStoreVersion, out MinecraftVersion? parsedVersion))
            {
                versionName = parsedVersion.ToRealString();
            }

            if (string.IsNullOrWhiteSpace(versionName))
            {
                versionName = latestReleaseRef?.Name ?? "Minecraft for Windows";
            }

            return new MCVersion(
                Constants.LATEST_RELEASE_UUID,
                Constants.LATEST_RELEASE_UUID,
                versionName,
                VersionType.Release,
                Constants.CurrentArchitecture);
        }

        private MCVersion BuildLatestReleaseFallbackVersion()
        {
            string name = latestReleaseRef?.Name ?? "Minecraft for Windows";
            return new MCVersion(
                Constants.LATEST_RELEASE_UUID,
                Constants.LATEST_RELEASE_UUID,
                name,
                VersionType.Release,
                Constants.CurrentArchitecture);
        }

        private string GetLatestReleaseDisplayName(MCVersion? latestReleaseVersion)
        {
            string officialStoreVersion = MainDataModel.Default.PackageManager.GetOfficialStorePackageVersionString();
            if (MinecraftVersion.TryParse(officialStoreVersion, out MinecraftVersion? installedVersion))
            {
                officialStoreVersion = installedVersion.ToRealString();
            }

            if (!string.IsNullOrWhiteSpace(officialStoreVersion))
            {
                return $"Minecraft for Windows ({officialStoreVersion})";
            }

            if (latestReleaseVersion != null && !string.IsNullOrWhiteSpace(latestReleaseVersion.Name))
            {
                return $"Minecraft for Windows ({latestReleaseVersion.Name})";
            }

            return "Minecraft for Windows";
        }

        private string GetCatalogCacheDirectory()
        {
            string currentLocation = MainDataModel.Default.FilePaths.CurrentLocation;
            Directory.CreateDirectory(currentLocation);
            return currentLocation;
        }

        private static string GetSupportedVersionKey(string exactVersion)
        {
            if (!MinecraftVersion.TryParse(exactVersion, out MinecraftVersion? version))
            {
                return exactVersion;
            }

            if (version.Major == 1 && version.Minor >= 26)
            {
                return $"1.{version.Minor}.{version.Patch}";
            }

            if (version.Major == 1 && version.Patch >= 100)
            {
                return $"1.{version.Minor}.{version.Patch}";
            }

            return version.Revision > 0
                ? $"{version.Major}.{version.Minor}.{version.Patch}.{version.Revision}"
                : version.Patch > 0
                    ? $"{version.Major}.{version.Minor}.{version.Patch}"
                    : $"{version.Major}.{version.Minor}";
        }

        private static string GetDisplayVersion(string supportedVersion)
        {
            if (!MinecraftVersion.TryParse(supportedVersion, out MinecraftVersion? version))
            {
                return supportedVersion;
            }

            if (version.Major == 1 && version.Minor >= 26)
            {
                return $"{version.Minor}.{version.Patch}";
            }

            if (version.Major == 1 && version.Patch >= 100)
            {
                return $"{version.Major}.{version.Minor}.{version.Patch}";
            }

            return version.Revision > 0
                ? $"{version.Major}.{version.Minor}.{version.Patch}.{version.Revision}"
                : version.Patch > 0
                    ? $"{version.Major}.{version.Minor}.{version.Patch}"
                    : $"{version.Major}.{version.Minor}";
        }

        private static string GetDisplayVersion(string supportedVersion, string exactPackageVersion)
        {
            if (MinecraftVersion.TryParse(supportedVersion, out MinecraftVersion? supported) &&
                supported.Major == 1 &&
                supported.Minor >= 26)
            {
                return GetDisplayVersion(supportedVersion);
            }

            if (MinecraftVersion.TryParse(exactPackageVersion, out MinecraftVersion? exact))
            {
                return exact.ToRealString();
            }

            return GetDisplayVersion(supportedVersion);
        }

        private static string GetArchitectureFromUrl(string downloadUrl)
        {
            if (downloadUrl?.Contains("_x86__", StringComparison.OrdinalIgnoreCase) == true)
            {
                return "x86";
            }

            if (downloadUrl?.Contains("_arm__", StringComparison.OrdinalIgnoreCase) == true)
            {
                return "arm";
            }

            if (downloadUrl?.Contains("_arm64__", StringComparison.OrdinalIgnoreCase) == true)
            {
                return "arm64";
            }

            return "x64";
        }

        private static string GetStableVersionId(string supportedVersion, string architecture)
        {
            return $"xylar-release-{supportedVersion.Replace('.', '-')}-{architecture}";
        }

        private static string GetLegacyCompatibleVersionId(string versionId)
        {
            if (string.IsNullOrWhiteSpace(versionId))
            {
                return versionId;
            }

            return versionId.StartsWith("flarial-release-", StringComparison.OrdinalIgnoreCase)
                ? "xylar-release-" + versionId.Substring("flarial-release-".Length)
                : versionId;
        }

        private static Version ParseVersionOrDefault(string version)
        {
            return Version.TryParse(version, out Version? parsedVersion)
                ? parsedVersion
                : new Version(0, 0, 0, 0);
        }

        private static MCVersion CloneVersion(MCVersion version)
        {
            return new MCVersion(version.UUID, version.PackageID, version.Name, version.Type, version.Architecture, version.RevisionNumber)
            {
                CustomName = version.CustomName,
                DirectDownloadUrl = version.DirectDownloadUrl,
                DirectDownloadUrls = version.DirectDownloadUrls?.ToList() ?? new List<string>()
            };
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
