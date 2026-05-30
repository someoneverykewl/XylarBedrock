using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using XylarBedrock.Classes;

namespace XylarBedrock.Handlers
{
    public static class AddonsCatalogHandler
    {
        public const string CurseForgeSearchUrl = "https://www.curseforge.com/minecraft-bedrock/search?class=addons&page=1&pageSize=20&sortBy=relevancy";
        private static readonly TimeSpan DefaultCacheFreshness = TimeSpan.FromMinutes(20);
        private const string EmbeddedActionsImage = "/XylarBedrock;component/Resources/addons/actions.jpg";
        private const string EmbeddedFallbackAddonImage = "/XylarBedrock;component/Resources/images/icons/addons_icon.png";
        private const string EmbeddedActionsPackageResourceName = "XylarBedrock.Resources.addons.Actions.mcpack";
        private const int MaxAddonDownloadHops = 5;
        private static readonly HttpClient HttpClient = BuildHttpClient();

        public static List<AddonEntry> BuildDefaultCatalog()
        {
            return new List<AddonEntry>()
            {
                BuildCustomActionsAddon()
            };
        }

        public static string BuildCurseForgeSearchUrl(string searchText, int page = 1)
        {
            return BuildCurseForgeSearchUrl(searchText, page, "addons");
        }

        public static IEnumerable<string> BuildCurseForgeSearchUrls(string searchText, int page = 1)
        {
            yield return BuildCurseForgeSearchUrl(searchText, page, "addons");
        }

        private static string BuildCurseForgeSearchUrl(string searchText, int page, string contentClass)
        {
            string baseUrl =
                $"https://www.curseforge.com/minecraft-bedrock/search?class={Uri.EscapeDataString(contentClass)}&page={Math.Max(page, 1)}&pageSize=20&sortBy=relevancy";

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return baseUrl;
            }

            return $"{baseUrl}&search={Uri.EscapeDataString(searchText.Trim())}";
        }

        public static async Task<AddonCatalogPage> FetchTrustedCatalogAsync(string searchText, int page = 1, CancellationToken cancellationToken = default)
        {
            return await Task.FromResult(new AddonCatalogPage()
            {
                CurrentPage = Math.Max(page, 1),
                MaxKnownPage = 1,
                ProviderSucceeded = false
            });
        }

