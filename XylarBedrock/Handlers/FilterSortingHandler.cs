using XylarBedrock.Classes.Launcher;
using XylarBedrock.Classes;
using XylarBedrock.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using PropertyChanged;
using XylarBedrock.ViewModels;
using JemExtensions;

namespace XylarBedrock.Handlers
{
    public class FilterSortingHandler
    {
        public static SortDescription? GetInstallationSortDescriptor()
        {
            switch (Properties.LauncherSettings.Default.InstallationsSortMode)
            {
                case InstallationSort.LatestPlayed:
                    return new SortDescription(nameof(BLInstallation.LastPlayedT), ListSortDirection.Descending);
                case InstallationSort.Name:
                    return new SortDescription(nameof(BLInstallation.DisplayName), ListSortDirection.Ascending);
                case InstallationSort.None:
                    return null;
                default:
                    return new SortDescription(nameof(BLInstallation.LastPlayedT), ListSortDirection.Descending);
            }
        }
        public static string InstallationsSearchFilter { get; set; } = string.Empty;

        public static void Refresh(object itemSource)
        {
            var view = CollectionViewSource.GetDefaultView(itemSource) as CollectionView;
            if (view != null) view.Refresh();
        }
        public static void Sort_InstallationList(object itemSource)
        {
            var view = CollectionViewSource.GetDefaultView(itemSource) as CollectionView;
            if (view != null)
            {
                view.SortDescriptions.Clear();
                var result = GetInstallationSortDescriptor();
                if (result != null) view.SortDescriptions.Add(result.Value);
                view.Refresh();
            }
        }

        public static bool Filter_InstallationList(object obj)
        {
            BLInstallation v = obj as BLInstallation;
            if (v == null) return false;
            else if (!v.IsRelease) return false;
            else if (!v.IsInstalledVersion) return false;
            else if (!v.DisplayName_Full.Contains(InstallationsSearchFilter, StringComparison.OrdinalIgnoreCase)) return false;
            else return true;
        }
        public static bool Filter_VersionList(object obj)
        {
            MCVersion v = obj as MCVersion;
            if (v == null) return false;
            if (!v.IsRelease) return false;
            if (string.Equals(v.UUID, Constants.LATEST_RELEASE_UUID, StringComparison.OrdinalIgnoreCase)) return false;
            if (v.IsInstalled) return true;
            if (v.MatchesOfficialStoreRelease) return true;
            if (!string.IsNullOrWhiteSpace(v.DirectDownloadUrl)) return true;
            return v.DirectDownloadUrls != null && v.DirectDownloadUrls.Count > 0;

        }

        public static bool Filter_OfficalNewsFeed(object obj)
        {
            if (!(obj is News_OfficalItem)) return false;
            else
            {
                var item = (obj as News_OfficalItem);
                if (item.newsType != null && item.newsType.Contains("News page"))
                {
                    if (item.category == "Minecraft Dungeons" && NewsViewModel.Default.Offical_ShowDungeonsContent) return ContainsText(item);
                    else if (item.category == "Minecraft for Windows" && NewsViewModel.Default.Offical_ShowBedrockContent) return ContainsText(item);
                    else return false;
                }
                else return false;
            }

            bool ContainsText(News_OfficalItem _item)
            {
                string searchParam = NewsViewModel.Default.Offical_SearchBoxText;
                if (string.IsNullOrEmpty(searchParam) || _item.title.Contains(searchParam, StringComparison.OrdinalIgnoreCase)) return true;
                else return false;
            }
        }
    }
}


