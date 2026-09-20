using System.Net;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Services;

public sealed record MirrorProbeResult(string Url, bool Ok, long LatencyMs);

/// <summary>
/// Automatic mirror selection: candidates come from mirrors.json in the
/// repo (updatable without a release), with a hardcoded fallback.
/// The fastest mirror that returns a real site page wins.
/// NOTE: cookies are domain-bound, so switching mirrors logs the user out.
/// </summary>
public static class MirrorService
{
    public const string RemoteMirrorsUrl = "https://raw.githubusercontent.com/eddinwork/HDREZKA/main/mirrors.json";
    public const string RedirectMirror = "https://rzk.link/";

    private static readonly string[] FallbackMirrors =
    [
        "https://hdrzk.org/",
        "https://rezka.ag/",
        "https://hdrezka.ag/",
        "https://hdrezka.me/",
        "https://rezkify.com/",
        "https://rezka-kz.tv/",
        "https://rezka-ua.net/",
    ];

    private static readonly HttpClient ProbeHttp = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    static MirrorService()
    {
        ProbeHttp.DefaultRequestHeaders.UserAgent.ParseAdd("HDREZKA/1.0.0 (Windows; WinUI)");
    }

    private static bool SameMirror(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    // ---------- candidates ----------

    public static async Task<IReadOnlyList<string>> GetCandidatesAsync(CancellationToken ct = default)
    {
        var list = new List<string>();

        // Current setting first: never lose a working custom mirror.
        var current = SettingsService.Instance.Mirror?.Trim() ?? "";
        if (current.Length > 0)
        {
            if (!current.EndsWith('/')) current += '/';
            list.Add(current);
        }

        var remote = await FetchRemoteListAsync(ct).ConfigureAwait(false);
        list.AddRange(remote.Count > 0 ? remote : FallbackMirrors);

        // Redirect service may point at a region-specific working mirror.
        var resolved = await ResolveRedirectAsync(ct).ConfigureAwait(false);
        if (resolved != null) list.Add(resolved);

        // Dedupe by host, keep order (current first).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var u in list)
        {
            try
            {
                var host = new Uri(u).Host;
                if (seen.Add(host)) result.Add(u.EndsWith('/') ? u : u + '/');
            }
            catch
            {
                // skip malformed
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<string>> FetchRemoteListAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await ProbeHttp.GetAsync(RemoteMirrorsUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return [];
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("mirrors", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];
            var urls = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var u = item.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(u)) urls.Add(u.EndsWith('/') ? u : u + '/');
                }
                else if (item.ValueKind == JsonValueKind.Object &&
                         item.TryGetProperty("url", out var urlEl) &&
                         urlEl.GetString() is { } u && u.Trim().Length > 0)
                {
                    u = u.Trim();
                    urls.Add(u.EndsWith('/') ? u : u + '/');
                }
            }

            return urls;
        }
        catch
        {
            return [];
        }
    }

