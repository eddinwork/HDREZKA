namespace HDREZKA.App.Services;

/// <summary>
/// External site pages that we deliberately do NOT implement natively
/// (registration with captcha, payments): open them in the browser.
///
/// NOTE: the API mirror (default https://hdrzk.org/) often returns 403
/// in a plain browser (WAF / geo-block) while the app itself works
/// because it sends X-Hdrezka-Android-App headers. So for the browser
/// we use the redirect service https://rzk.link/ (same as the mac client),
/// which forwards to a working mirror for the user's geo.
/// If the user set a custom mirror in settings, respect it.
/// </summary>
public static class SiteLinks
{
    private const string DefaultMirror = "https://hdrzk.org/";
    public const string RedirectMirror = "https://rzk.link/";

    private static Uri BrowserBase()
    {
        try
        {
            var mirror = (SettingsService.Instance.Mirror ?? "").Trim();
            if (mirror.Length == 0) return new Uri(RedirectMirror);
            if (!mirror.EndsWith('/')) mirror += '/';

            // Default mirror -> redirect service (handles geo-block).
            // Custom mirror -> user knows what works for them, use it.
            if (mirror.Equals(DefaultMirror, StringComparison.OrdinalIgnoreCase))
                return new Uri(RedirectMirror);

            return new Uri(mirror);
        }
        catch
        {
            return new Uri(RedirectMirror);
        }
    }

    public static Uri? RegisterUri
    {
        get
        {
            try
            {
                // DLE standard registration route, works on any mirror.
                return new Uri(BrowserBase(), "index.php?do=register");
            }
            catch
            {
                return null;
            }
        }
    }

    public static Uri? PaymentsUri
    {
        get
        {
            try
            {
                return new Uri(BrowserBase(), "payments/");
            }
            catch
            {
                return null;
            }
        }
    }

    public static async Task OpenAsync(Uri? uri)
    {
        if (uri == null) return;
        try
        {
            // Also copy to clipboard: if the browser shows 403,
            // user can try it with VPN or another mirror.
            try
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(uri.ToString());
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            }
            catch
            {
            }

            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }
}
