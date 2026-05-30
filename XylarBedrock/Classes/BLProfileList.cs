using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using XylarBedrock.Classes;
using JemExtensions;
using Newtonsoft.Json;
using XylarBedrock.Enums;
using PropertyChanged;
using System.ComponentModel;
using XylarBedrock.ViewModels;
using XylarBedrock.Handlers;
using Windows.Networking.NetworkOperators;
using XylarBedrock.UpdateProcessor.Extensions;

namespace XylarBedrock.Classes
{

    [AddINotifyPropertyChangedInterface]    //224 Lines
    public class BLProfileList : JemExtensions.WPF.NotifyPropertyChangedBase
    {
        private const string ManagedVersionInstallationPrefix = "catalog_version:";
        private static readonly string[] ManagedVersionIconPool = new[]
        {
            "Acacia_Leaves.png",
            "Ancient_Debris.png",
            "Bookshelf.png",
            "Bricks.png",
            "Cake.png",
            "Command_Block.png",
            "Copper_Block.png",
            "Crafting_Table.png",
            "Deepslate.png",
            "Diamond_Ore.png",
            "Enchanting_Table.png",
            "End_Stone.png",
            "Glowstone.png",
            "Grass_Block.png",
            "Grass_Path.png",
            "Honey_Block.png",
            "Mangrove_Planks.png",
            "Mycelium.png",
            "Nether_Bricks.png",
            "Observer.png",
            "Obsidian.png",
            "Pumpkin.png",
            "Redstone_Ore.png",
            "Slime_Block.png",
            "Snowy_Grass_Block.png",
            "TNT.png",
            "Warped_Planks.png"
        };
        public int Version = 2;



        public Dictionary<string, BLProfile> profiles { get; set; } = new Dictionary<string, BLProfile>();

        #region Runtime Values
        [JsonIgnore]
        public string FilePath { get; private set; } = string.Empty;

        [JsonIgnore]
        public string CurrentInstallationUUID
        {
            get
            {
                Depends.On(Properties.LauncherSettings.Default.CurrentInstallationUUID);
                return Properties.LauncherSettings.Default.CurrentInstallationUUID;
            }
            set
            {
                Properties.LauncherSettings.Default.CurrentInstallationUUID = value;
                Properties.LauncherSettings.Default.Save();
            }
        }
        [JsonIgnore] 
        public BLProfile CurrentProfile
        {
            get
            {
                Depends.On(Properties.LauncherSettings.Default.CurrentProfileUUID);
                if (profiles.ContainsKey(Properties.LauncherSettings.Default.CurrentProfileUUID)) return profiles[Properties.LauncherSettings.Default.CurrentProfileUUID];
                else return null;
            }
            set
            {
                if (profiles.ContainsKey(Properties.LauncherSettings.Default.CurrentProfileUUID)) profiles[Properties.LauncherSettings.Default.CurrentProfileUUID] = value;
            }
        }

        [JsonIgnore]
        public string CurrentProfileImagePath
        {
            get
            {
                Depends.On(Properties.LauncherSettings.Default.CurrentProfileUUID);
                if (profiles.ContainsKey(Properties.LauncherSettings.Default.CurrentProfileUUID)) return profiles[Properties.LauncherSettings.Default.CurrentProfileUUID].ImagePath;
                return string.Empty;
            }
        }
        [JsonIgnore] 
        public BLInstallation CurrentInstallation
        {
            get
            {
                Depends.On(CurrentInstallationUUID, CurrentInstallations);
                if (CurrentProfile == null) return null;
                else if (CurrentInstallations == null) return null;
                else if (CurrentInstallations.Any(x => x.InstallationUUID == CurrentInstallationUUID))
                    return CurrentInstallations.First(x => x.InstallationUUID == CurrentInstallationUUID);
                else return null;
            }
            set
            {
                if (CurrentProfile == null) return;
                else if (CurrentInstallations == null) return;
                else if (CurrentInstallations.Any(x => x.InstallationUUID == CurrentInstallationUUID))
                {
                    int index = CurrentInstallations.FindIndex(x => x.InstallationUUID == CurrentInstallationUUID);
                    CurrentInstallations[index] = value;
                }
                else return;
            }
        }
        [JsonIgnore] 
        public ObservableCollection<BLInstallation> CurrentInstallations
        {
            get
            {
                Depends.On(CurrentProfile);
                if (CurrentProfile == null) return null;
                else if (CurrentProfile.Installations == null) return null;
                else return CurrentProfile.Installations;
            }
            set
            {
                if (CurrentProfile == null) return;
                else if (CurrentProfile.Installations == null) return;
                else CurrentProfile.Installations = value;
            }
        }

