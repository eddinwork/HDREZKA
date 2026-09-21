using System.Text.Json;

namespace HDREZKA.App.Services;

public enum AppTheme { System, Light, Dark }

public enum AppLanguage { Ru, En, Uk }

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

    /// <summary>
    /// Poster width in px (S=104, M=132, L=164, XL=196). Height is always 1.5x.
    /// </summary>
    public int PosterSize { get; set; } = 132;

    public event Action? PosterSizeChanged;

    public void SetPosterSize(int width)
    {
        width = Math.Clamp(width, 80, 256);
        if (PosterSize == width) return;
        PosterSize = width;
        Save();
        PosterSizeChanged?.Invoke();
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
        }
        catch
        {
            // corrupted settings — keep defaults
        }
    }
}
