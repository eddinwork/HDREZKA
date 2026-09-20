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
    DateTime? UpdatedAt = null);