        public static async Task<string> DownloadRemotePackageAsync(
            AddonEntry addon,
            IProgress<AddonDownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (addon == null || string.IsNullOrWhiteSpace(addon.InstallUri))
            {
                throw new InvalidOperationException("No addon download link was provided.");
            }

            if (!Uri.TryCreate(addon.InstallUri, UriKind.Absolute, out Uri currentUri))
            {
                throw new InvalidOperationException("The addon download link was not valid.");
            }

            if (IsBrowserOnlyDownloadUri(currentUri))
            {
                throw new AddonBrowserDownloadRequiredException(
                    currentUri.ToString(),
                    "This addon provider needs the browser download flow.");
            }

            for (int hop = 0; hop < MaxAddonDownloadHops; hop++)
            {
                using HttpRequestMessage request = BuildAddonDownloadRequest(currentUri, addon);
                using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                string mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (IsHtmlMediaType(mediaType))
                {
                    string html = await response.Content.ReadAsStringAsync(cancellationToken);
                    string nextLink = FindFirstAddonPackageDownloadLink(html);

                    string resolved = ResolveUri(currentUri, nextLink);
                    if (string.IsNullOrWhiteSpace(resolved) ||
                        !Uri.TryCreate(resolved, UriKind.Absolute, out Uri nextUri) ||
                        Uri.Compare(nextUri, currentUri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        throw new InvalidOperationException("The provider returned a web page instead of an addon package.");
                    }

                    if (IsBrowserOnlyDownloadUri(nextUri))
                    {
                        throw new AddonBrowserDownloadRequiredException(
                            nextUri.ToString(),
                            "This addon provider needs the browser download flow.");
                    }

                    currentUri = nextUri;
                    continue;
                }

                return await SaveAddonPackageResponseAsync(response, addon, progress, cancellationToken);
            }

            throw new InvalidOperationException("The addon provider redirected too many times before giving a package.");
        }

        private static async Task<string> SaveAddonPackageResponseAsync(
            HttpResponseMessage response,
            AddonEntry addon,
            IProgress<AddonDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            string mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            string fileName = GetFileNameFromResponse(response, addon);
            string extension = Path.GetExtension(fileName);

            if (!IsAddonPackageExtension(extension))
            {
                bool looksLikeDownload =
                    response.Content.Headers.ContentDisposition != null ||
                    mediaType.IndexOf("zip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mediaType.IndexOf("octet-stream", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!looksLikeDownload)
                {
                    throw new InvalidOperationException("The provider did not return an .mcpack or .mcaddon file.");
                }

                fileName = $"{SafeFileName(addon.Title, "addon")}.mcaddon";
            }

            string destinationPath = GetManagedDownloadPath(fileName, addon.Title);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            long receivedBytes = 0;
            byte[] buffer = new byte[128 * 1024];

            await using (Stream inputStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream outputStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                while (true)
                {
                    int read = await inputStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read <= 0) break;

                    await outputStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    receivedBytes += read;
                    progress?.Report(new AddonDownloadProgress(receivedBytes, totalBytes > 0 ? totalBytes : (long?)null));
                }
            }

            FileInfo outputInfo = new FileInfo(destinationPath);
            if (!outputInfo.Exists || outputInfo.Length == 0)
            {
                throw new IOException("The addon download finished, but the file was empty.");
            }

            if (LooksLikeHtmlFile(destinationPath))
            {
                TryDeleteFile(destinationPath);
                throw new InvalidOperationException("The provider returned a web page instead of an addon package.");
            }

            return destinationPath;
        }

        public static string TryGetCachedDownloadedAddonPackage(AddonEntry addon)
        {
            if (addon == null) return string.Empty;

            string cacheKey = BuildDownloadCacheKey(addon);
            if (string.IsNullOrWhiteSpace(cacheKey)) return string.Empty;

            try
            {
                string cacheIndexPath = GetDownloadCacheIndexPath();
                if (!File.Exists(cacheIndexPath)) return string.Empty;

                Dictionary<string, string> cacheIndex =
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(cacheIndexPath)) ??
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!cacheIndex.TryGetValue(cacheKey, out string packagePath) ||
                    !IsReusableAddonPackage(packagePath))
                {
                    return string.Empty;
                }

                return packagePath;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void RememberDownloadedAddonPackage(AddonEntry addon, string packagePath)
        {
            if (addon == null || !IsReusableAddonPackage(packagePath)) return;

            string cacheKey = BuildDownloadCacheKey(addon);
            if (string.IsNullOrWhiteSpace(cacheKey)) return;

            try
            {
                string cacheIndexPath = GetDownloadCacheIndexPath();
                Directory.CreateDirectory(Path.GetDirectoryName(cacheIndexPath)!);

                Dictionary<string, string> cacheIndex = File.Exists(cacheIndexPath)
                    ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(cacheIndexPath)) ??
                      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                cacheIndex[cacheKey] = packagePath;
                File.WriteAllText(cacheIndexPath, JsonConvert.SerializeObject(cacheIndex, Formatting.Indented));
            }
            catch
            {
            }
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

        private static string GetDownloadCacheIndexPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XylarBedrock",
                "addon-downloads-cache.json");
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
                    .Where(x => x != null &&
                                !string.IsNullOrWhiteSpace(x.Title) &&
                                IsUsableCatalogImagePath(x.ImagePath) &&
                                IsCurseForgeCatalogItem(x))
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

            List<AddonEntry> cacheItems = addons
                .Where(x => x != null &&
                            !string.IsNullOrWhiteSpace(x.Title) &&
                            IsUsableCatalogImagePath(x.ImagePath) &&
                            IsCurseForgeCatalogItem(x))
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
                .ToList();

            if (cacheItems.Count == 0)
            {
                return;
            }

            string cachePath = GetCatalogCachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            CatalogCacheEntry cacheEntry = new CatalogCacheEntry()
            {
                UpdatedAtUtc = DateTime.UtcNow,
                Items = cacheItems
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
                    return;
                }

