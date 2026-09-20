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
