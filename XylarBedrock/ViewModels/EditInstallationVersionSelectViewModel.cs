using PropertyChanged;
using XylarBedrock.Classes;

namespace XylarBedrock.ViewModels
{
    [AddINotifyPropertyChangedInterface]
    public class EditInstallationVersionSelectViewModel
    {
        public string FilterString { get; set; } = string.Empty;
        public MCVersion SelectedVersion { get; set; }
        public string SelectedVersionUUID { get; set; } = string.Empty;

        internal void Update()
        {
            FilterString ??= string.Empty;

            if (SelectedVersion != null)
            {
                SelectedVersionUUID = SelectedVersion.UUID ?? string.Empty;
            }
        }
    }
}