                List<AddonEntry> usableItems = cacheEntry.Items
                    .Where(x => x != null &&
                                !string.IsNullOrWhiteSpace(x.Title) &&
                                IsUsableCatalogImagePath(x.ImagePath) &&
                                IsCurseForgeCatalogItem(x))
                    .ToList();

                if (usableItems.Count == 0)
                {
                    File.Delete(cachePath);
                    return;
                }

                if (usableItems.Count != cacheEntry.Items.Count)
                {
                    cacheEntry.Items = usableItems;
                    File.WriteAllText(cachePath, JsonConvert.SerializeObject(cacheEntry, Formatting.Indented));
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

        private static bool IsUsableCatalogImagePath(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return false;
            }

            if (imagePath.StartsWith("/", StringComparison.Ordinal))
            {
                return true;
            }

            if (Uri.TryCreate(imagePath, UriKind.Absolute, out Uri imageUri))
            {
                if (imageUri.IsFile)
                {
                    return File.Exists(imageUri.LocalPath);
                }

                if (imageUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    imageUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return !imageUri.AbsolutePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
                }
            }

            try
            {
                return File.Exists(imagePath);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCurseForgeCatalogItem(AddonEntry addon)
        {
            if (addon == null) return false;
            if (addon.IsCustom || addon.IsLocal) return true;

            return ContainsCurseForgeHost(addon.InstallUri) ||
                   ContainsCurseForgeHost(addon.PageUri) ||
                   addon.SourceLabel.IndexOf("CurseForge", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsCurseForgeHost(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            return Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
                   uri.Host.EndsWith("curseforge.com", StringComparison.OrdinalIgnoreCase);
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

        public static string GetFallbackAddonImagePath()
        {
            return EmbeddedFallbackAddonImage;
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

        private static HttpClient BuildHttpClient()
        {
            HttpClient client = new HttpClient(new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All
            });
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("XylarBedrock/0.0.0.5");
            return client;
        }

        private static HttpRequestMessage BuildAddonDownloadRequest(Uri downloadUri, AddonEntry addon)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            request.Headers.UserAgent.ParseAdd("XylarBedrock/0.0.0.5");
            request.Headers.Accept.ParseAdd("application/octet-stream");
            request.Headers.Accept.ParseAdd("application/zip");
            request.Headers.Accept.ParseAdd("application/x-zip-compressed");
            request.Headers.Accept.ParseAdd("text/html");
            request.Headers.Accept.ParseAdd("*/*");

            if (!string.IsNullOrWhiteSpace(addon.PageUri) && Uri.TryCreate(addon.PageUri, UriKind.Absolute, out Uri refererUri))
            {
                request.Headers.Referrer = refererUri;
            }
            else
            {
                request.Headers.Referrer = new Uri(downloadUri.GetLeftPart(UriPartial.Authority));
            }

            return request;
        }

        private static string CreateStableFileName(string value)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string FindFirstAddonPackageDownloadLink(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            string decodedHtml = WebUtility.HtmlDecode(html);

            Match packageMatch = Regex.Match(
                decodedHtml,
                "(?:href|data-url|data-href|data-download-url)=[\"'](?<href>[^\"']+\\.(?:mcpack|mcaddon)(?:\\?[^\"']*)?)[\"']",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (packageMatch.Success)
            {
                return packageMatch.Groups["href"].Value;
            }

            Match refreshMatch = Regex.Match(
                decodedHtml,
                "<meta[^>]*http-equiv=[\"']refresh[\"'][^>]*content=[\"'][^\"']*url=(?<href>[^\"']+)[\"']",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (refreshMatch.Success)
            {
                return refreshMatch.Groups["href"].Value;
            }

            MatchCollection linkMatches = Regex.Matches(
                decodedHtml,
                "(?:href|data-url|data-href|data-download-url)=[\"'](?<href>[^\"']+)[\"']",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (Match linkMatch in linkMatches)
            {
                string candidate = linkMatch.Groups["href"].Value;
                if (LooksLikeAddonDownloadCandidate(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static bool LooksLikeAddonDownloadCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;

            return candidate.IndexOf(".mcpack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   candidate.IndexOf(".mcaddon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   candidate.IndexOf("/download/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   candidate.IndexOf("downloadFile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   candidate.IndexOf("download-file", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ResolveUri(Uri baseUri, string rawUri)
        {
            if (string.IsNullOrWhiteSpace(rawUri)) return string.Empty;
            if (Uri.TryCreate(rawUri, UriKind.Absolute, out Uri absoluteUri)) return absoluteUri.ToString();
            return Uri.TryCreate(baseUri, rawUri, out Uri resolvedUri) ? resolvedUri.ToString() : string.Empty;
        }

        private static bool IsBrowserOnlyDownloadUri(Uri uri)
        {
            if (uri == null) return false;

            return uri.Host.EndsWith("curseforge.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFileNameFromResponse(HttpResponseMessage response, AddonEntry addon)
        {
            string fileName = response.Content.Headers.ContentDisposition?.FileNameStar;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = response.Content.Headers.ContentDisposition?.FileName;
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return SafeFileName(fileName.Trim('"'), $"{addon.Title}.mcaddon");
            }

            string pathFileName = string.Empty;
            if (response.RequestMessage?.RequestUri != null)
            {
                pathFileName = Path.GetFileName(response.RequestMessage.RequestUri.LocalPath);
            }

            return string.IsNullOrWhiteSpace(pathFileName)
                ? $"{SafeFileName(addon.Title, "addon")}.mcaddon"
                : SafeFileName(pathFileName, $"{addon.Title}.mcaddon");
        }

        private static string SafeFileName(string value, string fallback)
        {
            string fileName = string.IsNullOrWhiteSpace(value) ? fallback : value;

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(fileName) ? fallback : fileName;
        }

        private static bool IsAddonPackageExtension(string extension)
        {
            return extension.Equals(".mcpack", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".mcaddon", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHtmlMediaType(string mediaType)
        {
            return mediaType.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeHtmlFile(string path)
        {
            try
            {
                using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buffer = new byte[Math.Min(256, (int)Math.Min(stream.Length, 256))];
                int read = stream.Read(buffer, 0, buffer.Length);
                string prefix = System.Text.Encoding.UTF8.GetString(buffer, 0, read).TrimStart();
                return prefix.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                       prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsReusableAddonPackage(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

                string extension = Path.GetExtension(path);
                if (!IsAddonPackageExtension(extension)) return false;

                FileInfo fileInfo = new FileInfo(path);
                return fileInfo.Length > 0 && !LooksLikeHtmlFile(path);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildDownloadCacheKey(AddonEntry addon)
        {
            if (addon == null) return string.Empty;

            string identity = !string.IsNullOrWhiteSpace(addon.PageUri)
                ? addon.PageUri
                : addon.InstallUri;

            if (string.IsNullOrWhiteSpace(identity))
            {
                identity = addon.Title;
            }

            return string.IsNullOrWhiteSpace(identity)
                ? string.Empty
                : CreateStableFileName(identity.Trim().ToLowerInvariant());
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private sealed class CatalogCacheEntry
        {
            public DateTime UpdatedAtUtc { get; set; }
            public List<AddonEntry> Items { get; set; } = new List<AddonEntry>();
        }
    }

    public sealed class AddonCatalogPage
    {
        public int CurrentPage { get; set; }
        public int MaxKnownPage { get; set; } = 1;
        public string TotalCountText { get; set; } = string.Empty;
        public bool ProviderSucceeded { get; set; }
        public List<AddonEntry> Items { get; } = new List<AddonEntry>();
    }

    public sealed class AddonDownloadProgress
    {
        public AddonDownloadProgress(long bytesReceived, long? totalBytes)
        {
            BytesReceived = bytesReceived;
            TotalBytes = totalBytes;
        }

        public long BytesReceived { get; }
        public long? TotalBytes { get; }
    }

    public sealed class AddonBrowserDownloadRequiredException : Exception
    {
        public AddonBrowserDownloadRequiredException(string browserUri, string message)
            : base(message)
        {
            BrowserUri = browserUri;
        }

        public string BrowserUri { get; }
    }
}
