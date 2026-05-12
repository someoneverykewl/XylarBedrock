using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using XylarBedrock.Classes;

namespace XylarBedrock.Handlers
{
    public static class AddonsCatalogHandler
    {
        public const string CurseForgeSearchUrl = "https://www.curseforge.com/minecraft-bedrock/search?class=addons&page=1&pageSize=20&sortBy=relevancy";
        private static readonly TimeSpan DefaultCacheFreshness = TimeSpan.FromMinutes(20);
        private const string EmbeddedActionsImage = "/XylarBedrock;component/Resources/addons/actions.jpg";
        private const string EmbeddedActionsPackageResourceName = "XylarBedrock.Resources.addons.Actions.mcpack";

        public static List<AddonEntry> BuildDefaultCatalog()
        {
            return new List<AddonEntry>()
            {
                BuildCustomActionsAddon()
            };
        }

        public static string BuildCurseForgeSearchUrl(string searchText, int page = 1)
        {
            string baseUrl =
                $"https://www.curseforge.com/minecraft-bedrock/search?class=addons&page={Math.Max(page, 1)}&pageSize=20&sortBy=relevancy";

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return baseUrl;
            }

            return $"{baseUrl}&search={Uri.EscapeDataString(searchText.Trim())}";
        }

        public static string GetCustomAddonPackagePath()
        {
            string cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XylarBedrock",
                "addons");

