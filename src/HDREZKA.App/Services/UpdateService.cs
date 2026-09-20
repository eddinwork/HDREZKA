using System.Net;
using System.Text.RegularExpressions;

namespace HDREZKA.App.Services;

public sealed record UpdateInfo(string Version, string Url, string Notes);

public static class UpdateService
{
    public const string CurrentVersion = "1.1.2";

    private const string ReleasesPageUrl = "https://github.com/eddinwork/HDREZKA/releases";

    /// <summary>
    /// Latest release tag from the releases page HTML (newest first).
    /// Pure function for unit tests.
    /// </summary>
    public static string? ParseLatestTag(string html)
    {
        var m = Regex.Match(html, @"/eddinwork/HDREZKA/releases/tag/([^""\s<>]+)");
        if (!m.Success) return null;
        var tag = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
        return tag.Length > 0 ? tag : null;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("HDREZKA-Windows-App");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static async Task<UpdateInfo?> GetLatestAsync()
    {
        try
        {
            using var resp = await Http.GetAsync(ReleasesPageUrl).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var html = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var tag = ParseLatestTag(html);
            if (tag == null) return null;
            return new UpdateInfo(tag, $"https://github.com/eddinwork/HDREZKA/releases/tag/{tag}", "");
        }
        catch
        {
            return null;
        }
    }

    public static bool IsNewer(string latest, string current)
    {
        static Version? Parse(string v)
        {
            v = v.Trim().TrimStart('v', 'V');
            return Version.TryParse(v, out var r) ? r : null;
        }

        var l = Parse(latest);
        var c = Parse(current);
        return l != null && c != null && l > c;
    }

    /// <summary>
    /// Startup check: runs on every launch, but notifies at most once per version.
    /// Never throws and never blocks startup.
    /// </summary>
    public static async Task CheckAndNotifyAsync(Microsoft.UI.Xaml.Window window)
    {
        try
        {
            var latest = await GetLatestAsync().ConfigureAwait(false);
            var settings = SettingsService.Instance;
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            settings.Save();
            if (latest == null) return;
            if (!IsNewer(latest.Version, CurrentVersion)) return;
            if (settings.LastNotifiedVersion == latest.Version) return;

            settings.LastNotifiedVersion = latest.Version;
            settings.Save();

            await ShowUpdateDialogAsync(window, latest).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    public static async Task ShowUpdateDialogAsync(Microsoft.UI.Xaml.Window window, UpdateInfo latest)
    {
        var tcs = new TaskCompletionSource();
        window.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var xamlRoot = window.Content?.XamlRoot;
                if (xamlRoot == null) return;

                var notes = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(latest.Notes) ? latest.Version : latest.Notes,
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    MaxWidth = 420,
                };
                var scroll = new Microsoft.UI.Xaml.Controls.ScrollViewer
                {
                    Content = notes,
                    MaxHeight = 240,
                };
                var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    Title = Loc.Get("Settings.UpdateAvailable", latest.Version),
                    Content = scroll,
                    PrimaryButtonText = Loc.Get("Settings.Download"),
                    CloseButtonText = Loc.Get("Settings.Later"),
                    DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
                    XamlRoot = xamlRoot,
                };

                if (await dialog.ShowAsync() == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary &&
                    !string.IsNullOrEmpty(latest.Url))
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(latest.Url));
                }
            }
            catch (Exception ex)
            {
                App.TryLog(ex);
            }
            finally
            {
                tcs.TrySetResult();
            }
        });
        await tcs.Task.ConfigureAwait(false);
    }
}
