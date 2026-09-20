using Microsoft.UI.Xaml;

namespace HDREZKA.App.Services;

public static class ThemeHelper
{
    public static FrameworkElement? Root { get; private set; }

    public static void Apply(AppTheme theme)
    {
        if (Root == null) return;
        Root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public static void SetRoot(FrameworkElement root)
    {
        Root = root;
        Apply(SettingsService.Instance.Theme);
    }
}
