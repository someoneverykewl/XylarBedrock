using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media.Animation;
using XylarBedrock.Classes;
using XylarBedrock.Downloaders;
using XylarBedrock.Pages.General;
using XylarBedrock.Pages.Play.FAQ;
using XylarBedrock.Pages.Play.Home;
using XylarBedrock.UI.Components;

namespace XylarBedrock.Pages.Play
{
    public partial class GameTabs : Page
    {
        public PlayScreenPage playScreenPage = new PlayScreenPage();
        public FaqPage faqPage = new FaqPage();

        private Navigator Navigator { get; set; } = new Navigator();

        public GameTabs()
        {
            InitializeComponent();
        }



        public void ResetButtonManager(string buttonName)
        {
            this.Dispatcher.Invoke(() =>
            {
                ToggleButton[] toggleButtons = new ToggleButton[] {
                PlayButton,
                FaqButton
            };

                foreach (ToggleButton button in toggleButtons)
                {
                    button.IsChecked = button.Name == buttonName;
                }
            });

        }

        public void ButtonManager2(object sender, RoutedEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                var toggleButton = sender as ToggleButton;
                string name = toggleButton.Name;
                Task.Run(() => ButtonManager_Base(name));
            });
        }

        public void ButtonManager_Base(string senderName)
        {
            this.Dispatcher.Invoke(() =>
            {
                ResetButtonManager(senderName);

                if (senderName == PlayButton.Name) NavigateToPlayScreen();
                else if (senderName == FaqButton.Name) NavigateToFaqPage();
            });
        }

        public void NavigateToPlayScreen()
        {
            Navigator.UpdatePageIndex(0);
            Task.Run(() => Navigator.Navigate(MainPageFrame, playScreenPage));

        }

        public void NavigateToFaqPage()
        {
            Navigator.UpdatePageIndex(1);
            Task.Run(() => Navigator.Navigate(MainPageFrame, faqPage));
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            ResetButtonManager(null);
            ButtonManager_Base(PlayButton.Name);
        }

        private void CatEasterEggButton_Click(object sender, RoutedEventArgs e)
        {
            CatEasterEggWindow popup = new CatEasterEggWindow
            {
                Owner = Window.GetWindow(this)
            };
            popup.ShowDialog();
        }
    }
}