            return Path.Combine(cacheDirectory, "Actions.mcpack");
        }

        public static string EnsureCustomAddonPackagePath()
        {
            string packagePath = GetCustomAddonPackagePath();
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);

            using Stream embeddedStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(EmbeddedActionsPackageResourceName);

            if (embeddedStream == null)
            {
                throw new FileNotFoundException(
                    $"Bundled addon resource '{EmbeddedActionsPackageResourceName}' could not be found.");
            }

            using FileStream outputStream = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None);
            embeddedStream.CopyTo(outputStream);

            return packagePath;
        }

        public static string GetManagedDownloadDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XylarBedrock",
                "addon-downloads");
        }

        public static string GetCatalogCachePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XylarBedrock",
                "addons-cache.json");
        }

        public static List<AddonEntry> LoadCachedRemoteAddons()
        {
            string cachePath = GetCatalogCachePath();
            if (!File.Exists(cachePath)) return new List<AddonEntry>();

            try
            {
                string rawJson = File.ReadAllText(cachePath);
                CatalogCacheEntry cacheEntry = JsonConvert.DeserializeObject<CatalogCacheEntry>(rawJson);
                if (cacheEntry?.Items == null) return new List<AddonEntry>();

                return cacheEntry.Items
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Title) && !string.IsNullOrWhiteSpace(x.ImagePath))
                    .ToList();
            }
            catch
            {
                return new List<AddonEntry>();
            }
        }

        public static bool HasFreshRemoteCache()
        {
            return HasFreshRemoteCache(DefaultCacheFreshness);
        }

        public static bool HasFreshRemoteCache(TimeSpan maxAge)
        {
            string cachePath = GetCatalogCachePath();
            if (!File.Exists(cachePath)) return false;

            try
            {
                FileInfo cacheInfo = new FileInfo(cachePath);
                DateTime referenceTime = cacheInfo.LastWriteTimeUtc;

                if (referenceTime == DateTime.MinValue)
                {
                    return false;
                }

                return DateTime.UtcNow - referenceTime <= maxAge;
            }
            catch
            {
                return false;
            }
        }

        public static void SaveCachedRemoteAddons(IEnumerable<AddonEntry> addons)
        {
            if (addons == null) return;

            string cachePath = GetCatalogCachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            CatalogCacheEntry cacheEntry = new CatalogCacheEntry()
            {
                UpdatedAtUtc = DateTime.UtcNow,
                Items = addons
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Title) && !string.IsNullOrWhiteSpace(x.ImagePath))
                    .Take(48)
                    .Select(x => new AddonEntry()
                    {
                        Title = x.Title,
                        Author = x.Author,
                        Description = x.Description,
                        SourceLabel = x.SourceLabel,
                        ImagePath = x.ImagePath,
                        InstallUri = x.InstallUri,
                        PageUri = x.PageUri,
                        LocalPackagePath = x.LocalPackagePath,
                        DownloadsText = x.DownloadsText,
                        UpdatedText = x.UpdatedText,
                        FileSizeText = x.FileSizeText,
                        GameVersionText = x.GameVersionText,
                        IsCustom = x.IsCustom
                    })
                    .ToList()
            };

            File.WriteAllText(cachePath, JsonConvert.SerializeObject(cacheEntry, Formatting.Indented));
        }

        public static void CleanupManagedDownloads()
        {
            string downloadDirectory = GetManagedDownloadDirectory();
            if (!Directory.Exists(downloadDirectory)) return;

            foreach (string filePath in Directory.GetFiles(downloadDirectory))
            {
                string extension = Path.GetExtension(filePath);
                bool isAddonPackage =
                    extension.Equals(".mcpack", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".mcaddon", StringComparison.OrdinalIgnoreCase);

                bool isTemporaryDownload =
                    extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".crdownload", StringComparison.OrdinalIgnoreCase);

                if (!isAddonPackage && !isTemporaryDownload)
                {
                    continue;
                }

                try
                {
                    FileInfo fileInfo = new FileInfo(filePath);
                    bool shouldDelete =
                        fileInfo.Length == 0 ||
                        fileInfo.CreationTimeUtc < DateTime.UtcNow.AddDays(-2) ||
                        fileInfo.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-6) && isTemporaryDownload;

                    if (shouldDelete)
                    {
                        File.Delete(filePath);
                    }
                }
                catch
                {
                }
            }
        }

        public static void RepairCachedCatalog()
        {
            string cachePath = GetCatalogCachePath();
            if (!File.Exists(cachePath)) return;

            try
            {
                FileInfo cacheInfo = new FileInfo(cachePath);
                if (cacheInfo.Length == 0)
                {
                    File.Delete(cachePath);
                    return;
                }

                string rawJson = File.ReadAllText(cachePath);
                CatalogCacheEntry cacheEntry = JsonConvert.DeserializeObject<CatalogCacheEntry>(rawJson);
                if (cacheEntry?.Items == null)
                {
                    File.Delete(cachePath);
                }
            }
            catch
            {
                try
                {
                    File.Delete(cachePath);
                }
                catch
                {
                }
            }
        }

        public static string GetManagedDownloadPath(string suggestedFileName, string fallbackTitle = "addon")
        {
            string downloadDirectory = GetManagedDownloadDirectory();
            Directory.CreateDirectory(downloadDirectory);

            string fileName = string.IsNullOrWhiteSpace(suggestedFileName)
                ? $"{fallbackTitle}.mcaddon"
                : suggestedFileName;

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                fileName = Path.GetFileNameWithoutExtension(fileName) + ".mcaddon";
            }

            string destinationPath = Path.Combine(downloadDirectory, fileName);
            if (!File.Exists(destinationPath)) return destinationPath;

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string normalizedExtension = Path.GetExtension(fileName);
            int index = 1;

            while (File.Exists(destinationPath))
            {
                destinationPath = Path.Combine(downloadDirectory, $"{baseName}_{index}{normalizedExtension}");
                index++;
            }

            return destinationPath;
        }

        public static string GetCustomAddonImagePath()
        {
            return EmbeddedActionsImage;
        }

        private static AddonEntry BuildCustomActionsAddon()
        {
            return new AddonEntry()
            {
                Title = "Actions & Stuff",
                Author = "XylarBedrock",
                Description = "Bundled pack ready to install.",
                SourceLabel = "Custom",
                ImagePath = GetCustomAddonImagePath(),
                LocalPackagePath = GetCustomAddonPackagePath(),
                IsCustom = true
            };
        }

        private sealed class CatalogCacheEntry
        {
            public DateTime UpdatedAtUtc { get; set; }
            public List<AddonEntry> Items { get; set; } = new List<AddonEntry>();
        }
    }
}
