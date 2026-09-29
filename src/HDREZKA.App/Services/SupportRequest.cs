using HDREZKA.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Services;

/// <summary>
/// "Request a title" flow: builds the support message, copies it to the
/// clipboard and opens the support page (via redirect mirror, since
/// support.html does not exist on every mirror). The user picks the topic
/// and pastes manually — captchas can't be automated.
/// </summary>
public static class SupportRequest
{
    public static async Task ShowAsync(XamlRoot? xamlRoot, string prefill)
    {
        if (xamlRoot == null) return;

        var input = new TextBox
        {
            Text = prefill,
            PlaceholderText = Loc.Get("Search.SuggestPlaceholder"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            MinWidth = 320,
        };
        var hint = new TextBlock
        {
            Text = Loc.Get("Search.SuggestHint"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380,
            FontSize = 12,
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(input);
        panel.Children.Add(hint);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Search.SuggestTitle"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Search.SuggestOpen"),
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var titles = input.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .ToList();
        if (titles.Count == 0 && prefill.Trim().Length > 0) titles.Add(prefill.Trim());
        if (titles.Count == 0) return;
        var body = Loc.Get("Search.SuggestTemplate") + "\n" + string.Join("\n", titles.Select(t => "- " + t));

        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(body);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }

        // NOTE: own Launcher call, NOT SiteLinks.OpenAsync — that one
        // overwrites the clipboard with the URL, killing our message.
        try
        {
            var uri = SiteLinks.SupportUri;
            if (uri != null) await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }
}
