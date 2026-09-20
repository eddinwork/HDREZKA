using HDREZKA.App.Services;
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
        BookmarksButton.Content = Loc.Get("Account.Bookmarks");
        HistoryButton.Content = Loc.Get("Nav.Continue");
        LogoutButton.Content = Loc.Get("Common.Logout");
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
}