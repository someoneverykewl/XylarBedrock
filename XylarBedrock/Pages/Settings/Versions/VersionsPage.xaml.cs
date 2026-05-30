using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using XylarBedrock.Handlers;
using XylarBedrock.ViewModels;

namespace XylarBedrock.Pages.Settings.Versions
{
    public partial class VersionsPage : Page
    {
        private const double SmoothScrollDurationMs = 220;
        private ScrollViewer versionsScrollViewer;
        private DispatcherTimer smoothScrollTimer;
        private DateTime smoothScrollStartedAt;
        private double smoothScrollStartOffset;
        private double smoothScrollTargetOffset;
        private bool hasInitalized = false;
        public VersionsPage()
        {
            this.DataContext = MainDataModel.Default;
            InitializeComponent();
            smoothScrollTimer = new DispatcherTimer(DispatcherPriority.Render);
            smoothScrollTimer.Interval = TimeSpan.FromMilliseconds(16);
            smoothScrollTimer.Tick += SmoothScrollTimer_Tick;
        }

        private void Page_Initialized(object sender, EventArgs e)
        {
            
        }

        private void RefreshVersionsList(object sender, RoutedEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (VersionsList != null) Handlers.FilterSortingHandler.Refresh(VersionsList.ItemsSource);
            });
        }

        private void PageHost_Loaded(object sender, RoutedEventArgs e)
        {
            versionsScrollViewer ??= FindVisualChild<ScrollViewer>(VersionsList);

            if (!hasInitalized)
            {
                _ = MainDataModel.Default.LoadVersions(forceStoreCheck: true);
                foreach (var ver in MainDataModel.Default.Versions) ver.UpdateFolderSize();
                hasInitalized = true;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await MainDataModel.Default.LoadVersions(forceStoreCheck: true);
            foreach (var ver in MainDataModel.Default.Versions) ver.UpdateFolderSize();
        }

        private void CollectionViewSource_Filter(object sender, FilterEventArgs e)
        {
            e.Accepted = FilterSortingHandler.Filter_VersionList(e.Item);
        }

        private void VersionsRoot_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            versionsScrollViewer ??= FindVisualChild<ScrollViewer>(VersionsList);
            if (versionsScrollViewer == null || versionsScrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            e.Handled = true;

            double currentTarget = smoothScrollTimer.IsEnabled
                ? smoothScrollTargetOffset
                : versionsScrollViewer.VerticalOffset;

            smoothScrollStartOffset = versionsScrollViewer.VerticalOffset;
            smoothScrollTargetOffset = Math.Max(0, Math.Min(versionsScrollViewer.ScrollableHeight, currentTarget - (e.Delta * 0.72)));
            smoothScrollStartedAt = DateTime.UtcNow;
            smoothScrollTimer.Start();
        }

        private void SmoothScrollTimer_Tick(object sender, EventArgs e)
        {
            if (versionsScrollViewer == null)
            {
                smoothScrollTimer.Stop();
                return;
            }

            double progress = (DateTime.UtcNow - smoothScrollStartedAt).TotalMilliseconds / SmoothScrollDurationMs;
            if (progress >= 1)
            {
                versionsScrollViewer.ScrollToVerticalOffset(smoothScrollTargetOffset);
                smoothScrollTimer.Stop();
                return;
            }

            double easedProgress = 1 - Math.Pow(1 - progress, 3);
            double offset = smoothScrollStartOffset + ((smoothScrollTargetOffset - smoothScrollStartOffset) * easedProgress);
            versionsScrollViewer.ScrollToVerticalOffset(offset);
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                T nestedChild = FindVisualChild<T>(child);
                if (nestedChild != null)
                {
                    return nestedChild;
                }
            }

            return null;
        }

    }
}

