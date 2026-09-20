using System.Text.Json;

namespace HDREZKA.App.Services;

public sealed record WatchPosition(
    string MovieId,
    string TranslatorId,
    string? SeasonId,
    string? EpisodeId,
    double PositionSeconds,
    double DurationSeconds,
    DateTime UpdatedAt,
    string? SubtitleLang = null,
    string? Quality = null,
    string? Title = null,
    string? PosterUrl = null,
    string? PagePath = null);

public sealed class PositionService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDREZKA");

    private static readonly string FilePath = Path.Combine(Dir, "positions.json");

    public static PositionService Instance { get; } = new();

    private Dictionary<string, WatchPosition> _positions = new();

    private PositionService()
    {
        Load();
    }

    private static string Key(string movieId, string translatorId, string? season, string? episode) =>
        $"{movieId}|{translatorId}|{season ?? "0"}|{episode ?? "0"}";

    public WatchPosition? Get(string movieId, string translatorId, string? season, string? episode)
    {
        _positions.TryGetValue(Key(movieId, translatorId, season, episode), out var pos);
        return pos;
    }

    public IReadOnlyList<WatchPosition> GetAll() =>
        _positions.Values
            .Where(p => p is { DurationSeconds: > 60, PositionSeconds: > 30 } &&
                        p.PositionSeconds < p.DurationSeconds - 30)
            .OrderByDescending(p => p.UpdatedAt)
            .ToList();

    public void Save(WatchPosition position)
    {
        _positions[Key(position.MovieId, position.TranslatorId, position.SeasonId, position.EpisodeId)] = position;
        Persist();
    }

    public void Remove(string movieId)
    {
        var keys = _positions
            .Where(kv => kv.Value.MovieId == movieId || kv.Value.PagePath == movieId)
            .Select(kv => kv.Key)
            .ToList();
        if (keys.Count == 0) return;
        foreach (var key in keys)
        {
            _positions.Remove(key);
        }

        Persist();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            _positions = JsonSerializer.Deserialize<Dictionary<string, WatchPosition>>(json) ?? new();
        }
        catch
        {
            _positions = new();
        }
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_positions));
        }
        catch
        {
            // best effort
        }
    }
}
