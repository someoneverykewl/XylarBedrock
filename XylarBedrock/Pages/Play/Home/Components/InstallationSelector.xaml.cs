using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XylarBedrock.Handlers;
using XylarBedrock.ViewModels;

namespace XylarBedrock.Pages.Play.Home.Components
{
    /// <summary>
    /// Interaction logic for InstallationSelector.xaml
    /// </summary>
    public partial class InstallationSelector : ComboBox
    {

        public InstallationSelector()
        {
            InitializeComponent();
            DataContext = MainDataModel.Default;
        }
        private void ComboBox_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void ComboBox_SourceUpdated(object sender, DataTransferEventArgs e)
        {
        }

        private void CollectionViewSource_Filter(object sender, FilterEventArgs e)
        {
            e.Accepted = true;
        }
    }
}

