using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.Json;

namespace HDREZKA.Core.Api;

public static class Parsers
{
    private static readonly HtmlParser HtmlParser = new();

    public static IDocument ParseHtml(string html) => HtmlParser.ParseDocument(html);

    // ---------- helpers ----------

    internal static string Text(IElement el) => el.TextContent.Trim();

    internal static string StripNonDigits(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsDigit(ch)) sb.Append(ch);
        }

        return sb.ToString();
    }

    internal static string StripNonLetters(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetter(ch)) sb.Append(ch);
        }

        return sb.ToString();
    }

    internal static float? ToFloat(string? s) =>
        float.TryParse(s?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

    internal static int? ToInt(string? s) =>
        int.TryParse(StripNonDigits(s ?? ""), out var i) && s?.Length > 0 ? i : null;

    internal static string? CleanPath(string? url)
    {
        if (string.IsNullOrEmpty(url) || url == "#") return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            var p = abs.AbsolutePath.Trim('/');
            return p.Length == 0 ? null : p;
        }

        var rel = url.Trim('/');
        return rel.Length == 0 ? null : rel;
    }

    internal static string? NumericId(string? cleanPath)
    {
        if (string.IsNullOrEmpty(cleanPath)) return null;
        var seg = cleanPath.Split('/').LastOrDefault() ?? "";
        var first = seg.Split('-').FirstOrDefault() ?? "";
        return first.Length > 0 && first.All(char.IsDigit) ? first : null;
    }

    internal static string ShortNumber(string s)
    {
        var digits = StripNonDigits(s);
        if (digits.Length == 0) return "0";
        if (!long.TryParse(digits, out var n)) return "0";

        return n switch
        {
            >= 1_000_000_000 => (n / 1_000_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "B",
            >= 1_000_000 => (n / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M",
            >= 1_000 => (n / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K",
            _ => n.ToString(CultureInfo.InvariantCulture),
        };
    }

    internal static void CheckDocument(IDocument doc)
    {
        var body = doc.Body ?? throw RezkaException.Parse("body");

        if (body.QuerySelectorAll("#check-form").Length > 0)
        {
            throw RezkaException.LoginRequired();
        }

        if (body.QuerySelectorAll("#wrapper").Length == 0)
        {
            throw RezkaException.MirrorBanned();
        }
    }

    // ---------- catalog / lists ----------

    public static (string Title, IReadOnlyList<MovieSimple> Items) ParseMovieList(string html)
    {
        var doc = ParseHtml(html);
        CheckDocument(doc);

        var title = doc.QuerySelector(".b-content__htitle") is { } t ? Text(t) : "";
        var items = doc.QuerySelectorAll(".b-content__inline_item").Select(ParseInlineItem).ToList();
        return (title, items);
    }

    public static IReadOnlyList<MovieSimple> ParseHotMovies(string html)
    {
        var doc = ParseHtml(html);
        return doc.QuerySelectorAll(".b-content__inline_item").Select(ParseInlineItem).ToList();
    }

    private static MovieSimple ParseInlineItem(IElement item)
    {
        var id = CleanPath(item.GetAttribute("data-url")) ?? "";

        var link = item.QuerySelector(".b-content__inline_item-link a");
        var details = item.QuerySelector(".b-content__inline_item-link div");

        var cover = item.QuerySelector(".b-content__inline_item-cover a");
        var poster = cover?.QuerySelector("img")?.GetAttribute("src");

        MediaCat? cat = null;
        float? rating = null;
        var catSpan = cover?.QuerySelector(".cat");
        if (catSpan != null)
        {
            var ratingText = catSpan.QuerySelector(".b-category-bestrating") is { } r ? Text(r) : "";
            rating = ToFloat(ratingText);
            cat = catSpan.ClassList.LastOrDefault() switch
            {
                "films" => MediaCat.Film,
                "series" => MediaCat.Series,
                "animation" => MediaCat.Anime,
                "cartoons" => MediaCat.Cartoon,
                "show" => MediaCat.Show,
                _ => null,
            };
        }

        MediaInfoType? info = null;
        int? season = null, episode = null;
        var infoEl = cover?.QuerySelector(".info");
        if (infoEl != null)
        {
            var infoHtml = infoEl.InnerHtml;
            if (infoHtml.Contains("Завершен"))
            {
                info = MediaInfoType.Completed;
            }
            else if (infoHtml.Contains("В ожидании"))
            {
                info = MediaInfoType.Wait;
            }
            else if (infoHtml.Contains("сезон") && infoHtml.Contains("серия"))
            {
                var parts = infoHtml.Contains(',')
                    ? infoHtml.Split(", ", StringSplitOptions.RemoveEmptyEntries)
                    : infoHtml.Split("<br />", StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    season = ToInt(parts[0]);
                    episode = ToInt(parts[1]);
                    if (season != null && episode != null)
                    {
                        info = MediaInfoType.Series;
                    }
                }
            }
        }

        return new MovieSimple(
            Id: id,
            Name: link != null ? Text(link) : null,
            Details: details != null ? Text(details) : null,
            Poster: poster,
            Cat: cat,
            Rating: rating,
            Info: info,
            Season: season,
            Episode: episode);
    }

    // ---------- details ----------

    public static MovieDetailed ParseDetails(string html, string pagePath)
    {
        var doc = ParseHtml(html);
        CheckDocument(doc);

        var content = doc.QuerySelector(".b-content__main") ?? throw RezkaException.Parse("details content");
        var movieId = NumericId(pagePath) ?? "";

        var name = content.QuerySelector(".b-post__title") is { } n ? Text(n) : "";
        var origName = content.QuerySelector(".b-post__origtitle") is { } on && Text(on).Length > 0 ? Text(on) : null;
        var bigPoster = content.QuerySelector(".b-sidecover a")?.GetAttribute("href");
        var poster = content.QuerySelector(".b-sidecover a img")?.GetAttribute("src");
        var description = content.QuerySelector(".b-post__description .b-post__description_text") is { } d ? Text(d) : null;

        var isAvailable = doc.QuerySelectorAll(".b-player__container_cdn").Length > 0;
        var isComingSoon = doc.QuerySelectorAll(".b-post__status_logo").Length > 0;

        IReadOnlyList<MovieVoiceActing>? voices = null;
        if (isAvailable && !isComingSoon && movieId.Length > 0)
        {
            voices = ParseVoiceActings(doc, html, movieId);
        }

        IReadOnlyList<MovieSeason>? seasons = isAvailable && !isComingSoon ? ParseSeasonsFromDocument(doc) : null;

        var isRated = content.QuerySelectorAll(".b-post__rating .b-post__rating_wrapper").Length == 0;
        var siteRatingValue = ToFloat(content.QuerySelector(".b-post__rating .num")?.TextContent);
        var siteRatingVotes = content.QuerySelector(".b-post__rating .votes span") is { } v ? ShortNumber(Text(v)) : null;
        MovieRating? siteRating = siteRatingValue != null || siteRatingVotes != null
            ? new MovieRating(siteRatingValue, siteRatingVotes, null)
            : null;

        MovieRating? imdb = null, kp = null;
        string? releaseDate = null, year = null, slogan = null, ageRestriction = null;
        int? duration = null;
        IReadOnlyList<MovieCountry>? countries = null;
        IReadOnlyList<MovieGenre>? genres = null;
        IReadOnlyList<PersonSimple>? producers = null, actors = null;

        var info = content.QuerySelector(".b-post__info");
        if (info != null)
        {
            foreach (var tr in info.QuerySelectorAll("tr"))
            {
                var tds = tr.Children.Where(c => c.TagName.Equals("TD", StringComparison.OrdinalIgnoreCase)).ToList();
                for (var i = 0; i + 1 < tds.Count; i += 2)
                {
                    var labelEl = tds[i];
                    var valueEl = tds[i + 1];
                    var label = StripNonLetters(Text(labelEl));

                    switch (label)
                    {
                        case "Рейтинги":
                            imdb = ParseRateChunk(valueEl, "imdb");
                            kp = ParseRateChunk(valueEl, "kp");
                            break;
                        case "Датавыхода":
                            releaseDate = Text(valueEl);
                            break;
                        case "Год":
                            year = Text(valueEl);
                            break;
                        case "Режиссер":
                            producers = ParsePersons(valueEl);
                            break;
                        case "Возраст":
                            ageRestriction = valueEl.QuerySelector("span") is { } a ? Text(a) : Text(valueEl);
                            break;
                        case "Страна":
                            countries = valueEl.QuerySelectorAll("a")
                                .Select(a => new MovieCountry(Text(a), CleanPath(a.GetAttribute("href")) ?? "")).ToList();
                            break;
                        case "Жанр":
                            genres = valueEl.QuerySelectorAll("a")
                                .Select(a => new MovieGenre(Text(a), CleanPath(a.GetAttribute("href")) ?? "")).ToList();
                            break;
                        case "Время":
                            duration = ToInt(Text(valueEl));
                            break;
                        case "Слоган":
                            slogan = Text(valueEl);
                            break;
                        default:
                            if (label == "Вролях")
                            {
                                actors = ParsePersons(valueEl);
                            }

                            break;
                    }
                }
            }
        }

        var typeId = doc.QuerySelector("#type_id")?.GetAttribute("value");
        var adb = doc.QuerySelector("#has_adb")?.GetAttribute("value");
        var favs = doc.QuerySelector("#ctrl_favs")?.GetAttribute("value") ?? "1";
        var commentsCount = ToInt(doc.QuerySelector("#comments-list-button em")?.TextContent) ?? 0;

        return new MovieDetailed
        {
            Id = pagePath,
            Name = name,
            OriginalName = origName,
            Poster = poster,
            BigPoster = bigPoster,
            Description = description,
            Duration = duration,
            ReleaseDate = releaseDate,
            Year = year,
            Slogan = slogan,
            AgeRestriction = ageRestriction,
            Countries = countries,
            Genres = genres,
            Producers = producers,
            Actors = actors,
            SiteRating = siteRating,
            ImdbRating = imdb,
            KpRating = kp,
            IsAvailable = isAvailable,
            IsComingSoon = isComingSoon,
            IsRated = isRated,
            VoiceActings = voices,
            Seasons = seasons,
            Adb = adb,
            TypeId = typeId,
            Favs = favs,
            CommentsCount = commentsCount,
        };
    }

    private static MovieRating? ParseRateChunk(IElement chunk, string cls)
    {
        var holder = chunk.QuerySelector($".b-post__info_rates.{cls}");
        if (holder == null) return null;

        var value = ToFloat(holder.QuerySelector(".bold")?.TextContent);
        var votes = holder.QuerySelector("i") is { } i ? ShortNumber(Text(i)) : null;
        var linkBase64 = holder.QuerySelector("a")?.GetAttribute("href");
        string? link = null;
        if (linkBase64 != null)
        {
            var b64 = linkBase64.Trim('/').Split('/').LastOrDefault();
            if (b64 != null)
            {
                try
                {
                    var pad = b64.Length % 4;
                    if (pad == 2) b64 += "==";
                    else if (pad == 3) b64 += "=";
                    link = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                }
                catch
                {
                    // ignore
                }
            }
        }

        return new MovieRating(value, votes, link);
    }

    private static IReadOnlyList<PersonSimple> ParsePersons(IElement valueEl)
    {
        var result = new List<PersonSimple>();
        foreach (var item in valueEl.QuerySelectorAll(".item"))
        {
            var a = item.QuerySelector("a");
            if (a == null) continue;
            var href = CleanPath(a.GetAttribute("href"));
            if (href == null) continue;
            var photo = item.QuerySelector(".person-name-item")?.GetAttribute("data-photo");
            result.Add(new PersonSimple(href, Text(a), photo == "null" ? null : photo));
        }

        return result;
    }

    private static IReadOnlyList<MovieVoiceActing> ParseVoiceActings(IDocument doc, string html, string movieId)
    {
        var list = doc.QuerySelector("#translators-list");
        var result = new List<MovieVoiceActing>();

        if (list != null && list.QuerySelectorAll(".b-translator__item").Length > 0)
        {
            foreach (var el in list.QuerySelectorAll(".b-translator__item"))
            {
                var img = el.QuerySelector("img");
                var name = AddFlag(Text(el), img?.GetAttribute("src"));
                result.Add(new MovieVoiceActing(
                    Name: name,
                    VoiceId: movieId,
                    TranslatorId: el.GetAttribute("data-translator_id") ?? "",
                    IsCamrip: el.GetAttribute("data-camrip") ?? "",
                    IsAds: el.GetAttribute("data-ads") ?? "",
                    IsDirector: el.GetAttribute("data-director") ?? "",
                    IsPremium: el.ClassList.Contains("b-prem_translator"),
                    IsSelected: el.ClassList.Contains("active"),
                    Url: CleanPath(el.GetAttribute("href"))));
            }

            return result;
        }

        // fallback: initCDNMoviesEvents(12345, ...) / initCDNSeriesEvents(...)
        var offsetKey = "initCDNMoviesEvents";
        var idx = html.IndexOf(offsetKey, StringComparison.Ordinal);
        if (idx < 0)
        {
            offsetKey = "initCDNSeriesEvents";
            idx = html.IndexOf(offsetKey, StringComparison.Ordinal);
        }

        if (idx < 0)
        {
            return result;
        }

        var afterKey = html[(idx + offsetKey.Length)..].Replace($"{offsetKey}({movieId}, ", "");
        var comma = afterKey.IndexOf(',');
        var translatorId = comma >= 0 ? afterKey[..comma] : afterKey;

        var voiceName = "";
        var infoTable = doc.QuerySelector(".b-post__info");
        if (infoTable != null)
        {
            foreach (var tr in infoTable.QuerySelectorAll("tr"))
            {
                var label = tr.QuerySelector(".l");
                if (label != null && Text(label).Contains("В переводе"))
                {
                    var value = tr.QuerySelector("td:not(.l)");
                    voiceName = value != null ? Text(value) : "";
                    break;
                }
            }
        }

        result.Add(new MovieVoiceActing(voiceName, movieId, translatorId, "", "", "", false, true, null));
        return result;
    }

    private static string AddFlag(string name, string? imgSrc)
    {
        if (string.IsNullOrEmpty(imgSrc)) return name;
        var file = imgSrc.Split('/').LastOrDefault() ?? "";
        if (file.Contains("ua")) return name + " 🇺🇦";
        if (file.Contains("kz")) return name + " 🇰🇿";
        if (file.Contains("ru")) return name + " 🇷🇺";
        return name;
    }

    internal static IReadOnlyList<MovieSeason> ParseSeasonsFromDocument(IDocument doc)
    {
        var seasonTabs = doc.QuerySelectorAll("#simple-seasons-tabs .b-simple_season__item");
        var episodesLists = doc.QuerySelectorAll("[id^='simple-episodes-list']");

        if (seasonTabs.Length == 0 && episodesLists.Length == 0) return [];

        if (seasonTabs.Length == 0)
        {
            var firstEpisode = doc.QuerySelector("[id*='simple-episodes-list'] .b-simple_episode__item");
            var seasonId = firstEpisode?.GetAttribute("data-season_id") ?? "1";
            var list = doc.QuerySelector($"#simple-episodes-list-{seasonId}");
            var episodes = ParseEpisodes(list);
            return [new MovieSeason(seasonId, seasonId, episodes, true, null)];
        }

        var result = new List<MovieSeason>();
        foreach (var tab in seasonTabs)
        {
            var id = tab.GetAttribute("data-tab_id") ?? "";
            var list = doc.QuerySelector($"#simple-episodes-list-{id}");
            var nameRaw = Text(tab);
            var name = StripNonDigits(nameRaw).Length == 0 ? nameRaw : StripNonDigits(nameRaw);
            result.Add(new MovieSeason(
                id,
                name,
                ParseEpisodes(list),
                tab.ClassList.Contains("active"),
                CleanPath(tab.GetAttribute("href"))));
        }

        return result;
    }

    private static IReadOnlyList<MovieEpisode> ParseEpisodes(IElement? list)
    {
        var result = new List<MovieEpisode>();
        if (list == null) return result;
        foreach (var ep in list.QuerySelectorAll(".b-simple_episode__item"))
        {
            var textRaw = Text(ep);
            var name = StripNonDigits(textRaw).Length == 0 ? textRaw : StripNonDigits(textRaw);
            result.Add(new MovieEpisode(
                ep.GetAttribute("data-episode_id") ?? "",
                name,
                ep.ClassList.Contains("active"),
                CleanPath(ep.GetAttribute("href"))));
        }

        return result;
    }

    public static IReadOnlyList<MovieSeason> ParseSeasonsResponse(string body)
    {
        try
        {
            var doc = ParseHtml(body);
            var seasons = ParseSeasonsFromDocument(doc);
            if (seasons.Count > 0 || doc.QuerySelectorAll("[id^='simple-episodes-list']").Length > 0 || doc.QuerySelectorAll("#simple-seasons-tabs").Length > 0)
            {
                return seasons;
            }
        }
        catch
        {
            // fall through to JSON
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        var seasonsHtml = root.TryGetProperty("seasons", out var sEl) && sEl.ValueKind == JsonValueKind.String
            ? sEl.GetString()
            : null;
        var episodesHtml = root.TryGetProperty("episodes", out var eEl) && eEl.ValueKind == JsonValueKind.String
            ? eEl.GetString()
            : null;

        if (seasonsHtml == null || episodesHtml == null)
        {
            throw RezkaException.Parse("seasons json");
        }

        var seasonsDoc = ParseHtml(seasonsHtml);
        var episodesDoc = ParseHtml(episodesHtml);

        var result = new List<MovieSeason>();
        foreach (var season in seasonsDoc.QuerySelectorAll(".b-simple_season__item"))
        {
            var seasonId = season.GetAttribute("data-tab_id") ?? "";
            var seasonEpisodes = new List<MovieEpisode>();
            foreach (var episode in episodesDoc.QuerySelectorAll($".b-simple_episode__item[data-season_id='{seasonId}']"))
            {
                var textRaw = Text(episode);
                seasonEpisodes.Add(new MovieEpisode(
                    episode.GetAttribute("data-episode_id") ?? "",
                    StripNonDigits(textRaw).Length == 0 ? textRaw : StripNonDigits(textRaw),
                    episode.ClassList.Contains("active"),
                    CleanPath(episode.GetAttribute("href"))));
            }

            result.Add(new MovieSeason(
                seasonId,
                StripNonDigits(Text(season)).Length == 0 ? Text(season) : StripNonDigits(Text(season)),
                seasonEpisodes,
                season.ClassList.Contains("active"),
                CleanPath(season.GetAttribute("href"))));
        }

        return result;
    }

    // ---------- video ----------

    public static MovieVideo ParseVideoResponse(string body)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // fallback: script response like ... {"id": ...); });
            var start = body.IndexOf("{\"id\":", StringComparison.Ordinal);
            var end = body.LastIndexOf("); });", StringComparison.Ordinal);
            if (start < 0 || end <= start) throw RezkaException.Parse("video json");

            using var doc = JsonDocument.Parse(body[start..end]);
            root = doc.RootElement.Clone();
        }

        string? encrypted = null;
        if (root.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
        {
            encrypted = urlEl.GetString();
        }
        else if (root.TryGetProperty("streams", out var streamsEl) && streamsEl.ValueKind == JsonValueKind.String)
        {
            encrypted = streamsEl.GetString();
        }

        if (string.IsNullOrEmpty(encrypted))
        {
            if (root.TryGetProperty("message", out var msgEl) &&
                (msgEl.GetString() ?? "").Contains("необходимо авторизоваться", StringComparison.OrdinalIgnoreCase))
            {
                throw RezkaException.LoginRequired();
            }

            throw RezkaException.Parse("video url");
        }

        var streams = StreamDecryptor.Decrypt(encrypted!);

        var videos = new List<VideoTrack>();
        foreach (var stream in streams.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var nameStart = stream.IndexOf('[');
            var nameEnd = stream.IndexOf(']');
            if (nameStart < 0 || nameEnd < nameStart) continue;

            var nameHtml = stream[(nameStart + 1)..nameEnd];
            var linksPart = stream[(nameEnd + 1)..];

            var nameFrag = ParseHtml(nameHtml);
            var name = nameFrag.Body?.TextContent.Trim() ?? "";
            var needPremium = nameFrag.QuerySelectorAll("img").Length > 0;

            var links = linksPart
                .Split(" or ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l =>
                {
                    var idx = l.LastIndexOf(".mp4", StringComparison.OrdinalIgnoreCase);
                    return idx >= 0 ? l[..(idx + 4)] : l;
                })
                .Where(l => l != "null" && l.Length > 0)
                .Distinct()
                .ToList();

            videos.Add(new VideoTrack(name, links, links.Count == 0, needPremium));
        }

        var subtitles = new List<MovieSubtitle>();
        if (root.TryGetProperty("subtitle", out var subEl) && subEl.ValueKind == JsonValueKind.String &&
            root.TryGetProperty("subtitle_lns", out var lnsEl) && lnsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var raw in (subEl.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = raw.Trim();
                var lb = s.IndexOf('[');
                var rb = s.IndexOf(']');
                if (lb < 0 || rb < lb) continue;
                var name = s[(lb + 1)..rb];
                var link = s[(rb + 1)..];
                if (lnsEl.TryGetProperty(name, out var langEl) && langEl.ValueKind == JsonValueKind.String)
                {
                    subtitles.Add(new MovieSubtitle(name, link, langEl.GetString() ?? ""));
                }
            }
        }

        var needPremiumFlag = root.TryGetProperty("premium_content", out var premEl) && premEl.ValueKind == JsonValueKind.Number && premEl.GetInt32() == 1;
        var thumbnails = root.TryGetProperty("thumbnails", out var thEl) && thEl.ValueKind == JsonValueKind.String ? thEl.GetString() : null;

        return new MovieVideo(videos, subtitles, needPremiumFlag, thumbnails);
    }

    // ---------- account ----------

    public static void ParseLoginResponse(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        if (!root.TryGetProperty("success", out var successEl) || successEl.ValueKind != JsonValueKind.True)
        {
            var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;
            if (!string.IsNullOrEmpty(message))
            {
                var doc = ParseHtml(message!);
                var items = doc.QuerySelector("ul")?.QuerySelectorAll("li").Select(Text).ToList();
                if (items is { Count: > 0 })
                {
                    throw RezkaException.Site(string.Join("\n", items));
                }
            }

            throw RezkaException.Site(message ?? "Sign in failed");
        }
    }

    public static IReadOnlyList<AccountWatchItem> ParseContinueWatching(string html)
    {
        var doc = ParseHtml(html);
        CheckDocument(doc);

        var result = new List<AccountWatchItem>();
        foreach (var item in doc.QuerySelectorAll(".b-videosaves__list_item[id^='videosave']"))
        {
            var link = item.QuerySelector(".td.title a");
            var id = CleanPath(link?.GetAttribute("href"));
            if (id == null) continue;

            var title = "";
            if (link != null)
            {
                var linkClone = link.Clone() as IElement;
                linkClone?.QuerySelector("small")?.Remove();
                title = linkClone != null ? Text(linkClone) : Text(link);
            }

            var poster = link?.GetAttribute("data-cover_url");
            var details = item.QuerySelector(".td.title small") is { } d ? Text(d) : "";

            var infoEl = item.QuerySelector(".td.info");
            var info = "";
            string? actionLabel = null;
            if (infoEl != null)
            {
                var span = infoEl.QuerySelector("span");
                if (span != null)
                {
                    actionLabel = Text(span);
                    info = infoEl.TextContent.Replace(span.TextContent, "").Trim();
                }

                if (info.Length == 0) info = Text(infoEl);
            }

            var date = item.QuerySelector(".td.date") is { } dt ? Text(dt) : "";
            var dataId = item.QuerySelector(".i-sprt.delete")?.GetAttribute("data-id") ?? "";
            var watched = item.QuerySelector(".i-sprt.view")?.ClassList.Contains("watched") == true;

            result.Add(new AccountWatchItem(
                Id: id,
                Title: title,
                Poster: poster is "null" or null ? "" : poster,
                Details: details,
                Info: info,
                Date: date,
                DataId: dataId,
                Watched: watched,
                ActionLabel: string.IsNullOrEmpty(actionLabel) ? null : actionLabel));
        }

        return result;
    }

    public static IReadOnlyList<BookmarkCategory> ParseBookmarksCategories(string html)
    {
        var doc = ParseHtml(html);
        CheckDocument(doc);

        var result = new List<BookmarkCategory>();
        foreach (var el in doc.QuerySelectorAll(".b-favorites_content__cats_list_item"))
        {
            var id = ToInt(el.GetAttribute("data-cat_id")) ?? 0;
            var name = el.QuerySelector(".name") is { } n ? Text(n) : "";
            var count = el.QuerySelector(".num-holder .fb-1") is { } c ? ToInt(Text(c)) ?? 0 : 0;
            result.Add(new BookmarkCategory(id, name, count));
        }

        return result;
    }

    public static IReadOnlyList<CollectionSimple> ParseCollectionsList(string html)
    {
        var doc = ParseHtml(html);
        CheckDocument(doc);

        var result = new List<CollectionSimple>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collection-folder selectors first: generic movie-item selectors
        // (.b-content__inline_item) also match sidebars and would produce
        // garbage "collections" pointing at movie pages.
        var selectors = new[]
        {
            ".b-content__collections_item",
            "[data-collection-id]",
            ".b-collections__item",
            ".collections__item",
            ".b-content__inline_item",
            ".b-content__item"
        };

        var items = new List<IElement>();
        foreach (var selector in selectors)
        {
            items = doc.QuerySelectorAll(selector).ToList();
            if (items.Count > 0) break;
        }

        foreach (var el in items)
        {
            // Prefer the titled folder link (a.title), then any /collections/ link.
            var linkEl = el.QuerySelector("a.title") ?? el.QuerySelector("a[href*='/collections/']") ?? el.QuerySelector("a");
            if (linkEl == null) continue;

            var href = linkEl.GetAttribute("href") ?? "";
            // Folder links point at /collections/<slug>/ — movie links (sidebars,
            // "watching now", etc.) must be skipped.
            if (!href.Contains("/collections/", StringComparison.OrdinalIgnoreCase)) continue;
            var id = href.Trim('/').Split('/').LastOrDefault() ?? "";
            if (id.Length == 0 || !seen.Add(id)) continue;
            var title = linkEl.TextContent.Trim();

            var posterEl = el.QuerySelector("img");
            var poster = posterEl?.GetAttribute("src") ?? posterEl?.GetAttribute("data-src") ?? "";

            // Folder titles are sometimes image-only: fall back to attributes,
            // otherwise every folder is skipped as "empty title".
            if (string.IsNullOrEmpty(title))
            {
                var titleEl = el.QuerySelector(".b-content__inline_item-link, .title, h3, h4, [class*='title']");
                title = titleEl?.TextContent.Trim() ?? "";
            }

            if (string.IsNullOrEmpty(title))
            {
                title = linkEl.GetAttribute("title") ?? posterEl?.GetAttribute("alt") ?? "";
                title = title.Trim();
            }
            
            var countEl = el.QuerySelector(".num, .count, [class*='count']");
            var count = ToInt(countEl?.TextContent?.Trim()) ?? 0;

            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(title))
            {
                result.Add(new CollectionSimple(id, title, poster, count));
            }
        }

        return result;
    }

    // ---------- comments ----------

    public static IReadOnlyList<CommentItem> ParseCommentsResponse(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        if (!root.TryGetProperty("comments", out var commentsEl) || commentsEl.ValueKind != JsonValueKind.String)
        {
            return [];
        }

        var html = commentsEl.GetString();
        if (string.IsNullOrEmpty(html)) return [];

        var doc = ParseHtml(html!);
        return ParseCommentsLevel(doc.Body!, 0);
    }

    private static IReadOnlyList<CommentItem> ParseCommentsLevel(IElement root, int depth)
    {
        var result = new List<CommentItem>();
        foreach (var el in root.QuerySelectorAll($".comments-tree-item[data-indent='{depth}']"))
        {
            var textEl = el.QuerySelector(".text");
            var text = textEl != null ? ElementToPlainText(textEl) : "";

            var like = el.QuerySelector(".b-comment__like_it");

            result.Add(new CommentItem(
                Id: el.GetAttribute("data-id") ?? "",
                Date: (el.QuerySelector(".date") is { } d ? Text(d) : "").Replace("оставлен ", ""),
                Author: el.QuerySelector(".name") is { } a ? Text(a) : "",
                Photo: el.QuerySelector("img")?.GetAttribute("src"),
                Text: text,
                Replies: ParseCommentsLevel(el, depth + 1),
                Likes: ToInt(like?.GetAttribute("data-likes_num")) ?? 0,
                IsLiked: like != null && like.ClassList.Contains("disabled"),
                IsAdmin: el.QuerySelector(".b-comment")?.ClassList.Contains("b-comment__admin") == true));
        }

        return result;
    }

    internal static string ElementToPlainText(IElement el)
    {
        var clone = el.Clone() as IElement;
        if (clone != null)
        {
            foreach (var spoilerTitle in clone.QuerySelectorAll(".title_spoiler").ToList())
            {
                spoilerTitle.Remove();
            }

            foreach (var br in clone.QuerySelectorAll("br").ToList())
            {
                br.TextContent = "\n";
            }

            return clone.TextContent.Replace("\r", "").Trim();
        }

        return el.TextContent.Trim();
    }
}
