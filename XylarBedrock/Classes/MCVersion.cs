using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using XylarBedrock.Classes;
using XylarBedrock.UpdateProcessor.Enums;
using XylarBedrock.UpdateProcessor.Extensions;
using XylarBedrock.UpdateProcessor.Interfaces;
using XylarBedrock.UpdateProcessor.Classes;
using XylarBedrock.UpdateProcessor.Handlers;
using XylarBedrock.ViewModels;
using Newtonsoft.Json;
using PropertyChanged;
using Windows.Management.Deployment;

namespace XylarBedrock.Classes
{

    [AddINotifyPropertyChangedInterface]
    public class MCVersion
    {
        public MCVersion(string uuid, string pkgId, string name, VersionType type, string architecture, int revisionNumber = 1)
        {
            this.UUID = uuid;
            this.PackageID = pkgId;
            this.Name = name;
            this.Type = type;
            this.Architecture = architecture;
            this.RevisionNumber = revisionNumber;
            this.PackageType = this.Compare(Constants.GetMinimumGDKVersion()) >= 0 ? PackageType.GDK : PackageType.UWP;
        }

        public MCVersion(string name)
        {
            this.Name = name;
        }

        public string UUID { get; set; }
        public string PackageID { get; set; }
        public string Name { get; set; }
        public string Architecture { get; set; }
        public string CustomName { get; set; }
        public string DirectDownloadUrl { get; set; } = string.Empty;
        public List<string> DirectDownloadUrls { get; set; } = new List<string>();
        public VersionType Type { get; set; }
        public int RevisionNumber { get; set; } = 1;
        public PackageType PackageType { get; private set; }
        public bool IsBeta
        {
            get => Type == VersionType.Beta;
        }
        public bool IsRelease
        {
            get => Type == VersionType.Release;
        }
        public bool IsPreview
        {
            get => Type == VersionType.Preview;
        }
        public bool IsCustom
        {
            get => UUID != PackageID;
        }
        public bool IsInstalled
        {
            get
            {
                Depends.On(GameDirectory, RequireSizeRecalculation);
                return File.Exists(ManifestPath);
            }
        }

        public string GameDirectory
        {
            get
            {
                Depends.On(UUID);
                return Path.GetFullPath(MainDataModel.Default.FilePaths.VersionsFolder + UUID);
            }
        }
        public string DisplayName
        {
            get
            {
                Depends.On(Type, Name, Architecture);

                string _TypeSuffix = string.Empty;
                string _ArchSuffix = string.Empty;


                if (!VersionDbExtensions.DoesVerionArchMatch(Constants.CurrentArchitecture, Architecture))
                    _ArchSuffix = $" [{Architecture}]";

                switch (Type)
                {
                    case VersionType.Beta:
                        _TypeSuffix = string.Format(" ({0})", "Beta"); //TODO: Localize String
                        break;
                    case VersionType.Preview:
                        _TypeSuffix = string.Format(" ({0})", "Preview"); //TODO: Localize String
                        break;
                }

                return (IsCustom ? CustomName : Name) + _TypeSuffix + _ArchSuffix;
            }
        }
        public string InstallationSize
        {
            get
            {
                Depends.On(RequireSizeRecalculation, IsInstalled);

                if (File.Exists(ManifestPath))
                {
                    if (Constants.Debugging.CalculateVersionSizes) Task.Run(GetInstallSize);
                    else RequireSizeRecalculation = false;
                    return StoredInstallationSize;
                }

                if (MatchesOfficialStoreRelease)
                {
                    return "Installed in Microsoft Store";
                }

                return CanBeDownloadedFromCatalog() ? "Ready to install" : "Not installed";
            }
        }

        public bool CanManageFromVersionsPage
        {
            get
            {
                Depends.On(IsInstalled, DirectDownloadUrl, DirectDownloadUrls, MatchesOfficialStoreRelease);
                return IsInstalled ||
                       MatchesOfficialStoreRelease ||
                       !string.IsNullOrWhiteSpace(DirectDownloadUrl) ||
                       (DirectDownloadUrls != null && DirectDownloadUrls.Count > 0);
            }
        }

        public string PrimaryActionText
        {
            get
            {
                Depends.On(IsInstalled, MatchesOfficialStoreRelease);
                if (MatchesOfficialStoreRelease)
                {
                    return "Play";
                }

                return "Install";
            }
        }

