using System.Reflection;

namespace HDREZKA.Core.Api;

/// <summary>
/// Version/product facts shared by HTTP user agents and the UI.
/// The value comes from Directory.Build.props (InformationalVersion) —
/// single source of truth for the whole solution.
/// </summary>
public static class BuildInfo
{
    public static string Version { get; } = ResolveVersion();

    public static string UserAgent { get; } = $"HDREZKA/{Version} (Windows; WinUI)";

    private static string ResolveVersion()
    {
        try
        {
            var assembly = typeof(BuildInfo).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // "1.7.0+<git sha>" -> "1.7.0"
                var plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }
}
