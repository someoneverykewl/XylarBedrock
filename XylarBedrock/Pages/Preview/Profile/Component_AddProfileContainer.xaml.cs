using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XylarBedrock.Classes;
using XylarBedrock.ViewModels;

namespace XylarBedrock.Pages.Preview.Profile
{
    public partial class Component_AddProfileContainer : UserControl
    {
        public bool isEditMode = false;

        public event EventHandler GoBack;
        public event EventHandler Confirm;

        public EditProfileContainerViewModel ViewModel { get; set; } = new EditProfileContainerViewModel();

        public Component_AddProfileContainer()
        {
            InitializeComponent();
            DataContext = ViewModel;
        }

        public Component_AddProfileContainer(BLProfile profileToEdit)
        {
            InitializeComponent();
            DataContext = ViewModel;
            isEditMode = true;

            ViewModel.ProfileName = profileToEdit.Name;
            ViewModel.ProfileUUID = profileToEdit.UUID;
            ViewModel.ProfileImage = profileToEdit.ImagePath;
            ViewModel.ProfileDirectory = profileToEdit.ProfilePath;

            CreateProfileSubtitle.Text = this.FindResource("NewProfile_EditProfileSubTitle") as string;
            CreateProfileButtonText.Text = this.FindResource("NewProfile_EditProfileButton") as string;

        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            GoBack?.Invoke(this, EventArgs.Empty);
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ConfirmProfile();
            EvaluateDirectory();
        }

        private void CreateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            EvaluateDirectory();
            ConfirmProfile();
        }
        public void ConfirmProfile()
        {
            EnsureGeneratedProfileName();
            if (ViewModel.ProfileName.Length >= 1)
            {
                if (isEditMode) UpdateProfile();
                else CreateProfile();
            }
        }

        private void EnsureGeneratedProfileName()
        {
            string typedName = ProfileNameTextbox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typedName) ||
                typedName.Equals("Default Profile", StringComparison.OrdinalIgnoreCase))
            {
                typedName = GenerateRandomProfileName();
            }

            ViewModel.ProfileName = typedName;
            ProfileNameTextbox.Text = typedName;

            if (string.IsNullOrWhiteSpace(ViewModel.ProfileDirectory) ||
                ViewModel.ProfileDirectory.Equals("Default Profile", StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.ProfileDirectory = typedName;
            }
        }

        private static string GenerateRandomProfileName()
        {
            string[] adjectives =
            {
                "Pixel", "Blocky", "Lucky", "Swift", "Golden", "Emerald", "Cosmic", "Tiny",
                "Brave", "Silent", "Frost", "Sunny", "Shadow", "Crystal", "Turbo", "Magic",
                "Royal", "Clever", "Wild", "Happy", "Neon", "Rapid", "Mystic", "Epic",
                "Bouncy", "Copper", "Obsidian", "Cherry", "Mossy", "Lunar", "Solar", "Cloudy"
            };

            string[] nouns =
            {
                "Fox", "Cat", "Bee", "Panda", "Axolotl", "Wolf", "Ender", "Creeper",
                "Miner", "Builder", "Rider", "Knight", "Ninja", "Dragon", "Llama", "Warden",
                "Golem", "Steve", "Alex", "Piglin", "Strider", "Dolphin", "Turtle", "Falcon",
                "Raccoon", "Pumpkin", "Totem", "Pickaxe", "Lantern", "Comet", "Rocket", "Sprout"
            };

            // 32 * 32 * 1024 = 1,048,576 possible names.
            string adjective = adjectives[Random.Shared.Next(adjectives.Length)];
            string noun = nouns[Random.Shared.Next(nouns.Length)];
            int number = Random.Shared.Next(1024);

            return $"{adjective}{noun}{number:0000}";
        }

        public void UpdateProfile()
        {
            if (MainDataModel.Default.Config.Profile_Edit(ViewModel.ProfileName, ViewModel.ProfileUUID, ViewModel.ProfileDirectory, ViewModel.ProfileImage))
            {
                Confirm?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                CreateProfileText.SetResourceReference(TextBlock.TextProperty, "NewProfile_CreateProfileText_Error");
                CreateProfileText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            }
        }
        public void CreateProfile()
        {
            if (MainDataModel.Default.Config.Profile_Add(ViewModel.ProfileName, ViewModel.ProfileUUID, ViewModel.ProfileDirectory, ViewModel.ProfileImage))
            {
                Confirm?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                CreateProfileText.SetResourceReference(TextBlock.TextProperty, "NewProfile_CreateProfileText_Error");
                CreateProfileText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            }
        }

        private void ProfileNameTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            EvaluateDirectory();
        }

        private void EvaluateDirectory()
        {
            if (string.IsNullOrEmpty(ViewModel.ProfileDirectory) || ViewModel.ProfileName.StartsWith(ViewModel.ProfileDirectory)) 
                ViewModel.ProfileDirectory = ViewModel.ProfileName;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.OpenFileDialog ofd = new System.Windows.Forms.OpenFileDialog()
            {
                Filter = "PNG Files (*.png)|*.png"
            };

            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                ViewModel.ProfileImage = ofd.FileName;
        }
    }
}