        public bool MatchesOfficialStoreRelease
        {
            get
            {
                if (!IsRelease || IsCustom)
                {
                    return false;
                }

                var packageManager = MainDataModel.Default?.PackageManager;
                if (packageManager == null || !packageManager.IsOfficialStoreReleaseInstalled())
                {
                    return false;
                }

                string officialStoreVersion = packageManager.GetOfficialStorePackageVersionString();
                if (!MinecraftVersion.TryParse(officialStoreVersion, out MinecraftVersion installedVersion))
                {
                    return false;
                }

                if (!MinecraftVersion.TryParse(Name, out MinecraftVersion selectedVersion))
                {
                    return false;
                }

                if (installedVersion.CompareTo(selectedVersion) == 0)
                {
                    return true;
                }

                return DisplayVersionsMatch(officialStoreVersion, Name);
            }
        }

        public static bool DisplayVersionsMatch(string installedVersion, string selectedVersion)
        {
            string installedDisplayVersion = NormalizeDisplayVersion(installedVersion);
            string selectedDisplayVersion = NormalizeDisplayVersion(selectedVersion);

            if (string.IsNullOrWhiteSpace(installedDisplayVersion) ||
                string.IsNullOrWhiteSpace(selectedDisplayVersion))
            {
                return false;
            }

            return string.Equals(installedDisplayVersion, selectedDisplayVersion, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDisplayVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return string.Empty;
            }

            string normalized = version.Trim();
            if (MinecraftVersion.TryParse(normalized, out MinecraftVersion parsedVersion))
            {
                if (parsedVersion.Major == 1 &&
                    (parsedVersion.Minor >= 26 || parsedVersion.Patch >= 1000))
                {
                    normalized = parsedVersion.ToRealString();
                }
            }

            int minimumParts = normalized.StartsWith("1.", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            List<string> parts = normalized.Split('.').ToList();
            while (parts.Count > minimumParts &&
                   string.Equals(parts.LastOrDefault(), "0", StringComparison.OrdinalIgnoreCase))
            {
                parts.RemoveAt(parts.Count - 1);
            }

            return string.Join(".", parts);
        }

        private bool CanBeDownloadedFromCatalog()
        {
            if (!string.IsNullOrWhiteSpace(DirectDownloadUrl) ||
                (DirectDownloadUrls != null && DirectDownloadUrls.Count > 0))
            {
                return true;
            }

            if (IsInstalled || MatchesOfficialStoreRelease)
            {
                return true;
            }

            return MainDataModel.Default?.PackageManager?.VersionDownloader?.CanDownload(this) == true;
        }
        public string IconPath
        {
            get
            {
                Depends.On(IsBeta, IsPreview, IsRelease, IsCustom);
                if (IsCustom) return Constants.CUSTOM_VERSION_ICONPATH;
                else if (IsBeta) return Constants.BETA_VERSION_ICONPATH;
                else if (IsPreview) return Constants.PREVIEW_VERSION_ICONPATH;
                else if (IsRelease) return Constants.RELEASE_VERSION_ICONPATH;
                else return Constants.UNKNOWN_VERSION_ICONPATH;
            }
        }
        public string VersionListIconPath
        {
            get
            {
                Depends.On(Name, IsRelease, IsCustom, IsBeta, IsPreview);
                if (!IsRelease || IsCustom)
                {
                    return IconPath;
                }

                string[] releaseIcons =
                {
                    "Grass_Block.png",
                    "Copper_Block.png",
                    "Block_of_Diamond.png",
                    "Deepslate_Diamond_Ore.png",
                    "Observer.png",
                    "Enchanting_Table.png",
                    "Mangrove_Log.png",
                    "Tuff.png",
                    "Ancient_Debris.png",
                    "Ender_Chest.png"
                };

                int stableHash = 0;
                foreach (char character in Name ?? string.Empty)
                {
                    stableHash = unchecked((stableHash * 31) + character);
                }

                int index = Math.Abs(stableHash == int.MinValue ? 0 : stableHash) % releaseIcons.Length;
                return Constants.INSTALLATIONS_PREFABED_ICONS_ROOT + releaseIcons[index];
            }
        }
        public string ManifestPath
        {
            get
            {
                Depends.On(GameDirectory);
                return Path.Combine(GameDirectory, MCVersionExtensions.MainifestFileName);
            }
        }
        public string IdentificationPath
        {
            get
            {
                Depends.On(GameDirectory);
                return Path.Combine(GameDirectory, MCVersionExtensions.IdentificationFilename);
            }
        }

        #region Size Calcualtion

        [JsonIgnore] private string StoredInstallationSize { get; set; } = "...";
        [JsonIgnore] private bool RequireSizeRecalculation { get; set; } = false;
        [JsonIgnore] private static bool Internal_SizeCalcInProgress { get; set; } = false;


        private async Task GetInstallSize()
        {
            while (Internal_SizeCalcInProgress) await Task.Delay(500);

            await Task.Run(() =>
            {
                if (!RequireSizeRecalculation) return;

                if (IsInstalled)
                {
                    Internal_SizeCalcInProgress = true;
                    var dirSize = GetDirectorySize(Path.GetFullPath(GameDirectory));
                    string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                    int order = 0;
                    double len = dirSize;
                    while (len >= 1024 && order < sizes.Length - 1)
                    {
                        order++;
                        len = len / 1024;
                    }
                    StoredInstallationSize = String.Format("{0:0.##} {1}", len, sizes[order]);
                    RequireSizeRecalculation = false;
                    Internal_SizeCalcInProgress = false;
                }
                else
                {
                    StoredInstallationSize = "...";
                    RequireSizeRecalculation = false;
                }
            });

            ulong GetDirectorySize(string dir)
            {
                dynamic fso = Activator.CreateInstance(System.Type.GetTypeFromProgID("Scripting.FileSystemObject"));
                dynamic fldr = fso.GetFolder(dir);
                return (ulong)fldr.size;
            }
        }

        #endregion

        #region Methods

        public string GetPackageNameFromMainifest()
        {
            var (Name, Version, ProcessorArchitecture) = MCVersionExtensions.GetCommonPackageValues(ManifestPath);
            return String.Join("_", Name, Version, ProcessorArchitecture);

        }
        public void OpenDirectory()
        {
            string Directory = Path.GetFullPath(GameDirectory);
            if (!System.IO.Directory.Exists(Directory)) System.IO.Directory.CreateDirectory(Directory);
            Process.Start("explorer.exe", Directory);
        }
        public void UpdateFolderSize()
        {
            RequireSizeRecalculation = true;
        }
        public int Compare(MCVersion y)
        {
            try
            {
                var a = Version.Parse(this.Name);
                var b = Version.Parse(y.Name);
                return b.CompareTo(a);
            }
            catch
            {
                return y.Name.CompareTo(this.Name);
            }

        }

        #endregion
    }

