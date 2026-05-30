using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using XylarBedrock.Core;

namespace XylarBedrock.Pages.General
{
    public partial class UpdatePromptWindow : Window
    {
        private readonly GithubReleaseInfo releaseInfo;
        private bool isClosingAnimated;

        public UpdatePromptWindow(GithubReleaseInfo releaseInfo)
        {
            this.releaseInfo = releaseInfo ?? new GithubReleaseInfo();
            InitializeComponent();
            PopulateReleaseInfo();
        }

        private void PopulateReleaseInfo()
        {
            string tag = string.IsNullOrWhiteSpace(releaseInfo.tag_name)
                ? "next version"
                : releaseInfo.tag_name.Trim();

            VersionText.Text = $"Current: v{App.Version}  ->  Available: {tag}";
            BodyText.Text = BuildBodyText(releaseInfo.body);
        }

        private static string BuildBodyText(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return "A newer XylarBedrock build is available with fixes, polish, and launcher improvements.";
            }

            string plainText = Regex.Replace(body, @"[#>*_`\[\]\(\)]", string.Empty);
            plainText = Regex.Replace(plainText, @"\s+", " ").Trim();

            if (plainText.Length > 160)
            {
                plainText = plainText.Substring(0, 157).TrimEnd() + "...";
            }

            return plainText;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Storyboard storyboard = new Storyboard();

            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation scaleXAnimation = new DoubleAnimation
            {
                From = 0.94,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(330),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 }
            };

            DoubleAnimation scaleYAnimation = new DoubleAnimation
            {
                From = 0.94,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(330),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 }
            };

            Storyboard.SetTarget(opacityAnimation, DialogCard);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
            Storyboard.SetTarget(scaleXAnimation, DialogScale);
            Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("ScaleX"));
            Storyboard.SetTarget(scaleYAnimation, DialogScale);
            Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("ScaleY"));

            storyboard.Children.Add(opacityAnimation);
            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Begin();
        }

        private async void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            await CloseWithAnimationAsync(false);
        }

        private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
        {
            await CloseWithAnimationAsync(true);
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                await CloseWithAnimationAsync(false);
            }
        }

        private async Task CloseWithAnimationAsync(bool dialogResult)
        {
            if (isClosingAnimated)
            {
                return;
            }

            isClosingAnimated = true;

            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
            Storyboard storyboard = new Storyboard();

            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            DoubleAnimation scaleXAnimation = new DoubleAnimation
            {
                To = 0.965,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            DoubleAnimation scaleYAnimation = new DoubleAnimation
            {
                To = 0.965,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            Storyboard.SetTarget(opacityAnimation, DialogCard);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
            Storyboard.SetTarget(scaleXAnimation, DialogScale);
            Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("ScaleX"));
            Storyboard.SetTarget(scaleYAnimation, DialogScale);
            Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("ScaleY"));

            storyboard.Children.Add(opacityAnimation);
            storyboard.Children.Add(scaleXAnimation);
            storyboard.Children.Add(scaleYAnimation);
            storyboard.Completed += (_, _) => completion.TrySetResult(true);
            storyboard.Begin();

            await completion.Task;
            DialogResult = dialogResult;
            Close();
        }
    }
}
