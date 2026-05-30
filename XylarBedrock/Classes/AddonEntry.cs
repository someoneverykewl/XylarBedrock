using XylarBedrock.Localization.Language;

namespace XylarBedrock.Classes
{
    public class AddonEntry
    {
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SourceLabel { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string InstallUri { get; set; } = string.Empty;
        public string PageUri { get; set; } = string.Empty;
        public string LocalPackagePath { get; set; } = string.Empty;
        public string DownloadsText { get; set; } = string.Empty;
        public string UpdatedText { get; set; } = string.Empty;
        public string FileSizeText { get; set; } = string.Empty;
        public string GameVersionText { get; set; } = string.Empty;
        public bool IsCustom { get; set; }

        public bool IsRemote => !string.IsNullOrWhiteSpace(InstallUri);
        public bool IsLocal => !string.IsNullOrWhiteSpace(LocalPackagePath);
        public string AuthorDisplayText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Author))
                {
                    return LanguageManager.GetResource("AddonsPage_UnknownAuthor") as string ?? "Unknown author";
                }

                string format = LanguageManager.GetResource("AddonsPage_ByAuthorFormat") as string ?? "By {0}";
                return string.Format(format, Author);
            }
        }

        public string InstallButtonText
        {
            get
            {
                string key = IsCustom ? "AddonsPage_InstallButton" : "AddonsPage_DownloadButton";
                return LanguageManager.GetResource(key) as string ?? (IsCustom ? "Install" : "Download");
            }
        }
        public string SearchText => $"{Title} {Author} {Description} {SourceLabel}";
    }
}