        #endregion

        #region IO Methods

        public static BLProfileList Load(string filePath, string lastProfile = null, string lastInstallation = null)
        {
            string json;
            BLProfileList fileData = new BLProfileList();
            if (File.Exists(filePath))
            {
                json = File.ReadAllText(filePath);
                try
                {
                    fileData = JsonConvert.DeserializeObject<BLProfileList>(json, new JsonSerializerSettings()
                    {
                        NullValueHandling = NullValueHandling.Include,
                        MissingMemberHandling = MissingMemberHandling.Ignore
                    });
                }
                catch
                {
                    fileData = new BLProfileList();
                }
            }
            fileData.FilePath = filePath;
            fileData.Init(lastProfile, lastInstallation);
            fileData.Validate();
            return fileData;
        }
        public void Init(string lastProfile = null, string lastInstallation = null)
        {
            foreach(var profile in profiles) profile.Value.UUID = profile.Key;

            EnsureDefaultProfileExists();

            string savedProfileUuid = Properties.LauncherSettings.Default.CurrentProfileUUID;
            if (!string.IsNullOrWhiteSpace(lastProfile) && profiles.ContainsKey(lastProfile))
            {
                Properties.LauncherSettings.Default.CurrentProfileUUID = lastProfile;
            }
            else if (!string.IsNullOrWhiteSpace(savedProfileUuid) && profiles.ContainsKey(savedProfileUuid))
            {
                Properties.LauncherSettings.Default.CurrentProfileUUID = savedProfileUuid;
            }
            else if (profiles.Count != 0)
            {
                Properties.LauncherSettings.Default.CurrentProfileUUID = profiles.First().Key;
            }

            if (CurrentProfile != null)
            {
                string savedInstallationUuid = Properties.LauncherSettings.Default.CurrentInstallationUUID;

                if (!string.IsNullOrWhiteSpace(lastInstallation) && CurrentInstallations.Any(x => x.InstallationUUID == lastInstallation))
                {
                    CurrentInstallationUUID = lastInstallation;
                }
                else if (!string.IsNullOrWhiteSpace(savedInstallationUuid) && CurrentInstallations.Any(x => x.InstallationUUID == savedInstallationUuid))
                {
                    CurrentInstallationUUID = savedInstallationUuid;
                }
                else if (CurrentInstallations.Any(IsOfficialInstallation))
                {
                    CurrentInstallationUUID = Constants.LATEST_RELEASE_UUID;
                }
                else if (CurrentInstallations.FirstOrDefault(IsManagedVersionInstallation) is BLInstallation preferredManagedInstallation)
                {
                    CurrentInstallationUUID = preferredManagedInstallation.InstallationUUID;
                }
                else if (CurrentInstallations.Count != 0)
                {
                    CurrentInstallationUUID = CurrentInstallations.First().InstallationUUID;
                }
            }
        }
        public void Save(string filePath)
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }
        public void Save()
        {
            if (!string.IsNullOrEmpty(FilePath)) Save(FilePath);
        }
        public void Validate()
        {
            EnsureDefaultProfileExists();

            foreach (var profile in profiles.Values)
            {
                profile.Installations ??= new ObservableCollection<BLInstallation>();
                EnsureOfficialInstallation(profile);
            }

            Properties.LauncherSettings.Default.ShowReleases = true;
            Properties.LauncherSettings.Default.ShowBetas = false;
            Properties.LauncherSettings.Default.ShowPreviews = false;

            EnsureCurrentInstallationSelection();
            Properties.LauncherSettings.Default.Save();
            Save();
        }

