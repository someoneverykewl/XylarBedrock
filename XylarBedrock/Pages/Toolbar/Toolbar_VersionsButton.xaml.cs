using System.Windows;

namespace XylarBedrock.Pages.Toolbar
{
    public partial class Toolbar_VersionsButton : Toolbar_ButtonBase
    {
        public Toolbar_VersionsButton()
        {
            InitializeComponent();
        }

        private void SideBarButton_Click(object sender, RoutedEventArgs e)
        {
            ToolbarButtonBase_Click(this, e);
        }
    }
}
