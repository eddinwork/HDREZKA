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
            await dialog.ShowAsync();
        }
    }
}
