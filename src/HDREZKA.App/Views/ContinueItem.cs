namespace HDREZKA.App.Views;

public sealed record ContinueItem(
    string Id,
    string Title,
    string? Poster,
    string Subtitle,
    double PositionSeconds,
    double DurationSeconds,
    string? Info = null,
    string? ActionLabel = null,
    string? DataId = null,
    bool IsWatched = false,
    DateTime? UpdatedAt = null,
    // Local watch-key parts (movie/translator/season/episode) for the
    // offline "watched" checkmark. Null for server-only entries.
    string? MovieKey = null,
    string? TranslatorKey = null,
    string? SeasonKey = null,
    string? EpisodeKey = null);
