using System;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Resources;

namespace XylarBedrock.Pages.General
{
    public partial class CatEasterEggWindow : Window
    {
        private SoundPlayer catPlayer;

        public CatEasterEggWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PlayCatSound();
        }

        private void PlayCatSound()
        {
            try
            {
                StreamResourceInfo soundResource = Application.GetResourceStream(
                    new Uri("pack://application:,,,/XylarBedrock;component/Resources/sounds/catsound.wav"));

                if (soundResource?.Stream == null)
                {
                    return;
                }

                catPlayer = new SoundPlayer(soundResource.Stream);
                catPlayer.Load();
                catPlayer.Play();
            }
            catch
            {
                // This is only an easter egg, so audio failures should stay silent.
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }
    }
}
