using Microsoft.Win32;

namespace HDREZKA.App.Services;

/// <summary>
/// Custom URL protocol (hdrezka://details?path=...) used by toast buttons.
/// Registered per-user (no admin), refreshed on every launch so a moved
/// portable folder keeps working. No COM activator needed (unpackaged app).
/// </summary>
public static class ProtocolService
{
    public const string Scheme = "hdrezka";

    public static void Register()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            using var scheme = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            scheme.SetValue("", "URL:HDREZKA");
            scheme.SetValue("URL Protocol", "");
            using var command = scheme.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{exe}\" \"%1\"");
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    public static Uri DetailsUri(string pagePath) =>
        new($"{Scheme}://details?path={Uri.EscapeDataString(pagePath)}");

    public static string? ParseDetailsPath(Uri uri)
    {
        try
        {
            if (!uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)) return null;
            if (!uri.Host.Equals("details", StringComparison.OrdinalIgnoreCase)) return null;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var path = query["path"];
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }
}
