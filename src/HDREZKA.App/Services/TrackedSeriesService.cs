using System.Text.Json;
using HDREZKA.Core.Api;
using Microsoft.Windows.AppNotifications.Builder;

namespace HDREZKA.App.Services;

public sealed record SeriesUpdate(
    string PagePath,
    string Title,
    string? Poster,
    string Text);

internal sealed record TrackedTitle(
    string PagePath,
    string? Title,
    string? Poster,
    Dictionary<string, int>? Snapshot,
    DateTime UpdatedAt);

public sealed record StoredUpdate(
    string PagePath,
    string Title,
    string? Poster,
    string Text,
    DateTime Date);

/// <summary>
/// Tracks watched series and notifies about new seasons/episodes.
/// Baseline snapshot is taken silently on first sight — only growth notifies.
/// </summary>
public static class TrackedSeriesService
{
    private const int MaxTracked = 30;
    private const int MaxCheckedPerRun = 25;

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDREZKA");

    private static readonly string FilePath = Path.Combine(Dir, "tracked.json");
    private static readonly string UpdatesFilePath = Path.Combine(Dir, "updates.json");
    private const int MaxStoredUpdates = 30;

    public static event Action? UpdatesChanged;

    private static List<StoredUpdate> _updates = LoadUpdates();

    private static List<StoredUpdate> LoadUpdates()
    {
        try
        {
            if (!File.Exists(UpdatesFilePath)) return new();
            var json = File.ReadAllText(UpdatesFilePath);
            return JsonSerializer.Deserialize<List<StoredUpdate>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void PersistUpdates()
    {
        try
        {
            if (_updates.Count > MaxStoredUpdates)
            {
                _updates = _updates
                    .OrderByDescending(u => u.Date)
                    .Take(MaxStoredUpdates)
                    .ToList();
            }

            Directory.CreateDirectory(Dir);
            File.WriteAllText(UpdatesFilePath, JsonSerializer.Serialize(_updates));
        }
        catch
        {
            // best effort
        }
    }

    public static IReadOnlyList<StoredUpdate> GetUpdates() =>
        _updates.OrderByDescending(u => u.Date).ToList();

    public static int UnreadCount => _updates.Count;

    public static void AddUpdates(IEnumerable<SeriesUpdate> updates)
    {
        var added = false;
        foreach (var u in updates)
        {
            // Same title+text twice: refresh date instead of duplicating.
            _updates.RemoveAll(x => x.PagePath == u.PagePath && x.Text == u.Text);
            _updates.Add(new StoredUpdate(u.PagePath, u.Title, u.Poster, u.Text, DateTime.Now));
            added = true;
        }

        if (added)
        {
            PersistUpdates();
            try { UpdatesChanged?.Invoke(); } catch { }
        }
    }

    public static void RemoveUpdate(StoredUpdate update)
    {
        var removed = _updates.RemoveAll(x => x.PagePath == update.PagePath && x.Text == update.Text && x.Date == update.Date);
        if (removed > 0)
        {
            PersistUpdates();
            try { UpdatesChanged?.Invoke(); } catch { }
        }
    }

    public static void ClearUpdates()
    {
        if (_updates.Count == 0) return;
        _updates.Clear();
        PersistUpdates();
        try { UpdatesChanged?.Invoke(); } catch { }
    }

    private static Dictionary<string, TrackedTitle> _tracked = Load();

    private static Dictionary<string, TrackedTitle> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, TrackedTitle>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void Persist()
    {
        try
        {
            // LRU cap.
            if (_tracked.Count > MaxTracked)
            {
                _tracked = _tracked
                    .OrderByDescending(kv => kv.Value.UpdatedAt)
                    .Take(MaxTracked)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_tracked));
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Remember a series the user watches. Series only (season+episode).</summary>
    public static void Touch(string pagePath, string? title, string? poster)
    {
        if (string.IsNullOrWhiteSpace(pagePath)) return;
        try
        {
            if (_tracked.TryGetValue(pagePath, out var existing))
            {
                _tracked[pagePath] = existing with
                {
                    Title = title ?? existing.Title,
                    Poster = poster ?? existing.Poster,
                    UpdatedAt = DateTime.Now,
                };
            }
            else
            {
                _tracked[pagePath] = new TrackedTitle(pagePath, title, poster, null, DateTime.Now);
            }

            Persist();
        }
        catch
        {
        }
    }

    public static bool IsTracked(string pagePath) =>
        !string.IsNullOrWhiteSpace(pagePath) && _tracked.ContainsKey(pagePath);

    /// <returns>New tracked state.</returns>
    public static bool ToggleTracked(string pagePath, string? title, string? poster)
    {
        if (string.IsNullOrWhiteSpace(pagePath)) return false;
        try
        {
            if (_tracked.Remove(pagePath))
            {
                Persist();
                return false;
            }
        }
        catch
        {
            return false;
        }

        Touch(pagePath, title, poster);
        return true;
    }

    /// <summary>Seed tracking from existing watch history (silent baseline).</summary>
    public static void SeedFromHistory()
    {
        try
        {
            foreach (var p in PositionService.Instance.GetAll())
            {
                if (p.SeasonId == null || p.EpisodeId == null) continue;
                var id = p.PagePath ?? p.MovieId;
                if (!_tracked.ContainsKey(id))
                {
                    _tracked[id] = new TrackedTitle(id, p.Title, p.PosterUrl, null, p.UpdatedAt);
                }
            }

            Persist();
        }
        catch
        {
        }
    }

    private static Dictionary<string, int> BuildSnapshot(IReadOnlyList<MovieSeason>? seasons)
    {
        var snap = new Dictionary<string, int>();
        if (seasons == null) return snap;
        foreach (var s in seasons)
        {
            snap[s.SeasonId] = s.Episodes.Count;
        }

        return snap;
    }

    private static string? DiffText(Dictionary<string, int> oldSnap, Dictionary<string, int> newSnap, IReadOnlyList<MovieSeason>? seasons)
    {
        var parts = new List<string>();
        var names = (seasons ?? []).ToDictionary(s => s.SeasonId, s => s.Name);
        foreach (var (seasonId, newCount) in newSnap)
        {
            var name = names.TryGetValue(seasonId, out var n) ? n : seasonId;
            if (!oldSnap.TryGetValue(seasonId, out var oldCount))
            {
                parts.Add($"{Loc.Get("Common.Season")} {name} ({newCount})");
            }
            else if (newCount > oldCount)
            {
                parts.Add($"{Loc.Get("Common.Season")} {name}: +{newCount - oldCount}");
            }
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    public static async Task<IReadOnlyList<SeriesUpdate>> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var updates = new List<SeriesUpdate>();
        var client = RezkaService.Instance.Client;

        var titles = _tracked.Values
            .OrderByDescending(t => t.UpdatedAt)
            .Take(MaxCheckedPerRun)
            .ToList();

        foreach (var title in titles)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var details = await client.GetDetailsAsync(title.PagePath, ct).ConfigureAwait(false);
                var snap = BuildSnapshot(details.Seasons);
                var text = title.Snapshot == null
                    ? null // first sight: silent baseline
                    : DiffText(title.Snapshot, snap, details.Seasons);

                _tracked[title.PagePath] = title with
                {
                    Snapshot = snap,
                    Title = details.Name,
                    Poster = details.Poster ?? title.Poster,
                    UpdatedAt = DateTime.Now,
                };
                Persist();

                if (text != null)
                {
                    updates.Add(new SeriesUpdate(title.PagePath, details.Name, details.Poster, text));
                }
            }
            catch
            {
                // Offline / mirror error / parse: keep old snapshot, try next.
            }

            try { await Task.Delay(400, ct).ConfigureAwait(false); } catch { break; }
        }

        SettingsService.Instance.LastSeriesCheckUtc = DateTime.UtcNow;
        SettingsService.Instance.Save();
        if (updates.Count > 0) AddUpdates(updates);
        return updates;
    }

    public static void NotifyUpdates(IEnumerable<SeriesUpdate> updates)
    {
        foreach (var u in updates.Take(3))
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddText(Loc.Get("Notify.NewEpisodes"))
                    .AddText($"{u.Title}: {u.Text}")
                    .BuildNotification();
                Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                App.TryLog(ex);
            }
        }
    }

    /// <summary>Delayed background check on every launch. Never throws.</summary>
    public static async Task CheckOnStartupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            if (!SettingsService.Instance.SeriesUpdatesEnabled) return;
            SeedFromHistory();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var updates = await CheckForUpdatesAsync(cts.Token).ConfigureAwait(false);
            if (updates.Count > 0) NotifyUpdates(updates);
        }
        catch
        {
        }
    }
}
