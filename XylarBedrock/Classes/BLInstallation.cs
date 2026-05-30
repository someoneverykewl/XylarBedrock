using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using XylarBedrock.Classes;
using Newtonsoft.Json;
using XylarBedrock.ViewModels;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using XylarBedrock.Enums;
using PropertyChanged;
using XylarBedrock.UpdateProcessor.Enums;
using XylarBedrock.UpdateProcessor.Classes;

namespace XylarBedrock.Classes
{

    [AddINotifyPropertyChangedInterface]    //88 Lines
    public class BLInstallation
    {
        public string DisplayName { get; set; }
        public string VersionUUID { get; set; }
        public string IconPath { get; set; }
        public bool IsCustomIcon { get; set; } = false;
        public string DirectoryName { get; set; }
        public bool ReadOnly { get; set; }
        public VersioningMode VersioningMode { get; set; } = VersioningMode.None;
        public DateTime LastPlayed { get; set; }
        public string InstallationUUID { get; set; } = Guid.NewGuid().ToString();

        #region Runtime Values

        [JsonIgnore]
        public string IconPath_Full
        {
            get
            {
                Depends.On(IsCustomIcon, IconPath);
                if (IsCustomIcon) return Path.Combine(MainDataModel.Default.FilePaths.GetCacheFolderPath(), IconPath);
                else return Constants.INSTALLATIONS_ICONPATH + IconPath;
            }
        }
        [JsonIgnore]
        public string DisplayName_Full
        {
            get
            {
                Depends.On(VersionUUID, DisplayName);
                if (VersionUUID == Constants.LATEST_RELEASE_UUID && ReadOnly) return "Minecraft for Windows";
                else if (VersionUUID == Constants.LATEST_PREVIEW_UUID && ReadOnly) return XylarBedrock.Localization.Language.LanguageManager.GetResource("VersionEntries_LatestPreview").ToString();
                //else if (VersionUUID == Constants.LATEST_BETA_UUID && ReadOnly) return XylarBedrock.Localization.Language.LanguageManager.GetResource("VersionEntries_LatestBeta").ToString();
                else if (string.IsNullOrWhiteSpace(DisplayName)) return XylarBedrock.Localization.Language.LanguageManager.GetResource("VersionEntries_UnnamedInstallation").ToString();
                else return DisplayName;
            }
        }
        [JsonIgnore]
        public string DirectoryName_Full
        {
            get
            {
                Depends.On(DirectoryName, DisplayName);
                if (string.IsNullOrEmpty(DirectoryName))
                {
                    char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
                    string result = new string(DisplayName.Where(ch => !invalidFileNameChars.Contains(ch)).ToArray());
                    return result;
                }
                else return DirectoryName;
            }
        }
        [JsonIgnore, SafeForDependencyAnalysis]
        public MCVersion? Version
        {
            get
            {
                Depends.On(VersioningMode, VersionUUID);
                return MainDataModel.Default.PackageManager.VersionDownloader.GetVersion(VersioningMode, VersionUUID);
            }
        }
        [JsonIgnore]
        public bool IsOfficialInstallation
        {
            get
            {
                Depends.On(InstallationUUID);
                return string.Equals(InstallationUUID, Constants.LATEST_RELEASE_UUID, StringComparison.OrdinalIgnoreCase);
            }
        }
        [JsonIgnore]
        public bool IsInstalledVersion
        {
            get
            {
                Depends.On(VersionUUID, VersioningMode, InstallationUUID);
                if (Version?.IsInstalled == true)
                {
                    return true;
                }

                if (IsOfficialInstallation)
                {
                    return MainDataModel.Default.PackageManager.IsOfficialStoreReleaseInstalled();
                }

                if (Version?.MatchesOfficialStoreRelease == true)
                {
                    return true;
                }

                return false;
            }
        }
        [JsonIgnore]
        public bool IsRelease
        {
            get
            {
                Depends.On(VersionUUID, VersioningMode);
                if (VersioningMode == VersioningMode.LatestRelease) return true;
                else return Version?.IsRelease ?? false;
            }
        }
        [JsonIgnore]
        public bool IsBeta
        {
            get
            {
                Depends.On(VersionUUID, VersioningMode);
                if (VersioningMode == VersioningMode.LatestBeta) return true;
                else return Version?.IsBeta ?? false;
            }
        }
        [JsonIgnore]
        public bool IsPreview
        {
            get
            {
                Depends.On(VersionUUID, VersioningMode);
                if (VersioningMode == VersioningMode.LatestPreview) return true;
                else return Version?.IsPreview ?? false;
            }
        }
        [JsonIgnore]
        public string VersionName
        {
            get
            {
                Depends.On(VersionUUID, VersioningMode);
                if (VersionUUID == Constants.LATEST_RELEASE_UUID && ReadOnly)
                {
                    string officialStoreVersion = MainDataModel.Default.PackageManager.GetOfficialStorePackageVersionString();
                    if (MinecraftVersion.TryParse(officialStoreVersion, out MinecraftVersion parsedOfficialVersion))
                    {
                        return parsedOfficialVersion.ToRealString();
                    }

                    if (!string.IsNullOrWhiteSpace(Version?.Name))
                    {
                        return Version.Name;
                    }

                    return "Original Microsoft Store build";
                }
                string version = Version?.Name ?? "???";
                //if (VersioningMode == VersioningMode.LatestPreview) return XylarBedrock.Localization.Language.LanguageManager.GetResource("VersionEntries_LatestPreview").ToString();
                //else if (VersioningMode == VersioningMode.LatestBeta) return XylarBedrock.Localization.Language.LanguageManager.GetResource("VersionEntries_LatestBeta").ToString();
                //else if (VersioningMode == VersioningMode.LatestRelease) return XylarBedrock.Localization.Language.LanguageManager.GetResource("VersionEntries_LatestRelease").ToString();
                return version;
            }
        }
        [JsonIgnore]
        public string LastPlayedT
        {
            get
            {
                Depends.On(LastPlayed);
                return LastPlayed.ToString("s");
            }
        }

        public VersionType VersionType
        {
            get
            {
                return Version?.Type ?? VersionType.Release;
            }
        }

        #endregion

        public BLInstallation Clone(string newName = null)
        {
            var clone = (BLInstallation)this.MemberwiseClone();
            clone.InstallationUUID = Guid.NewGuid().ToString();
            if (!string.IsNullOrEmpty(newName)) clone.DisplayName = newName;
            return clone;
        }

        public void DeleteUserData()
        {
            string Directory = MainDataModel.Default.FilePaths.GetInstallationPath(Properties.LauncherSettings.Default.CurrentProfileUUID, DirectoryName_Full);
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }

        public void OpenDirectory()
        {
            string Directory = MainDataModel.Default.FilePaths.GetInstallationPackageDataPath(Properties.LauncherSettings.Default.CurrentProfileUUID, DirectoryName_Full);
            if (!System.IO.Directory.Exists(Directory)) System.IO.Directory.CreateDirectory(Directory);
            Process.Start("explorer.exe", Directory);
        }

    }

}


