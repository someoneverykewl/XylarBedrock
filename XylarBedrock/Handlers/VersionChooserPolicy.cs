using System;
using XylarBedrock.Classes;

namespace XylarBedrock.Handlers
{
    internal static class VersionChooserPolicy
    {
        public static bool IsVisibleInChooser(MCVersion version, string selectedVersionUuid = "")
        {
            if (version == null)
            {
                return false;
            }

            if (string.Equals(version.UUID, selectedVersionUuid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (version.IsCustom)
            {
                return false;
            }

            if (string.Equals(version.UUID, Constants.LATEST_RELEASE_UUID, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return version.IsRelease &&
                   (version.IsInstalled ||
                    version.MatchesOfficialStoreRelease ||
                    !string.IsNullOrWhiteSpace(version.DirectDownloadUrl) ||
                    (version.DirectDownloadUrls != null && version.DirectDownloadUrls.Count > 0));
        }
    }
}