        public bool SyncVisibleInstallationsFromVersions()
        {
            if (MainDataModel.Default.Versions == null || MainDataModel.Default.Versions.Count == 0)
            {
                return false;
            }

            bool changed = false;
            List<MCVersion> visibleVersions = MainDataModel.Default.Versions
                .Where(version =>
                    !version.IsCustom &&
                    !string.Equals(version.UUID, Constants.LATEST_RELEASE_UUID, StringComparison.OrdinalIgnoreCase) &&
                    VersionChooserPolicy.IsVisibleInChooser(version) &&
                    VersionDbExtensions.DoesVerionArchMatch(Constants.CurrentArchitecture, version.Architecture))
                .ToList();

            foreach (BLProfile profile in profiles.Values)
            {
                profile.Installations ??= new ObservableCollection<BLInstallation>();
                BLInstallation officialInstallation = EnsureOfficialInstallation(profile);

                List<BLInstallation> existingInstallations = profile.Installations.ToList();
                List<BLInstallation> userInstallations = existingInstallations
                    .Where(installation => !IsManagedVersionInstallation(installation) && !IsOfficialInstallation(installation))
                    .ToList();

                ObservableCollection<BLInstallation> rebuiltInstallations = new ObservableCollection<BLInstallation>();
                rebuiltInstallations.Add(officialInstallation);

                foreach (MCVersion version in visibleVersions)
                {
                    BLInstallation existingManagedInstallation = existingInstallations
                        .FirstOrDefault(installation =>
                            IsManagedVersionInstallation(installation) &&
                            string.Equals(installation.VersionUUID, version.UUID, StringComparison.OrdinalIgnoreCase));

                    rebuiltInstallations.Add(existingManagedInstallation == null
                        ? CreateManagedVersionInstallation(version)
                        : UpdateManagedVersionInstallation(existingManagedInstallation, version));
                }

                foreach (BLInstallation installation in userInstallations)
                {
                    rebuiltInstallations.Add(installation);
                }

                if (!InstallationOrderMatches(profile.Installations, rebuiltInstallations))
                {
                    profile.Installations = rebuiltInstallations;
                    changed = true;
                }
            }

            if (!EnsureCurrentInstallationSelection() && !changed)
            {
                return false;
            }

            Properties.LauncherSettings.Default.Save();
            Save();
            OnPropertyChanged(nameof(CurrentProfile));
            OnPropertyChanged(nameof(CurrentInstallations));
            OnPropertyChanged(nameof(CurrentInstallation));
            return true;
        }

        private void EnsureDefaultProfileExists()
        {
            if (profiles.Count != 0) return;

            const string defaultProfileUuid = "default_profile";
            profiles[defaultProfileUuid] = CreateDefaultProfile(defaultProfileUuid);
        }

        private static BLProfile CreateDefaultProfile(string uuid)
        {
            return new BLProfile("Default Profile", "Default Profile", uuid)
            {
                Installations = new ObservableCollection<BLInstallation>()
            };
        }

        private static BLInstallation CreateOfficialInstallation(DateTime lastPlayed = default)
        {
            return new BLInstallation()
            {
                DisplayName = "Minecraft for Windows",
                DirectoryName = "Minecraft for Windows",
                VersionUUID = Constants.LATEST_RELEASE_UUID,
                VersioningMode = VersioningMode.LatestRelease,
                IconPath = Constants.INSTALLATIONS_LATEST_RELEASE_ICONPATH,
                IsCustomIcon = false,
                ReadOnly = true,
                InstallationUUID = Constants.LATEST_RELEASE_UUID,
                LastPlayed = lastPlayed
            };
        }

