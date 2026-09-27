using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace HDREZKA.App.Services;

public sealed record UpdateInfo(string Version, string Url, string Notes, string? SetupUrl = null);

public static class UpdateService
{
    public const string CurrentVersion = "1.4.3";

    private const string ReleasesApiUrl = "https://api.github.com/repos/eddinwork/HDREZKA/releases/latest";
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

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly HttpClient DownloadHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("HDREZKA-Windows-App");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        Http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        DownloadHttp.DefaultRequestHeaders.UserAgent.ParseAdd("HDREZKA-Windows-App");
    }

    public static async Task<UpdateInfo?> GetLatestAsync()
    {
        // 1) Official GitHub API (reliable JSON).
        try
        {
            using var resp = await Http.GetAsync(ReleasesApiUrl).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var info = ParseLatestFromApi(json);
                if (info != null) return info;
            }
        }
        catch
        {
            // fall through to HTML scraping
        }

        // 2) Fallback: scrape releases page HTML (kept for compat / API rate-limit).
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

    /// <summary>
    /// Parses https://api.github.com/.../releases/latest JSON.
    /// Pure function for unit tests.
    /// </summary>
    public static UpdateInfo? ParseLatestFromApi(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagEl)) return null;
            var tag = tagEl.GetString()?.Trim();
            if (string.IsNullOrEmpty(tag)) return null;
            var url = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
            var notes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(url)) url = $"https://github.com/eddinwork/HDREZKA/releases/tag/{tag}";
            return new UpdateInfo(tag, url, notes, FindSetupAsset(root));
        }
        catch
        {
            return null;
        }
    }

    private static string? FindSetupAsset(System.Text.Json.JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != System.Text.Json.JsonValueKind.Array)
                return null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                if (!name.StartsWith("HDREZKA-Setup-", StringComparison.OrdinalIgnoreCase) ||
                    !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var download = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
                if (!string.IsNullOrEmpty(download)) return download;
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>Setup-based install has an uninstaller next to the exe; portable does not.</summary>
    public static bool IsInstalledVersion
    {
        get
        {
            try
            {
                return File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));
            }
            catch
            {
                return false;
            }
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

            try { App.TryLog(new Exception("[Update] notifying " + latest.Version)); } catch { }
            await ShowUpdateDialogAsync(window, latest).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    /// <summary>
    /// In-app update: downloads Setup.exe with progress, then launches it
    /// and exits (installer can't overwrite a running exe). Must be called
    /// on the UI thread; portable builds fall back to the browser.
    /// </summary>
    public static async Task DownloadAndInstallAsync(
        Microsoft.UI.Xaml.Window window,
        Microsoft.UI.Xaml.XamlRoot xamlRoot,
        UpdateInfo latest)
    {
        var queue = window.DispatcherQueue;
        var cts = new CancellationTokenSource();

        var bar = new Microsoft.UI.Xaml.Controls.ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            MinWidth = 320,
        };
        var status = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = Loc.Get("Settings.Downloading", 0),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            MaxWidth = 420,
        };
        var panel = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 12 };
        panel.Children.Add(bar);
        panel.Children.Add(status);

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = Loc.Get("Settings.UpdateAvailable", latest.Version),
            Content = panel,
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };
        dialog.CloseButtonClick += (_, _) =>
        {
            try { cts.Cancel(); } catch { }
        };

        var showTask = dialog.ShowAsync().AsTask();
        try
        {
            var fileName = latest.SetupUrl!.Split('/').LastOrDefault() ?? "HDREZKA-Setup.exe";
            foreach (var bad in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(bad, '_');
            var dest = Path.Combine(Path.GetTempPath(), fileName);

            void Report(double percent)
            {
                queue.TryEnqueue(() =>
                {
                    try
                    {
                        bar.Value = Math.Clamp(percent, 0, 100);
                        status.Text = Loc.Get("Settings.Downloading", (int)percent);
                    }
                    catch
                    {
                    }
                });
            }

            await DownloadFileAsync(latest.SetupUrl!, dest, Report, cts.Token).ConfigureAwait(false);

            queue.TryEnqueue(() =>
            {
                try { status.Text = Loc.Get("Settings.Installing"); } catch { }
            });
            await Task.Delay(400, CancellationToken.None).ConfigureAwait(false);

            Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });

            queue.TryEnqueue(() =>
            {
                try { dialog.Hide(); } catch { }
                try { Microsoft.UI.Xaml.Application.Current.Exit(); } catch { }
            });
        }
        catch (OperationCanceledException)
        {
            try { dialog.Hide(); } catch { }
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            queue.TryEnqueue(() =>
            {
                try { status.Text = Loc.Get("Settings.DownloadFailed"); } catch { }
            });
            await showTask.ConfigureAwait(false);
            return;
        }

        try { await showTask.ConfigureAwait(false); } catch { }
    }

    private static async Task DownloadFileAsync(
        string url, string destPath, Action<double> progress, CancellationToken ct)
    {
        using var resp = await DownloadHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        using var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var file = File.Create(destPath);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await net.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            if (total is > 0) progress(100.0 * read / total.Value);
        }

        progress(100);
    }

    public static async Task ShowUpdateDialogAsync(Microsoft.UI.Xaml.Window window, UpdateInfo latest)
    {
        var tcs = new TaskCompletionSource();
        var enqueued = window.DispatcherQueue.TryEnqueue(async () =>
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

                if (await dialog.ShowAsync() == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                {
                    if (!string.IsNullOrEmpty(latest.SetupUrl) && IsInstalledVersion)
                    {
                        await DownloadAndInstallAsync(window, xamlRoot, latest).ConfigureAwait(false);
                    }
                    else if (!string.IsNullOrEmpty(latest.Url))
                    {
                        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(latest.Url));
                    }
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
        if (!enqueued)
        {
            return;
        }

        await tcs.Task.ConfigureAwait(false);
    }
}
