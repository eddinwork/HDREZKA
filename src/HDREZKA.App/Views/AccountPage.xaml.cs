using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class AccountPage : Page
{
    public AccountPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateAccountState();
        RenderOffline();
        RezkaService.Instance.AuthStateChanged += OnAuthStateChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        RezkaService.Instance.AuthStateChanged -= OnAuthStateChanged;
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyLocalization();
            UpdateAccountState();
        });
    }

    private void ApplyLocalization()
    {
        LoginPromptText.Text = Loc.Get("Account.LoginTitle");
        LoginButton.Content = Loc.Get("Common.Login");
        RegisterLink.Content = Loc.Get("Account.Register");
        PremiumLink.Content = Loc.Get("Account.Premium");
        PremiumLinkLoggedIn.Content = Loc.Get("Account.Premium");
        SuggestLinkLoggedOut.Content = Loc.Get("Search.Suggest");
        SuggestButton.Content = Loc.Get("Search.Suggest");
        BookmarksButton.Content = Loc.Get("Account.Bookmarks");
        HistoryButton.Content = Loc.Get("Nav.Continue");
        LogoutButton.Content = Loc.Get("Common.Logout");
        OfflineHeader.Text = Loc.Get("Account.Offline");
        OfflineEmptyText.Text = Loc.Get("Account.OfflineEmpty");
        OfflineFolderButton.Content = Loc.Get("Downloads.OpenFolder");
        DisclaimerText.Text = Loc.Get("Settings.Disclaimer");
    }

    private void OnAuthStateChanged()
    {
        DispatcherQueue.TryEnqueue(UpdateAccountState);
    }

    private void UpdateAccountState()
    {
        var isLoggedIn = RezkaService.Instance.IsLoggedIn;
        LoggedInPanel.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        LoggedOutPanel.Visibility = isLoggedIn ? Visibility.Collapsed : Visibility.Visible;
        ActionsPanel.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;

        if (isLoggedIn)
        {
            UpdateUserInfo();
        }
    }

    private void UpdateUserInfo()
    {
        var login = SettingsService.Instance.AccountLogin.Trim();
        if (login.Length == 0)
        {
            // Legacy session (cookies restored, login typed before this feature).
            login = Loc.Get("Account.User");
        }

        NicknameText.Text = login;
        AvatarInitial.Text = login.Substring(0, 1).ToUpperInvariant();

        AvatarBrush.ImageSource = null;
        if (login.Contains('@'))
        {
            EmailText.Text = login;
            EmailText.Visibility = Visibility.Visible;
            AvatarBrush.ImageSource = new BitmapImage(new Uri(GravatarUrl(login)));
        }
        else
        {
            EmailText.Text = "";
            EmailText.Visibility = Visibility.Collapsed;
        }
    }

    private void AvatarBrush_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // No gravatar: keep the initial letter visible.
        AvatarBrush.ImageSource = null;
    }

    private static string GravatarUrl(string email)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return $"https://www.gravatar.com/avatar/{Convert.ToHexString(hash).ToLowerInvariant()}?s=160&d=404";
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LoginDialog
        {
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void RegisterLink_Click(object sender, RoutedEventArgs e)
    {
        await SiteLinks.OpenAsync(SiteLinks.RegisterUri);
    }

    private async void PremiumLink_Click(object sender, RoutedEventArgs e)
    {
        await SiteLinks.OpenAsync(SiteLinks.PaymentsUri);
    }

    private async void SuggestLink_Click(object sender, RoutedEventArgs e)
    {
        await SupportRequest.ShowAsync(Content.XamlRoot, "");
    }

    private void BookmarksButton_Click(object sender, RoutedEventArgs e)
    {
        Nav.Go<BookmarksPage>();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        Nav.Go<ContinueWatchingPage>();
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        await RezkaService.Instance.LogoutAsync();
        UpdateAccountState();
    }

    private sealed record OfflineRow(string Title, string Details, string Path);

    private void RenderOffline()
    {
        // Offline section exists only for unlocked users.
        if (!DonorUnlock.IsUnlocked)
        {
            OfflinePanel.Visibility = Visibility.Collapsed;
            return;
        }

        OfflinePanel.Visibility = Visibility.Visible;
        var rows = new List<OfflineRow>();
        try
        {
            var folder = DownloadService.DownloadFolder;
            if (Directory.Exists(folder))
            {
                foreach (var file in new DirectoryInfo(folder)
                             .GetFiles("*.mp4", SearchOption.AllDirectories)
                             .OrderByDescending(f => f.LastWriteTimeUtc)
                             .Take(100))
                {
                    var mb = file.Length / 1048576.0;
                    rows.Add(new OfflineRow(
                        Path.GetFileNameWithoutExtension(file.Name),
                        $"{mb:0} MB · {file.LastWriteTime:dd.MM.yyyy}",
                        file.FullName));
                }
            }
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }

        OfflineList.ItemsSource = rows;
        OfflineList.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OfflineEmptyText.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OfflineList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not OfflineRow row) return;
        try
        {
            var id = "local:" + row.Path;
            var details = new MovieDetailed
            {
                Id = id,
                Name = row.Title,
                Favs = "1",
            };
            var voice = new MovieVoiceActing(
                Loc.Get("Common.Quality"), "0", "0", "", "", "", false, true, null);
            var playerWindow = new PlayerWindow();
            playerWindow.ShowPlayer(new PlayerLaunch(details, voice, null, null, null, row.Path));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    private void OfflineDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not OfflineRow row) return;
        try
        {
            if (File.Exists(row.Path)) File.Delete(row.Path);
            PositionService.Instance.Remove("local:" + row.Path);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }

        RenderOffline();
    }

    private async void OfflineFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(DownloadService.DownloadFolder);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }
}