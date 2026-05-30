using System.Windows;

namespace XylarBedrock.Pages.Toolbar
{
    public partial class Toolbar_SkinsButton : Toolbar_ButtonBase
    {
        public Toolbar_SkinsButton()
        {
            InitializeComponent();
        }

        private void SideBarButton_Click(object sender, RoutedEventArgs e)
        {
            ToolbarButtonBase_Click(this, e);
        }
    }
}
