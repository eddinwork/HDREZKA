using System.Diagnostics;
using System.Net;
using System.Text.Json;
using HDREZKA.Core.Api;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Services;

public sealed record MirrorProbeResult(string Url, bool Ok, long LatencyMs);

/// <summary>
/// Automatic mirror selection: candidates come from mirrors.json in the
/// repo (updatable without a release), with a hardcoded fallback.
///
/// Правила, которые важны для гео-блокировок (частые 403 у пользователей из UA):
///  * зеркало считается рабочим только если отдаёт НАСТОЯЩУЮ страницу сайта
///    (маркеры каталога), а не заглушку/капчу с кодом 200;
///  * зеркала региона пользователя проверяются первыми;
///  * если сохранённое зеркало перестало работать, приложение само
///    переключается на рабочее (кроме залогиненных — их смена разлогинит).
/// NOTE: cookies are domain-bound, so switching mirrors logs the user out.
/// </summary>
public static class MirrorService
{
    public const string RemoteMirrorsUrl = "https://raw.githubusercontent.com/eddinwork/HDREZKA/main/mirrors.json";
    public const string RedirectMirror = "https://rzk.link/";

    private static readonly MirrorEntry[] FallbackMirrors =
    [
        new("https://hdrzk.org/", MirrorCatalog.AllRegions, "default"),
        new("https://rezka.ag/", MirrorCatalog.AllRegions, "main"),
        new("https://rezka.fi/", MirrorCatalog.AllRegions, "main alt domain"),
        new("https://hdrezka.ag/", MirrorCatalog.AllRegions, "main alt"),
        new("https://hdrezka.me/", MirrorCatalog.AllRegions, "alt"),
        new("https://rezkify.com/", MirrorCatalog.AllRegions, "alt"),
        new("https://rezka-kz.tv/", "KZ", "kazakhstan"),
        new("https://rezka-ua.net/", "UA", "ukraine"),
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

    private static readonly SemaphoreSlim CatalogGate = new(1, 1);
    private static IReadOnlyList<MirrorEntry> _catalog = [];
    private static DateTime _catalogFetchedUtc = DateTime.MinValue;
    private static TimeSpan _catalogTtl = TimeSpan.FromHours(6);

    /// <summary>ISO 3166-1 alpha-2 страны пользователя (например, "UA"), либо null.</summary>
    public static string? UserRegion { get; } = DetectRegion();

    static MirrorService()
    {
        ProbeHttp.DefaultRequestHeaders.UserAgent.ParseAdd(BuildInfo.UserAgent);
    }

    private static string? DetectRegion()
    {
        try
        {
            var code = new Windows.Globalization.GeographicRegion().Code;
            if (!string.IsNullOrWhiteSpace(code) && !code.Equals("ZZ", StringComparison.OrdinalIgnoreCase))
            {
                return code.ToUpperInvariant();
            }
        }
        catch
        {
            // fall through to .NET region info
        }

        try
        {
            var iso = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
            if (!string.IsNullOrWhiteSpace(iso) && !iso.Equals("ZZ", StringComparison.OrdinalIgnoreCase))
            {
                return iso.ToUpperInvariant();
            }
        }
        catch
        {
        }

        return null;
    }

    // ---------- candidates ----------

    private static async Task<IReadOnlyList<MirrorEntry>> GetCatalogAsync(CancellationToken ct)
    {
        if (_catalog.Count > 0 && DateTime.UtcNow - _catalogFetchedUtc < _catalogTtl)
        {
            return _catalog;
        }

        await CatalogGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_catalog.Count > 0 && DateTime.UtcNow - _catalogFetchedUtc < _catalogTtl)
            {
                return _catalog;
            }

            var json = await FetchRemoteJsonAsync(ct).ConfigureAwait(false);
            var remote = MirrorCatalog.Parse(json);

            // Fallback-зеркала полезны и когда remote-список получен:
            // он может быть неполным или отставать от релиза.
            var merged = new List<MirrorEntry>(remote);
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in merged)
            {
                if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri)) hosts.Add(uri.Host);
            }

            foreach (var fallback in FallbackMirrors)
            {
                if (Uri.TryCreate(fallback.Url, UriKind.Absolute, out var uri) && hosts.Add(uri.Host))
                {
                    merged.Add(fallback);
                }
            }

            _catalog = merged;
            _catalogFetchedUtc = DateTime.UtcNow;
            // Ошибка сети: не долбим GitHub на каждый вызов, но и не кэшируем на 6 часов.
            _catalogTtl = remote.Count > 0 ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(10);
            return _catalog;
        }
        finally
        {
            CatalogGate.Release();
        }
    }

    private static async Task<string?> FetchRemoteJsonAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var resp = await ProbeHttp.GetAsync(RemoteMirrorsUrl, timeout.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<IReadOnlyList<string>> GetCandidatesAsync(CancellationToken ct = default)
    {
        var catalog = await GetCatalogAsync(ct).ConfigureAwait(false);

        // Регион пользователя → ALL → остальные → текущее кастомное зеркало.
        var list = MirrorCatalog.Order(catalog, SettingsService.Instance.Mirror, UserRegion).ToList();

        // Redirect-сервис может указывать на регионально доступное зеркало.
        var resolved = await ResolveRedirectAsync(ct).ConfigureAwait(false);
        if (resolved != null && !list.Any(u => MirrorCatalog.SameMirror(u, resolved)))
        {
            list.Add(resolved);
        }

        return list;
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
            if (ct.IsCancellationRequested) break;
            var sw = Stopwatch.StartNew();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
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

                // ВАЖНО: код 200 ещё не значит «зеркало работает». Заглушки,
                // капча и страница «Вход на HDrezka» приходят с 200, поэтому
                // проверяем реальную разметку сайта (старый чек по подстроке
                // "wrapper" пропускал такие страницы).
                var ok = Parsers.LooksLikeSite(body);
                return new MirrorProbeResult(url, ok, ok ? sw.ElapsedMilliseconds : 0);
            }
            catch
            {
                // try other variant / fail
            }
        }

        return new MirrorProbeResult(url, false, 0);
    }

    /// <summary>
    /// Проверяет зеркала параллельно. Порядок результата = порядок входных
    /// URL, а не «самый быстрый»: приоритет региона важнее миллисекунд.
    /// </summary>
    public static async Task<IReadOnlyList<MirrorProbeResult>> ProbeAllAsync(
        IEnumerable<string> urls, CancellationToken ct = default)
    {
        var tasks = urls.Select(u => ProbeAsync(u, ct)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
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
    /// Проверяет сохранённое зеркало и, если оно перестало отдавать реальную
    /// страницу, молча переключается на рабочее. Возвращает новое зеркало
    /// или null, если всё в порядке / переключение невозможно.
    /// Залогиненных не трогаем: смена домена = потеря сессии.
    /// </summary>
    /// <param name="contentPath">
    /// Конкретная страница, на которой пользователь получил ошибку (например,
    /// путь фильма). Бывает, что главная открывается, а глубокие страницы
    /// отдают 403 — тогда проверяем именно её.
    /// </param>
    public static async Task<string?> EnsureWorkingMirrorAsync(
        string? contentPath = null, CancellationToken ct = default)
    {
        try
        {
            var current = SettingsService.Instance.Mirror;
            if (string.IsNullOrWhiteSpace(current)) return null;

            var rootOk = (await ProbeAsync(current, ct).ConfigureAwait(false)).Ok;
            if (rootOk && (contentPath == null || await ProbeContentAsync(current, contentPath, ct).ConfigureAwait(false)))
            {
                return null;
            }

            LogMirror($"current {current} failed probe (root={rootOk} content={contentPath ?? "-"})");

            // Кандидаты проверяем в приоритетном порядке (регион → ALL → …)
            // и берём первый, который реально отдаёт нужную страницу.
            var candidates = await GetCandidatesAsync(ct).ConfigureAwait(false);
            var results = await ProbeAllAsync(candidates, ct).ConfigureAwait(false);

            string? best = null;
            foreach (var result in results.Where(r => r.Ok).Take(4))
            {
                if (contentPath != null && !await ProbeContentAsync(result.Url, contentPath, ct).ConfigureAwait(false))
                {
                    continue;
                }

                best = result.Url;
                break;
            }

            if (best == null || MirrorCatalog.SameMirror(best, current)) return null;

            if (RezkaService.Instance.IsLoggedIn)
            {
                LogMirror("switch skipped: user is logged in");
                return null;
            }

            ApplyMirror(best);
            LogMirror($"switched {current} -> {best}");
            return best;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            return null;
        }
    }

    /// <summary>
    /// Проверяет конкретную страницу на конкретном зеркале. Нужна, потому что
    /// 403 бывает избирательным: главная открывается, а страница фильма — нет.
    /// </summary>
    private static async Task<bool> ProbeContentAsync(string mirror, string path, CancellationToken ct)
    {
        try
        {
            var url = new Uri(new Uri(mirror), path.TrimStart('/')).ToString();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            req.Headers.Add("X-Hdrezka-Android-App", "1");
            req.Headers.Add("X-Hdrezka-Android-App-Version", "2.2.2");

            using var resp = await ProbeHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parsers.LooksLikeSite(body) && !body.Contains("id=\"check-form\"", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// First-launch autopick + regular health check on every launch.
    /// Silent, never throws. Logged-in sessions are only autopicked once
    /// (switching would log the user out).
    /// </summary>
    public static async Task EnsureOnStartupAsync()
    {
        try
        {
            var settings = SettingsService.Instance;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            if (!settings.MirrorAutoPicked)
            {
                if (RezkaService.Instance.IsLoggedIn)
                {
                    settings.MirrorAutoPicked = true;
                    settings.Save();
                    return;
                }

                var best = await PickBestAsync(cts.Token).ConfigureAwait(false);
                if (best == null)
                {
                    // Offline? Retry on next launch.
                    return;
                }

                settings.MirrorAutoPicked = true;
                if (!MirrorCatalog.SameMirror(best, settings.Mirror)) ApplyMirror(best);
                else settings.Save();
                return;
            }

            // Зеркало уже выбиралось, но гео-блокировки меняются: проверяем,
            // что сохранённое зеркало всё ещё отдаёт настоящую страницу.
            if (!RezkaService.Instance.IsLoggedIn)
            {
                await EnsureWorkingMirrorAsync(ct: cts.Token).ConfigureAwait(false);
            }
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
        if (MirrorCatalog.SameMirror(best, current))
        {
            SettingsService.Instance.LastMirrorCheckUtc = DateTime.UtcNow;
            SettingsService.Instance.Save();
            return current;
        }

        if (RezkaService.Instance.IsLoggedIn && xamlRoot != null && dispatcher != null)
        {
            var tcs = new TaskCompletionSource<bool>();
            var enqueued = dispatcher.TryEnqueue(async () =>
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
            if (!enqueued)
            {
                return null;
            }

            if (!await tcs.Task.ConfigureAwait(false)) return null;
        }

        ApplyMirror(best);
        return best;
    }

    private static void LogMirror(string message)
    {
        try { App.TryLog(new Exception("[Mirror] " + message)); } catch { }
    }
}
