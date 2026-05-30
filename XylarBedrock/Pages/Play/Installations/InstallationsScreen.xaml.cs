using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XylarBedrock.Handlers;
using XylarBedrock.ViewModels;

namespace XylarBedrock.Pages.Play.Installations
{
    public partial class InstallationsScreen : Page
    {

        public InstallationsScreen()
        {
            InitializeComponent();
            this.DataContext = MainDataModel.Default;
        }
        public void RefreshInstallations()
        {
            this.Dispatcher.Invoke(() =>
            {
                if (InstallationsList?.ItemsSource != null)
                {
                    CollectionViewSource.GetDefaultView(InstallationsList.ItemsSource)?.Refresh();
                }
            });
        }
        private void PageHost_Loaded(object sender, RoutedEventArgs e)
        {
            this.RefreshInstallations();
        }

        private void InstallationsList_SourceUpdated(object sender, DataTransferEventArgs e)
        {
            this.RefreshInstallations();
        }

        private void CollectionViewSource_Filter(object sender, FilterEventArgs e)
        {
            e.Accepted = Handlers.FilterSortingHandler.Filter_InstallationList(e.Item);
        }
    }
}