        private static bool IsOfficialInstallation(BLInstallation installation)
        {
            return installation != null &&
                   string.Equals(installation.InstallationUUID, Constants.LATEST_RELEASE_UUID, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManagedVersionInstallation(BLInstallation installation)
        {
            return installation != null &&
                   installation.InstallationUUID != null &&
                   installation.InstallationUUID.StartsWith(ManagedVersionInstallationPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetManagedInstallationUUID(MCVersion version)
        {
            return ManagedVersionInstallationPrefix + version.UUID;
        }

        private static string GetManagedDisplayName(MCVersion version)
        {
            return $"Minecraft for Windows ({version.Name})";
        }

        private BLInstallation CreateManagedVersionInstallation(MCVersion version)
        {
            string installationName = GetManagedDisplayName(version);
            return new BLInstallation()
            {
                DisplayName = installationName,
                DirectoryName = ValidatePathName(installationName),
                VersionUUID = version.UUID,
                VersioningMode = VersioningMode.None,
                IconPath = GetDefaultInstallationIconPath(version),
                IsCustomIcon = false,
                ReadOnly = true,
                InstallationUUID = GetManagedInstallationUUID(version)
            };
        }

        private BLInstallation UpdateManagedVersionInstallation(BLInstallation installation, MCVersion version)
        {
            string installationName = GetManagedDisplayName(version);
            installation.DisplayName = installationName;
            installation.DirectoryName = ValidatePathName(installationName);
            installation.VersionUUID = version.UUID;
            installation.VersioningMode = VersioningMode.None;
            installation.IconPath = GetDefaultInstallationIconPath(version);
            installation.IsCustomIcon = false;
            installation.ReadOnly = true;
            installation.InstallationUUID = GetManagedInstallationUUID(version);
            return installation;
        }

        private static string GetDefaultInstallationIconPath(MCVersion version)
        {
            if (version == null)
            {
                return Constants.INSTALLATIONS_LATEST_RELEASE_ICONPATH;
            }

            if (version.IsCustom)
            {
                return string.IsNullOrWhiteSpace(version.IconPath)
                    ? Constants.INSTALLATIONS_FALLBACK_ICONPATH
                    : Path.GetFileName(version.IconPath);
            }

            return GetManagedVersionIconPath(version);
        }

        private static string GetManagedVersionIconPath(MCVersion version)
        {
            if (ManagedVersionIconPool.Length == 0)
            {
                return Constants.INSTALLATIONS_LATEST_RELEASE_ICONPATH;
            }

            string key = $"{version.UUID}|{version.Name}|{version.Architecture}";
            uint hash = 2166136261;

            foreach (char character in key)
            {
                hash ^= character;
                hash *= 16777619;
            }

            int iconIndex = (int)(hash % ManagedVersionIconPool.Length);
            return ManagedVersionIconPool[iconIndex];
        }

        private BLInstallation EnsureOfficialInstallation(BLProfile profile)
        {
            List<BLInstallation> officialInstallations = profile.Installations
                .Where(IsOfficialInstallation)
                .ToList();

            BLInstallation officialInstallation = officialInstallations.FirstOrDefault();
            if (officialInstallation == null)
            {
                DateTime lastPlayed = profile.Installations
                    .OrderByDescending(x => x.LastPlayed)
                    .FirstOrDefault(x => x.VersionUUID == Constants.LATEST_RELEASE_UUID)?.LastPlayed ?? default;

                officialInstallation = CreateOfficialInstallation(lastPlayed);
                profile.Installations.Insert(0, officialInstallation);
            }
            else
            {
                officialInstallation.DisplayName = "Minecraft for Windows";
                officialInstallation.DirectoryName = "Minecraft for Windows";
                officialInstallation.VersionUUID = Constants.LATEST_RELEASE_UUID;
                officialInstallation.VersioningMode = VersioningMode.LatestRelease;
                officialInstallation.IconPath = Constants.INSTALLATIONS_LATEST_RELEASE_ICONPATH;
                officialInstallation.IsCustomIcon = false;
                officialInstallation.ReadOnly = true;
                officialInstallation.InstallationUUID = Constants.LATEST_RELEASE_UUID;

                foreach (BLInstallation duplicate in officialInstallations.Skip(1).ToList())
                {
                    profile.Installations.Remove(duplicate);
                }

                int currentIndex = profile.Installations.IndexOf(officialInstallation);
                if (currentIndex > 0)
                {
                    profile.Installations.Move(currentIndex, 0);
                }
            }

            return officialInstallation;
        }

        private bool EnsureCurrentInstallationSelection()
        {
            if (CurrentProfile == null || CurrentInstallations == null || CurrentInstallations.Count == 0)
            {
                return false;
            }

            if (CurrentInstallations.Any(x => x.InstallationUUID == CurrentInstallationUUID))
            {
                return false;
            }

            if (CurrentInstallations.Any(IsOfficialInstallation))
            {
                CurrentInstallationUUID = Constants.LATEST_RELEASE_UUID;
                return true;
            }

            BLInstallation preferredManagedInstallation = CurrentInstallations
                .FirstOrDefault(IsManagedVersionInstallation);
            if (preferredManagedInstallation != null)
            {
                CurrentInstallationUUID = preferredManagedInstallation.InstallationUUID;
                return true;
            }

            CurrentInstallationUUID = CurrentInstallations.First().InstallationUUID;
            return true;
        }

        private static bool InstallationOrderMatches(IList<BLInstallation> current, IList<BLInstallation> updated)
        {
            if (ReferenceEquals(current, updated))
            {
                return true;
            }

            if (current == null || updated == null || current.Count != updated.Count)
            {
                return false;
            }

            for (int index = 0; index < current.Count; index++)
            {
                if (!string.Equals(current[index].InstallationUUID, updated[index].InstallationUUID, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void GenerateProfileImage(string img, string uuid)
        {
            string path = MainDataModel.Default.FilePaths.GetProfilePath(uuid);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            string new_img = Path.Combine(path, Constants.PROFILE_CUSTOM_IMG_NAME);
            if (string.IsNullOrEmpty(img)) return;
            else
            {
                try
                {
                    File.Copy(img, new_img, true);
                }
                catch
                {
                    //TODO: Add Error Message
                }
            }

        }

        #endregion

        #region Management Methods
        string ValidatePathName(string pathName)
        {
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
            return new string(pathName.Select(ch => invalidFileNameChars.Contains(ch) ? '_' : ch).ToArray());
        }
        public bool Profile_Add(string name, string uuid, string directory, string img)
        {
            var real_directory = ValidatePathName(directory);
            BLProfile profileSettings = new BLProfile(name, real_directory, uuid);
            

            if (profiles.ContainsKey(uuid)) return false;
            else
            {
                profiles.Add(uuid, profileSettings);
                GenerateProfileImage(img, uuid);

                Profile_Switch(uuid);
                Validate();
                Save();
                return true;
            }

        }
        public bool Profile_Edit(string name, string uuid, string directory, string img)
        {
            var real_directory = ValidatePathName(directory);

            if (!profiles.ContainsKey(uuid)) return false;
            else
            {
                profiles[uuid].Name = name;
                profiles[uuid].ProfilePath = name;
                GenerateProfileImage(img, uuid);

                Profile_Switch(uuid);
                Validate();
                Save();
                return true;
            }

        }
        public void Profile_Remove(string profileUUID)
        {
            if (profiles.ContainsKey(profileUUID) && profiles.Count > 1)
            {
                profiles.Remove(profileUUID);
                Save();
                Profile_Switch(profiles.FirstOrDefault().Key);
            }

        }
        public void Profile_Switch(string profileUUID)
        {
            if (profiles.ContainsKey(profileUUID))
            {
                Properties.LauncherSettings.Default.CurrentProfileUUID = profileUUID;      
                Properties.LauncherSettings.Default.Save();

                OnPropertyChanged(nameof(CurrentProfile));
                OnPropertyChanged(nameof(CurrentInstallations));
                OnPropertyChanged(nameof(CurrentInstallation));
                OnPropertyChanged(nameof(CurrentProfileImagePath));
            }
        }

        public void Installation_Add(BLInstallation installation)
        {
            if (CurrentProfile == null) return;
            if (CurrentInstallations == null) return;
            if (installation == null || installation.ReadOnly) return;
            if (!CurrentInstallations.Any(x => x.InstallationUUID == installation.InstallationUUID))
            {
                CurrentInstallations.Add(installation);
                Save();
            }
        }

        public void Installation_Move(BLInstallation installation, bool moveUp)
        {
            if (CurrentProfile == null) return;
            if (CurrentInstallations == null) return;
            if (CurrentInstallations.Any(x => x.InstallationUUID == installation.InstallationUUID))
            {
                int oldIndex = CurrentInstallations.FindIndex(x => x.InstallationUUID == installation.InstallationUUID);
                int count = CurrentInstallations.Count() - 1;
                int newIndex = oldIndex + (moveUp ? -1 : 1);
                if (newIndex >= 0 && newIndex <= count) CurrentInstallations.Move(oldIndex, newIndex);
                Save();
            }
        }

        public void Installation_MoveDown(BLInstallation installation)
        {
            Installation_Move(installation, false);
        }

        public void Installation_MoveUp(BLInstallation installation)
        {
            Installation_Move(installation, true);
        }

        public void Installation_Clone(BLInstallation installation)
        {
            if (CurrentProfile == null) return;
            if (CurrentInstallations == null) return;
            if (installation == null) return;
            if (CurrentInstallations.Any(x => x.InstallationUUID == installation.InstallationUUID))
            {
                string newName = installation.DisplayName;
                int i = 1;

                while (CurrentInstallations.Any(x => x.DisplayName == newName))
                {
                    newName = $"{installation.DisplayName} ({i})";
                    i++;
                }
                var Clone = installation.Clone(newName);
                Clone.DirectoryName = ValidatePathName(newName);
                Clone.ReadOnly = false;
                Installation_Add(Clone);
            }
        }
        public void Installation_Create(string name, MCVersion version, string directory, string iconPath = null, bool isCustom = false)
        {
            if (CurrentProfile == null || CurrentInstallations == null || version == null) return;

            GetVersionParams(version, out VersioningMode versioningMode, out string versionUUID);

            string displayName = string.IsNullOrWhiteSpace(name) ? version.DisplayName : name.Trim();
            string directoryName = ValidatePathName(string.IsNullOrWhiteSpace(directory) ? displayName : directory.Trim());

            BLInstallation installation = new BLInstallation()
            {
                DisplayName = displayName,
                VersionUUID = versionUUID,
                IconPath = string.IsNullOrWhiteSpace(iconPath) ? GetDefaultInstallationIconPath(version) : iconPath,
                IsCustomIcon = isCustom,
                DirectoryName = directoryName,
                ReadOnly = false,
                VersioningMode = versioningMode
            };

            Installation_Add(installation);
        }
        public void Installation_Edit(string uuid, string name, MCVersion version, string directory, string iconPath = null, bool isCustom = false)
        {
            if (CurrentProfile == null || CurrentInstallations == null || version == null || string.IsNullOrWhiteSpace(uuid)) return;

            BLInstallation installation = CurrentInstallations.FirstOrDefault(x => x.InstallationUUID == uuid);
            if (installation == null || installation.ReadOnly) return;

            GetVersionParams(version, out VersioningMode versioningMode, out string versionUUID);

            string previousDirectoryPath = MainDataModel.Default.FilePaths.GetInstallationPath(CurrentProfile.UUID, installation.DirectoryName_Full);
            string displayName = string.IsNullOrWhiteSpace(name) ? version.DisplayName : name.Trim();
            string directoryName = ValidatePathName(string.IsNullOrWhiteSpace(directory) ? displayName : directory.Trim());

            installation.DisplayName = displayName;
            installation.DirectoryName = directoryName;
            installation.VersionUUID = versionUUID;
            installation.VersioningMode = versioningMode;
            installation.IconPath = string.IsNullOrWhiteSpace(iconPath) ? GetDefaultInstallationIconPath(version) : iconPath;
            installation.IsCustomIcon = isCustom;

            string newDirectoryPath = MainDataModel.Default.FilePaths.GetInstallationPath(CurrentProfile.UUID, installation.DirectoryName_Full);
            if (!string.IsNullOrWhiteSpace(previousDirectoryPath) &&
                !string.IsNullOrWhiteSpace(newDirectoryPath) &&
                !string.Equals(previousDirectoryPath, newDirectoryPath, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(previousDirectoryPath) &&
                !Directory.Exists(newDirectoryPath))
            {
                Directory.Move(previousDirectoryPath, newDirectoryPath);
            }

            Save();
            OnPropertyChanged(nameof(CurrentInstallations));
            OnPropertyChanged(nameof(CurrentInstallation));
        }
        public void Installation_Delete(BLInstallation installation, bool deleteData = true)
        {
            if (CurrentProfile == null) return;
            if (CurrentInstallations == null) return;
            if (installation == null || installation.ReadOnly) return;
            if (deleteData)
            {
                try { installation.DeleteUserData(); }
                catch (Exception ex) { _ = MainDataModel.BackwardsCommunicationHost.exceptionmsg(ex); }
            }
            CurrentInstallations.Remove(installation);
            Save();
        }
        public void Installation_UpdateLP(BLInstallation installation)
        {
            if (installation == null) return;
            installation.LastPlayed = DateTime.Now;
            Save();
        }

        public BLInstallation SelectInstallationForVersion(MCVersion version)
        {
            if (CurrentProfile == null || CurrentInstallations == null || version == null)
            {
                return null;
            }

            BLInstallation installation = CurrentInstallations
                .FirstOrDefault(x => string.Equals(x.VersionUUID, version.UUID, StringComparison.OrdinalIgnoreCase) &&
                                     (IsManagedVersionInstallation(x) || IsOfficialInstallation(x)));

            if (installation == null)
            {
                installation = CreateManagedVersionInstallation(version);
                int insertIndex = CurrentInstallations.Any(IsOfficialInstallation) ? 1 : 0;
                CurrentInstallations.Insert(insertIndex, installation);
                Save();
                OnPropertyChanged(nameof(CurrentInstallations));
            }

            CurrentInstallationUUID = installation.InstallationUUID;
            OnPropertyChanged(nameof(CurrentInstallation));
            return installation;
        }

        #endregion

        #region Extensions

        public static void GetVersionParams(MCVersion version, out VersioningMode versioningMode, out string version_id)
        {
            version_id = Constants.LATEST_RELEASE_UUID;
            versioningMode = VersioningMode.LatestRelease;

            if (version != null)
            {
                //if (version.UUID == Constants.LATEST_BETA_UUID) versioningMode = VersioningMode.LatestBeta;
                if (version.UUID == Constants.LATEST_RELEASE_UUID) versioningMode = VersioningMode.LatestRelease;
                else if (version.UUID == Constants.LATEST_PREVIEW_UUID) versioningMode = VersioningMode.LatestPreview;
                else versioningMode = VersioningMode.None;

                version_id = version.UUID;
            }
        }

        #endregion
    }

}


