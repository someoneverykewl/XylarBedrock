using PropertyChanged;

namespace XylarBedrock.ViewModels
{
    /// <summary>
    /// Interaction logic for EditInstallationScreen.xaml
    /// </summary>
    /// 

    [AddINotifyPropertyChangedInterface]
    public class EditInstallationsPageViewModel
    {
        public string SelectedVersionUUID { get; set; } = string.Empty;
        public string SelectedVersionDisplayName { get; set; } = string.Empty;
        public string SelectedUUID { get; set; } = string.Empty;
        public string InstallationName { get; set; } = string.Empty;
        public string InstallationDirectory { get; set; } = string.Empty;
    }
}


