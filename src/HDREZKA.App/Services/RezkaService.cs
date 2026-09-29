using HDREZKA.Core.Api;

namespace HDREZKA.App.Services;

public sealed class RezkaService
{
    public static RezkaService Instance { get; } = new();

    public RezkaClient Client { get; private set; }

    public event Action? AuthStateChanged;

    private RezkaService()
    {
        Client = new RezkaClient(new RezkaClientOptions
        {
            Mirror = SettingsService.Instance.Mirror,
            UseAndroidHeaders = SettingsService.Instance.UseAndroidHeaders,
        });
        // Persist auto-flipped value so next launch uses the working variant.
        Client.HeadersFlipped += () =>
        {
            try
            {
                SettingsService.Instance.UseAndroidHeaders = Client.Options.UseAndroidHeaders;
                SettingsService.Instance.Save();
            }
            catch
            {
            }
        };
    }

    public bool IsLoggedIn => Client.IsLoggedIn;

    public void RaiseAuthChanged() => AuthStateChanged?.Invoke();

    private static string CookiePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDREZKA", "cookies.json");

    public void RestoreSession()
    {
        Client.LoadCookies(CookiePath);
        RaiseAuthChanged();
    }

    public async Task SignInAsync(string login, string password, CancellationToken ct = default)
    {
        await Client.SignInAsync(login, password, ct).ConfigureAwait(false);
        Client.SaveCookies(CookiePath);
        SettingsService.Instance.AccountLogin = login.Trim();
        SettingsService.Instance.Save();
        RaiseAuthChanged();
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await Client.LogoutAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // logout even on network failure
        }

        Client.ClearSession();
        RezkaClient.DeleteCookies(CookiePath);
        SettingsService.Instance.AccountLogin = "";
        SettingsService.Instance.Save();
        RaiseAuthChanged();
    }

    public void ApplySettings()
    {
        var settings = SettingsService.Instance;
        Client.Options.UseAndroidHeaders = settings.UseAndroidHeaders;
        Client.SetMirror(settings.Mirror);
        RaiseAuthChanged();
    }

    private static bool SameMirror(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Person pages 404 on some mirrors while movies work fine, so probe
    /// other known mirrors IN PARALLEL (sequential 30s timeouts felt like
    /// an endless load) with throwaway clients (public pages, session
    /// untouched). Throws the original error if nothing works.
    /// </summary>
    public async Task<PersonDetailed> GetPersonAsync(string pagePath, CancellationToken ct = default)
    {
        RezkaException? firstError = null;
        try
        {
            return await Client.GetPersonAsync(pagePath, ct).ConfigureAwait(false);
        }
        catch (RezkaException ex)
        {
            // Person pages are public: any site-level failure (incl. 403s
            // surfacing as LoginRequired on stale sessions) is worth
            // retrying on other mirrors before giving up.
            firstError = ex;
            LogPersonAttempt(Client.Options.Mirror, "primary " + ex.Kind);
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(25));
            var candidates = (await MirrorService.GetCandidatesAsync(cts.Token).ConfigureAwait(false))
                .Where(m => !SameMirror(m, Client.Options.Mirror))
                .Take(5)
                .ToList();

            var pending = candidates
                .Select(m => (Mirror: m, Task: TryPersonMirrorAsync(m, pagePath, cts.Token)))
                .ToList();
            while (pending.Count > 0)
            {
                var done = await Task.WhenAny(pending.Select(p => p.Task)).ConfigureAwait(false);
                var entry = pending.First(p => p.Task == done);
                pending.Remove(entry);
                try
                {
                    var person = await done.ConfigureAwait(false);
                    try { cts.Cancel(); } catch { }
                    return person;
                }
                catch (Exception ex)
                {
                    var kind = ex is RezkaException rex ? rex.Kind.ToString() : ex.GetType().Name;
                    LogPersonAttempt(entry.Mirror, "fail " + kind);
                }
            }
        }
        catch
        {
        }

        throw firstError ?? new RezkaException(RezkaError.Network, "person failed");
    }

    private async Task<PersonDetailed> TryPersonMirrorAsync(string mirror, string pagePath, CancellationToken ct)
    {
        using var probe = new RezkaClient(new RezkaClientOptions
        {
            Mirror = mirror,
            UseAndroidHeaders = Client.Options.UseAndroidHeaders,
        });
        var person = await probe.GetPersonAsync(pagePath, ct).ConfigureAwait(false);
        LogPersonAttempt(mirror, "ok");
        return person;
    }

    private static void LogPersonAttempt(string mirror, string outcome)
    {
        try { App.TryLog(new Exception($"[Person] mirror={mirror} {outcome}")); } catch { }
    }

    public string ErrorText(RezkaException ex) => ex.Kind switch
    {
        RezkaError.LoginRequired => Loc.Get("Error.LoginRequired"),
        RezkaError.MirrorBanned => Loc.Get("Error.MirrorBanned"),
        RezkaError.AccessDenied => Loc.Get("Error.AccessDenied"),
        RezkaError.Parse => Loc.Get("Error.Parse"),
        _ => Loc.Get("Error.Network"),
    };
}

public static class Loc
{
    public static string Get(string key) => LocalizationService.Instance.Get(key);
    public static string Get(string key, params object[] args) => LocalizationService.Instance.Get(key, args);
}