    public static class MCVersionExtensions
    {
        public const string IdentificationFilename = "PackageID.txt";
        public const string MainifestFileName = "AppxManifest.xml";

        static Tuple<string, string, string> GetCommonPackageValues_CommonFunctionality(string manifestXml)
        {
            try
            {
                XDocument XMLDoc = XDocument.Parse(manifestXml);
                var Descendants = XMLDoc.Descendants();
                XElement Identity = Descendants.Where(x => x.Name.LocalName == "Identity").FirstOrDefault();
                string Name = Identity.Attribute("Name").Value;
                string Version = Identity.Attribute("Version").Value;
                string ProcessorArchitecture = Identity.Attribute("ProcessorArchitecture").Value;

                return new Tuple<string, string, string>(Name, Version, ProcessorArchitecture);
            }
            catch
            {
                return new Tuple<string, string, string>("???", "???", "???");
            }
        }
        public static async Task<Tuple<string, string, string>> GetCommonPackageValuesAsync(string manifestPath)
        {
            string manifestXml = await File.ReadAllTextAsync(manifestPath);
            return MCVersionExtensions.GetCommonPackageValues_CommonFunctionality(manifestXml);

        }
        public static Tuple<string,string,string> GetCommonPackageValues(string manifestPath)
        {
            string manifestXml = File.ReadAllText(manifestPath);
            return MCVersionExtensions.GetCommonPackageValues_CommonFunctionality(manifestXml);
        }
    }
}


