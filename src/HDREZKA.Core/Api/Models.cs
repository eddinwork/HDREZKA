namespace HDREZKA.Core.Api;

public enum MediaCat { Film, Series, Anime, Cartoon, Show }

public enum MediaInfoType { Completed, Series, Wait }

public sealed record MovieSimple(
    string Id,
    string? Name = null,
    string? Details = null,
    string? Poster = null,
    MediaCat? Cat = null,
    float? Rating = null,
    MediaInfoType? Info = null,
    int? Season = null,
    int? Episode = null)
{
    public bool IsSeriesLike => Info == MediaInfoType.Series;
}

public sealed record MovieRating(float? Value, string? Votes, string? Link);

public sealed record PersonSimple(string Id, string Name, string? Photo);

public sealed record NamedLink(string Name, string Id);

public sealed record MovieVoiceActing(
    string Name,
    string VoiceId,
    string TranslatorId,
    string IsCamrip,
    string IsAds,
    string IsDirector,
    bool IsPremium,
    bool IsSelected,
    string? Url);

public sealed record MovieVoiceActingRating(string Name, float Percent);

public sealed record MovieEpisode(string EpisodeId, string Name, bool IsSelected, string? Url);

public sealed record MovieSeason(string SeasonId, string Name, IReadOnlyList<MovieEpisode> Episodes, bool IsSelected, string? Url);

public sealed record MovieSubtitle(string Name, string Link, string Lang);

public sealed record VideoTrack(
    string Quality,
    IReadOnlyList<string> Urls,
    bool NeedAccount,
    bool NeedPremium);

public sealed record MovieVideo(
    IReadOnlyList<VideoTrack> Videos,
    IReadOnlyList<MovieSubtitle> Subtitles,
    bool NeedPremium,
    string? Thumbnails)
{
    public IReadOnlyList<string> AvailableQualities => Videos.Where(v => !v.NeedAccount && !v.NeedPremium).Select(v => v.Quality).ToList();
    public IReadOnlyList<string> AccountQualities => Videos.Where(v => v.NeedAccount && !v.NeedPremium).Select(v => v.Quality).ToList();
    public IReadOnlyList<string> PremiumQualities => Videos.Where(v => !v.NeedAccount && v.NeedPremium).Select(v => v.Quality).ToList();

    public VideoTrack? GetMaxQuality() => Videos.LastOrDefault(v => !v.NeedAccount && !v.NeedPremium);

    public VideoTrack? GetClosestTo(string quality) =>
        Videos.FirstOrDefault(v => v.Quality == quality && !v.NeedAccount && !v.NeedPremium) ?? GetMaxQuality();
}

public sealed record CommentItem(
    string Id,
    string Date,
    string Author,
    string? Photo,
    string Text,
    IReadOnlyList<CommentItem> Replies,
    int Likes,
    bool IsLiked,
    bool IsAdmin);

public sealed record BookmarkCategory(int Id, string Name, int Count);

public sealed record CollectionSimple(
    string Id,
    string Title,
    string? Poster,
    int Count);

public sealed record AccountWatchItem(
    string Id,
    string Title,
    string Poster,
    string Details,
    string Info,
    string Date,
    string DataId,
    bool Watched,
    string? ActionLabel = null);

public sealed class MovieDetailed
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? OriginalName { get; init; }
    public string? Poster { get; init; }
    public string? BigPoster { get; init; }
    public string? Description { get; init; }
    public int? Duration { get; init; }
    public string? ReleaseDate { get; init; }
    public string? Year { get; init; }
    public string? Slogan { get; init; }
    public string? AgeRestriction { get; init; }
    public IReadOnlyList<MovieCountry>? Countries { get; init; }
    public IReadOnlyList<MovieGenre>? Genres { get; init; }
    public IReadOnlyList<PersonSimple>? Producers { get; init; }
    public IReadOnlyList<PersonSimple>? Actors { get; init; }
    public MovieRating? SiteRating { get; init; }
    public MovieRating? ImdbRating { get; init; }
    public MovieRating? KpRating { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsComingSoon { get; init; }
    public bool IsRated { get; init; }
    public IReadOnlyList<MovieVoiceActing>? VoiceActings { get; init; }
    public IReadOnlyList<MovieVoiceActingRating>? VoiceActingRatings { get; init; }
    public IReadOnlyList<MovieSeason>? Seasons { get; init; }
    public IReadOnlyList<MovieSimple>? WatchAlso { get; init; }
    public IReadOnlyList<SeriesScheduleGroup>? Schedule { get; init; }
    public string? Adb { get; init; }
    public string? TypeId { get; init; }
    public required string Favs { get; init; }
    public int CommentsCount { get; init; }
}

public sealed record MovieCountry(string Name, string Id);
public sealed record MovieGenre(string Name, string Id);

public sealed record PersonMovieGroup(string RoleId, IReadOnlyList<MovieSimple> Movies);

public sealed record SeriesScheduleItem(
    string Title,
    string RussianName,
    string? OriginalName,
    string ReleaseDate);

public sealed record SeriesScheduleGroup(string Name, IReadOnlyList<SeriesScheduleItem> Items);

public sealed class PersonDetailed
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? OriginalName { get; init; }
    public string? Photo { get; init; }
    public string? BigPhoto { get; init; }
    public string? Career { get; init; }
    public string? BirthDate { get; init; }
    public string? BirthPlace { get; init; }
    public string? DeathDate { get; init; }
    public string? DeathPlace { get; init; }
    public string? Height { get; init; }
    public IReadOnlyList<PersonMovieGroup>? Filmography { get; init; }
}

public enum Section { Films, Series, Cartoons, Show, Anime }

public enum ListFilter { Latest, Popular, Soon, WatchingNow }

public static class SectionExtensions
{
    public static string ToPath(this Section section) => section switch
    {
        Section.Films => "films",
        Section.Series => "series",
        Section.Cartoons => "cartoons",
        Section.Show => "show",
        Section.Anime => "animation",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    public static int ToGenreCode(this Section section) => section switch
    {
        Section.Films => 1,
        Section.Series => 2,
        Section.Cartoons => 3,
        Section.Show => 4,
        Section.Anime => 82,
        Section.Films or _ => 1,
    };
}

public static class ListFilterExtensions
{
    public static string ToQuery(this ListFilter filter) => filter switch
    {
        ListFilter.Latest => "last",
        ListFilter.Popular => "popular",
        ListFilter.Soon => "soon",
        ListFilter.WatchingNow => "watching",
        _ => "last",
    };
}
