using System.Text.Json;

namespace HDREZKA.App.Services;

public enum AppTheme { System, Light, Dark }

public enum AppLanguage { Ru, En, Uk }

public sealed record HomeSectionState(string Id, bool Visible);

public sealed class SettingsService
{
    public static SettingsService Instance { get; } = new();

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDREZKA");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    public string Mirror { get; set; } = "https://hdrzk.org/";

    /// <summary>First-launch mirror autopick already performed.</summary>
    public bool MirrorAutoPicked { get; set; }

    public DateTime LastMirrorCheckUtc { get; set; }
    public AppLanguage Language { get; set; } = AppLanguage.Ru;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string DefaultQuality { get; set; } = "1080p";
    public bool UseAndroidHeaders { get; set; } = true;
    public int Volume { get; set; } = 100;
    public double Speed { get; set; } = 1.0;

    /// <summary>Login (email or username) typed at the last successful sign-in.</summary>
    public string AccountLogin { get; set; } = "";
    /// <summary>Latest version the user was already notified about (notify once).</summary>
    public string LastNotifiedVersion { get; set; } = "";

    public DateTime LastUpdateCheckUtc { get; set; }

    /// <summary>Notify about new episodes of watched series.</summary>
    public bool SeriesUpdatesEnabled { get; set; } = true;

    public DateTime LastSeriesCheckUtc { get; set; }

    /// <summary>Continue-watching list order: oldest first when true.</summary>
    public bool ContinueOldestFirst { get; set; }

    /// <summary>Home continue cards start playback directly instead of opening details.</summary>
    public bool PlayFromHomeDirectly { get; set; } = true;

    /// <summary>
    /// Poster width in px (S=104, M=132, L=164, XL=196). Height is always 1.5x.
    /// </summary>
    public int PosterSize { get; set; } = 132;

    /// <summary>Home screen blocks order + visibility (ids: Hero, Continue, Films, Series, Cartoons, Popular, Watching, Soon).</summary>
    public List<HomeSectionState> HomeSections { get; set; } = DefaultHomeSections();

    public static List<HomeSectionState> DefaultHomeSections() =>
    [
        new("Hero", true),
        new("Continue", true),
        new("Tracked", true),
        new("Films", true),
        new("Series", true),
        new("Cartoons", true),
        new("Popular", true),
        new("Watching", true),
        new("Soon", true),
    ];

    public event Action? PosterSizeChanged;

    public void SetPosterSize(int width)
    {
        width = Math.Clamp(width, 80, 256);
        if (PosterSize == width) return;
        PosterSize = width;
        Save();
        PosterSizeChanged?.Invoke();
    }

    private static List<HomeSectionState> MergeHomeSections(List<HomeSectionState>? saved)
    {
        var defaults = DefaultHomeSections();
        var result = new List<HomeSectionState>();
        // Saved order wins for known ids (preserves user customization).
        foreach (var s in saved ?? [])
        {
            var def = defaults.FirstOrDefault(d => d.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase));
            if (def != null && result.All(r => !r.Id.Equals(def.Id, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new HomeSectionState(def.Id, s.Visible));
            }
        }

        // New ids land on their default positions.
        foreach (var (def, index) in defaults.Select((d, i) => (d, i)))
        {
            if (result.All(r => !r.Id.Equals(def.Id, StringComparison.OrdinalIgnoreCase)))
            {
                result.Insert(Math.Min(index, result.Count), def);
            }
        }

        return result;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<SettingsService>(json, JsonOpts);
            if (loaded == null) return;
            Mirror = loaded.Mirror;
            MirrorAutoPicked = loaded.MirrorAutoPicked;
            LastMirrorCheckUtc = loaded.LastMirrorCheckUtc;
            Language = loaded.Language;
            Theme = loaded.Theme;
            DefaultQuality = loaded.DefaultQuality;
            UseAndroidHeaders = loaded.UseAndroidHeaders;
            Volume = loaded.Volume;
            Speed = loaded.Speed is > 0 and <= 4 ? loaded.Speed : 1.0;
            PosterSize = loaded.PosterSize is >= 80 and <= 256 ? loaded.PosterSize : 132;
            AccountLogin = loaded.AccountLogin ?? "";
            LastNotifiedVersion = loaded.LastNotifiedVersion ?? "";
            LastUpdateCheckUtc = loaded.LastUpdateCheckUtc;
            SeriesUpdatesEnabled = loaded.SeriesUpdatesEnabled;
            LastSeriesCheckUtc = loaded.LastSeriesCheckUtc;
            ContinueOldestFirst = loaded.ContinueOldestFirst;
            PlayFromHomeDirectly = loaded.PlayFromHomeDirectly;
            HomeSections = MergeHomeSections(loaded.HomeSections);
        }
        catch
        {
            // corrupted settings — keep defaults
        }
    }
}
