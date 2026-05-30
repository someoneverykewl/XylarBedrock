using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using XylarBedrock.ViewModels;

namespace XylarBedrock.Pages.General
{
    public partial class SocialWindow : Window, INotifyPropertyChanged
    {
        private const int CurrentSocialSchemaVersion = 2;

        private readonly SocialState state;
        private string activeTab = "friends";

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<SocialFriend> Friends { get; } = new ObservableCollection<SocialFriend>();
        public ObservableCollection<SocialRequest> OutgoingRequests { get; } = new ObservableCollection<SocialRequest>();
        public ObservableCollection<string> BlockedUsers { get; } = new ObservableCollection<string>();
        public ObservableCollection<SocialGroup> Groups { get; } = new ObservableCollection<SocialGroup>();

        public string CurrentInitial => GetInitial(MainDataModel.Default.Config?.CurrentProfile?.Name ?? "X");

        public SocialWindow()
        {
            InitializeComponent();
            state = SocialState.Load();
            DataContext = this;
            RefreshAll();
            SetActiveTab("friends");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ProfileIdText.Text = $"Your profile: {MainDataModel.Default.Config?.CurrentProfile?.Name ?? "Xylar Player"}";
        }

        private void RefreshAll()
        {
            RefreshFriends();
            RefreshCollection(OutgoingRequests, state.OutgoingRequests.Select(name => new SocialRequest { Name = name, Direction = "Outgoing" }));
            RefreshCollection(BlockedUsers, state.BlockedUsers.OrderBy(name => name));
            RefreshCollection(Groups, state.Groups.OrderBy(group => group.Name));

            FriendCountText.Text = $"{Friends.Count} friend{(Friends.Count == 1 ? string.Empty : "s")}";
            RequestCountText.Text = $"{OutgoingRequests.Count} pending";
            BlockedCountText.Text = $"{BlockedUsers.Count} blocked";
            GroupCountText.Text = $"{Groups.Count} group{(Groups.Count == 1 ? string.Empty : "s")}";

            EmptyFriendsPanel.Visibility = Friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyRequestsPanel.Visibility = OutgoingRequests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyBlockedPanel.Visibility = BlockedUsers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyGroupsPanel.Visibility = Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentInitial)));
        }

        private void RefreshFriends()
        {
            string query = SearchTextBox?.Text?.Trim() ?? string.Empty;
            IEnumerable<SocialFriend> visibleFriends = state.Friends
                .Where(friend => !state.BlockedUsers.Contains(friend.Name, StringComparer.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(query))
            {
                visibleFriends = visibleFriends.Where(friend =>
                    friend.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    friend.StatusText.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            RefreshCollection(Friends, visibleFriends.OrderByDescending(friend => friend.IsOnline).ThenBy(friend => friend.Name));
            SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(SearchTextBox?.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void RefreshCollection<T>(ObservableCollection<T> collection, IEnumerable<T> values)
        {
            collection.Clear();
            foreach (T value in values)
            {
                collection.Add(value);
            }
        }

        private void SetActiveTab(string tab)
        {
            activeTab = tab;
            FriendsPanel.Visibility = tab == "friends" ? Visibility.Visible : Visibility.Collapsed;
            RequestsPanel.Visibility = tab == "requests" ? Visibility.Visible : Visibility.Collapsed;
            GroupsPanel.Visibility = tab == "groups" ? Visibility.Visible : Visibility.Collapsed;

            ApplyTabVisual(FriendsTabButton, tab == "friends");
            ApplyTabVisual(RequestsTabButton, tab == "requests");
            ApplyTabVisual(GroupsTabButton, tab == "groups");

            SearchTextBox.IsEnabled = tab == "friends";
            SearchPlaceholder.Text = tab == "friends" ? "Search friends..." : "Search is available in Friends.";
        }

        private static void ApplyTabVisual(System.Windows.Controls.Button button, bool active)
        {
            button.Background = active
                ? new SolidColorBrush(Color.FromRgb(238, 238, 238))
                : Brushes.Transparent;
            button.Foreground = active
                ? Brushes.Black
                : new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));
        }

        private void AddFriendButton_Click(object sender, RoutedEventArgs e)
        {
            string friendName = FriendNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(friendName))
            {
                SetStatus("Type a real profile name first.");
                return;
            }

            if (IsCurrentProfile(friendName))
            {
                SetStatus("You cannot add yourself.");
                return;
            }

            if (state.BlockedUsers.Contains(friendName, StringComparer.OrdinalIgnoreCase))
            {
                SetStatus($"{friendName} is blocked. Unblock them before sending a request.");
                return;
            }

            if (state.Friends.Any(friend => friend.Name.Equals(friendName, StringComparison.OrdinalIgnoreCase)))
            {
                SetStatus($"{friendName} is already in your friends list.");
                return;
            }

            if (!state.OutgoingRequests.Contains(friendName, StringComparer.OrdinalIgnoreCase))
            {
                state.OutgoingRequests.Add(friendName);
                state.Save();
                RefreshAll();
            }

            FriendNameTextBox.Text = string.Empty;
            SetStatus($"Friend request saved for {friendName}. It will stay pending until a real service accepts it.");
        }

        private void CancelRequestButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string userName)
            {
                return;
            }

            state.OutgoingRequests.RemoveAll(name => name.Equals(userName, StringComparison.OrdinalIgnoreCase));
            state.Save();
            RefreshAll();
            SetStatus($"Request cancelled for {userName}.");
        }

        private void CreateGroupButton_Click(object sender, RoutedEventArgs e)
        {
            string groupName = GroupNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                SetStatus("Give the group a name first.");
                return;
            }

            if (!state.Groups.Any(group => group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
            {
                state.Groups.Add(new SocialGroup
                {
                    Name = groupName,
                    IsPublic = PublicGroupCheckBox.IsChecked == true,
                    MemberCount = 1
                });
                state.Save();
                RefreshAll();
            }

            GroupNameTextBox.Text = string.Empty;
            PublicGroupCheckBox.IsChecked = false;
            SetStatus($"Group created: {groupName}.");
        }

        private void BlockPersonButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string userName || string.IsNullOrWhiteSpace(userName))
            {
                return;
            }

            AddBlockedUser(userName);
            SetStatus($"{userName} blocked.");
        }

        private void UnblockButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string userName)
            {
                return;
            }

            state.BlockedUsers.RemoveAll(name => name.Equals(userName, StringComparison.OrdinalIgnoreCase));
            state.Save();
            RefreshAll();
            SetStatus($"{userName} unblocked.");
        }

        private void AddBlockedUser(string userName)
        {
            if (!state.BlockedUsers.Contains(userName, StringComparer.OrdinalIgnoreCase))
            {
                state.BlockedUsers.Add(userName);
            }

            state.Friends.RemoveAll(friend => friend.Name.Equals(userName, StringComparison.OrdinalIgnoreCase));
            state.OutgoingRequests.RemoveAll(name => name.Equals(userName, StringComparison.OrdinalIgnoreCase));
            state.Save();
            RefreshAll();
        }

        private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshFriends();
        }

        private void FriendsTabButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTab("friends");
        }

        private void RequestsTabButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTab("requests");
        }

        private void GroupsTabButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTab("groups");
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

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        private bool IsCurrentProfile(string value)
        {
            string currentName = MainDataModel.Default.Config?.CurrentProfile?.Name ?? string.Empty;
            return value.Equals(currentName, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetInitial(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "?" : value.Trim().Substring(0, 1).ToUpperInvariant();
        }

        public class SocialFriend
        {
            public string Name { get; set; }
            public bool IsOnline { get; set; }
            public string AccentColor { get; set; } = "#FF9EE7A5";

            [JsonIgnore]
            public string Initial => GetInitial(Name);

            [JsonIgnore]
            public string StatusText => IsOnline ? "Online" : "Offline";

            [JsonIgnore]
            public Brush StatusBrush => IsOnline ? new SolidColorBrush(Color.FromRgb(67, 232, 93)) : new SolidColorBrush(Color.FromRgb(110, 110, 118));

            [JsonIgnore]
            public Brush AccentBrush
            {
                get
                {
                    try
                    {
                        return (Brush)new BrushConverter().ConvertFromString(AccentColor);
                    }
                    catch
                    {
                        return new SolidColorBrush(Color.FromRgb(158, 231, 165));
                    }
                }
            }
        }

        public class SocialRequest
        {
            public string Name { get; set; }
            public string Direction { get; set; }

            [JsonIgnore]
            public string DirectionLabel => Direction == "Incoming" ? "Incoming request" : "Outgoing request";
        }

        public class SocialGroup
        {
            public string Name { get; set; }
            public bool IsPublic { get; set; }
            public int MemberCount { get; set; } = 1;

            [JsonIgnore]
            public string VisibilityLabel => IsPublic ? "Public group" : "Private group";

            [JsonIgnore]
            public string MemberCountLabel => $"{MemberCount} member{(MemberCount == 1 ? string.Empty : "s")}";
        }

        public class SocialState
        {
            public int SchemaVersion { get; set; } = CurrentSocialSchemaVersion;
            public List<SocialFriend> Friends { get; set; } = new List<SocialFriend>();
            public List<string> OutgoingRequests { get; set; } = new List<string>();
            public List<string> BlockedUsers { get; set; } = new List<string>();
            public List<SocialGroup> Groups { get; set; } = new List<SocialGroup>();

            public static SocialState Load()
            {
                try
                {
                    string path = GetStatePath();
                    if (!File.Exists(path))
                    {
                        return CreateEmptyAndSave();
                    }

                    string json = File.ReadAllText(path);
                    SocialState loadedState = JsonConvert.DeserializeObject<SocialState>(json) ?? new SocialState();

                    if (loadedState.SchemaVersion < CurrentSocialSchemaVersion)
                    {
                        loadedState = new SocialState();
                        loadedState.Save();
                    }

                    loadedState.Friends ??= new List<SocialFriend>();
                    loadedState.OutgoingRequests ??= new List<string>();
                    loadedState.BlockedUsers ??= new List<string>();
                    loadedState.Groups ??= new List<SocialGroup>();
                    return loadedState;
                }
                catch
                {
                    return new SocialState();
                }
            }

            public void Save()
            {
                SchemaVersion = CurrentSocialSchemaVersion;
                string path = GetStatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }

            private static SocialState CreateEmptyAndSave()
            {
                SocialState socialState = new SocialState();
                socialState.Save();
                return socialState;
            }

            private static string GetStatePath()
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "XylarBedrock", "social.json");
            }
        }
    }
}