    private static async Task<string?> ResolveRedirectAsync(CancellationToken ct)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var resp = await client.GetAsync(RedirectMirror, ct).ConfigureAwait(false);
            var location = resp.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(location)) return null;
            var resolved = location.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? location
                : new Uri(new Uri(RedirectMirror), location).ToString();
            if (!resolved.EndsWith('/')) resolved += '/';
            return resolved;
        }
        catch
        {
            return null;
        }
    }

    // ---------- probing ----------

    private static async Task<MirrorProbeResult> ProbeAsync(string url, CancellationToken ct)
    {
        // Try with Android headers first (app default), then without —
        // mirrors accept different variants (same as the 403-flip retry).
        foreach (var android in new[] { true, false })
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.ParseAdd("text/html");
                if (android)
                {
                    req.Headers.Add("X-Hdrezka-Android-App", "1");
                    req.Headers.Add("X-Hdrezka-Android-App-Version", "2.2.2");
                }

                using var resp = await ProbeHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.StatusCode == HttpStatusCode.Forbidden) continue; // try other variant
                if (!resp.IsSuccessStatusCode) return new MirrorProbeResult(url, false, 0);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                sw.Stop();
                // Real site layout marker (see Parsers.CheckDocument: #wrapper).
                if (body.Contains("wrapper", StringComparison.OrdinalIgnoreCase))
                    return new MirrorProbeResult(url, true, sw.ElapsedMilliseconds);
                return new MirrorProbeResult(url, false, 0);
            }
            catch
            {
                // try other variant / fail
            }
        }

        return new MirrorProbeResult(url, false, 0);
    }

    public static async Task<IReadOnlyList<MirrorProbeResult>> ProbeAllAsync(IEnumerable<string> urls, CancellationToken ct = default)
    {
        var tasks = urls.Select(u => ProbeAsync(u, ct)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.OrderBy(r => !r.Ok).ThenBy(r => r.LatencyMs).ToList();
    }

    public static async Task<string?> PickBestAsync(CancellationToken ct = default)
    {
        var candidates = await GetCandidatesAsync(ct).ConfigureAwait(false);
        var results = await ProbeAllAsync(candidates, ct).ConfigureAwait(false);
        return results.FirstOrDefault(r => r.Ok)?.Url;
    }

    private static void ApplyMirror(string url)
    {
        var settings = SettingsService.Instance;
        settings.Mirror = url;
        settings.LastMirrorCheckUtc = DateTime.UtcNow;
        settings.Save();
        RezkaService.Instance.ApplySettings();
    }

    // ---------- flows ----------

    /// <summary>
    /// First-launch autopick. Silent, never throws, never touches a
    /// logged-in session (mirror switch would log the user out).
    /// </summary>
    public static async Task AutoPickOnStartupAsync()
    {
        try
        {
            var settings = SettingsService.Instance;
            if (settings.MirrorAutoPicked) return;
            if (RezkaService.Instance.IsLoggedIn)
            {
                settings.MirrorAutoPicked = true;
                settings.Save();
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var best = await PickBestAsync(cts.Token).ConfigureAwait(false);
            if (best == null)
            {
                // Offline? Retry on next launch.
                return;
            }

            settings.MirrorAutoPicked = true;
            if (!SameMirror(best, settings.Mirror)) ApplyMirror(best);
            else settings.Save();
        }
        catch
        {
            // never break startup
        }
    }

    /// <summary>
    /// Manual / on-403 flow with a logout warning for logged-in users.
    /// Returns the applied mirror, the unchanged current mirror, or null
    /// when nothing works or the user cancelled.
    /// </summary>
    public static async Task<string?> PickAndApplyAsync(XamlRoot? xamlRoot, DispatcherQueue? dispatcher, CancellationToken ct = default)
    {
        var candidates = await GetCandidatesAsync(ct).ConfigureAwait(false);
        var results = await ProbeAllAsync(candidates, ct).ConfigureAwait(false);
        var best = results.FirstOrDefault(r => r.Ok)?.Url;
        if (best == null) return null;

        var current = SettingsService.Instance.Mirror;
        if (SameMirror(best, current))
        {
            SettingsService.Instance.LastMirrorCheckUtc = DateTime.UtcNow;
            SettingsService.Instance.Save();
            return current;
        }

        if (RezkaService.Instance.IsLoggedIn && xamlRoot != null && dispatcher != null)
        {
            var tcs = new TaskCompletionSource<bool>();
            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    var dialog = new ContentDialog
                    {
                        Title = Loc.Get("Mirror.PickTitle"),
                        Content = Loc.Get("Mirror.PickLogoutWarn", best),
                        PrimaryButtonText = Loc.Get("Mirror.Apply"),
                        CloseButtonText = Loc.Get("Common.Cancel"),
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = xamlRoot,
                    };
                    tcs.TrySetResult(await dialog.ShowAsync() == ContentDialogResult.Primary);
                }
                catch
                {
                    tcs.TrySetResult(false);
                }
            });
            if (!await tcs.Task.ConfigureAwait(false)) return null;
        }

        ApplyMirror(best);
        return best;
    }
}
