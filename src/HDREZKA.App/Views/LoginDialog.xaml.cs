using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Views;

public sealed partial class LoginDialog : ContentDialog
{
    public LoginDialog()
    {
        InitializeComponent();
        ApplyLocalization();
        IsPrimaryButtonEnabled = false;
        PrimaryButtonClick += async (_, _) => await SignInAsync();
    }

    private void ApplyLocalization()
    {
        Title = Loc.Get("Account.LoginTitle");
        PrimaryButtonText = Loc.Get("Common.Login");
        CloseButtonText = Loc.Get("Common.Cancel");
        LoginBox.Header = Loc.Get("Common.Email");
        PasswordBox.Header = Loc.Get("Common.Password");
        RegisterLink.Content = Loc.Get("Account.Register");
        PremiumLink.Content = Loc.Get("Account.Premium");
    }

    private void Fields_Changed(object sender, RoutedEventArgs e)
    {
        IsPrimaryButtonEnabled =
            LoginBox.Text.Trim().Length > 0 &&
            PasswordBox.Password.Length > 0;
    }

    private async Task SignInAsync()
    {
        ErrorText.Text = "";
        IsPrimaryButtonEnabled = false;
        Hide();

        try
        {
            await RezkaService.Instance.SignInAsync(LoginBox.Text.Trim(), PasswordBox.Password);
        }
        catch (RezkaException ex)
        {
            var dialog = new ContentDialog
            {
                Title = Loc.Get("Common.Error"),
                Content = RezkaService.Instance.ErrorText(ex),
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            };
            if (ex.Kind is RezkaError.AccessDenied or RezkaError.MirrorBanned)
            {
                dialog.SecondaryButtonText = Loc.Get("Settings.AutoMirror");
                if (await dialog.ShowAsync() == ContentDialogResult.Secondary)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    await MirrorService.PickAndApplyAsync(XamlRoot, DispatcherQueue, cts.Token);
                    await ShowAsync();
                }
            }
            else
            {
                await dialog.ShowAsync();
            }
        }
    }

    private async void RegisterLink_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        await SiteLinks.OpenAsync(SiteLinks.RegisterUri);
    }

    private async void PremiumLink_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        await SiteLinks.OpenAsync(SiteLinks.PaymentsUri);
    }
}
