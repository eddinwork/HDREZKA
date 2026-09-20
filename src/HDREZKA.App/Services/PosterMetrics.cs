namespace HDREZKA.App.Services;

/// <summary>
/// Single source of truth for poster sizes across all screens.
/// Everything derives from SettingsService.PosterSize (poster width in px),
/// so changing it in Settings resizes all posters everywhere identically.
/// Aspect ratio is always 2:3.
/// </summary>
public static class PosterMetrics
{
    public static int Base => SettingsService.Instance.PosterSize;

    /// <summary>Grid cards (catalog, search, bookmarks, collections, home) and continue cards.</summary>
    public static int CardWidth => Base;
    public static int CardHeight => (int)Math.Round(Base * 1.5);

    /// <summary>Hero poster on the home page.</summary>
    public static int HeroWidth => (int)Math.Round(Base * 1.5);
    public static int HeroHeight => (int)Math.Round(HeroWidth * 1.5);
    public static int HeroCardHeight => HeroHeight + 80;

    /// <summary>Large poster on the details page.</summary>
    public static int DetailsWidth => Base * 2;
    public static int DetailsHeight => Base * 3;

    /// <summary>Collection folder thumbnails (landscape-ish).</summary>
    public static int FolderHeight => (int)Math.Round(Base * 0.75);
}
