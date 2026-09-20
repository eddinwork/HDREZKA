using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

public class ParserTests
{
    private const string CatalogHtml = """
        <html><body>
        <div id="wrapper">
        <div class="b-content__htitle"><h1>Новые фильмы</h1></div>
        <div class="b-content__inline_items">
          <div class="b-content__inline_item" data-url="https://hdrzk.org/films/1234-movie-one.html">
            <div class="b-content__inline_item-cover">
              <a href="https://hdrzk.org/films/1234-movie-one.html">
                <img src="https://cdn.example.org/posters/1.jpg">
                <span class="cat films"><span class="b-category-bestrating">8.2</span></span>
                <div class="info">Завершен</div>
              </a>
            </div>
            <div class="b-content__inline_item-link"><a href="#">Фильм один</a><div>2024, США, фантастика</div></div>
          </div>
          <div class="b-content__inline_item" data-url="https://hdrzk.org/series/5678-serial-two.html">
            <div class="b-content__inline_item-cover">
              <a href="https://hdrzk.org/series/5678-serial-two.html">
                <img src="https://cdn.example.org/posters/2.jpg">
                <span class="cat series"></span>
                <div class="info">2 сезон, 5 серия</div>
              </a>
            </div>
            <div class="b-content__inline_item-link"><a href="#">Сериал два</a><div>2023, Канада</div></div>
          </div>
        </div>
        </div></body></html>
        """;

    [Fact]
    public void ParseMovieList_Works()
    {
        var (title, items) = Parsers.ParseMovieList(CatalogHtml);

        Assert.Equal("Новые фильмы", title);
        Assert.Equal(2, items.Count);

        var first = items[0];
        Assert.Equal("films/1234-movie-one.html", first.Id);
        Assert.Equal("Фильм один", first.Name);
        Assert.Equal("2024, США, фантастика", first.Details);
        Assert.Equal("https://cdn.example.org/posters/1.jpg", first.Poster);
        Assert.Equal(MediaCat.Film, first.Cat);
        Assert.Equal(8.2f, first.Rating);
        Assert.Equal(MediaInfoType.Completed, first.Info);

        var second = items[1];
        Assert.Equal(MediaCat.Series, second.Cat);
        Assert.Equal(MediaInfoType.Series, second.Info);
        Assert.Equal(2, second.Season);
        Assert.Equal(5, second.Episode);
    }

    private const string DetailsHtml = """
        <html><body>
        <div id="wrapper">
        <div class="b-content__main">
          <div class="b-post__title">Тестовый фильм</div>
          <div class="b-post__origtitle">Test Movie (2024)</div>
          <div class="b-sidecover"><a href="https://cdn.example.org/big.jpg"><img src="https://cdn.example.org/small.jpg"></a></div>
          <table class="b-post__info">
            <tr><td class="l">Год:</td><td><a href="https://hdrzk.org/year/2024/">2024</a></td>
                <td class="l">Страна:</td><td><a href="https://hdrzk.org/country/usa/">США</a></td></tr>
            <tr><td class="l">Жанр:</td><td><a href="https://hdrzk.org/genre/fantastic/">фантастика</a></td>
                <td class="l">Время:</td><td>127 мин.</td></tr>
            <tr><td class="l">Режиссер:</td><td><div class="persons-list-holder"><div class="item"><a href="https://hdrzk.org/person/42-name.html">Режиссёр Один</a></div></div></td></tr>
          </table>
          <div class="b-post__description"><div class="b-post__description_text">Описание тестового фильма.</div></div>
          <div class="b-player__container_new_signature" data-player_id="1">
            <div class="b-player__container_cdn"></div>
          </div>
          <div id="translators-list">
            <ul>
              <li class="b-translator__item active" data-translator_id="128" data-camrip="" data-ads="" data-director="">Дубляж</li>
              <li class="b-translator__item" data-translator_id="245" data-camrip="" data-ads="" data-director="">Многоголосый</li>
            </ul>
          </div>
          <input type="hidden" id="ctrl_favs" value="favsvalue123">
          <input type="hidden" id="type_id" value="1">
          <input type="hidden" id="has_adb" value="0">
          <div id="comments-list-button">Комментарии <em>42</em></div>
        </div>
        </div></body></html>
        """;

    [Fact]
    public void ParseDetails_Works()
    {
        var details = Parsers.ParseDetails(DetailsHtml, "films/999-test-movie.html");

        Assert.Equal("Тестовый фильм", details.Name);
        Assert.Equal("Test Movie (2024)", details.OriginalName);
        Assert.Equal("https://cdn.example.org/small.jpg", details.Poster);
        Assert.Equal("Описание тестового фильма.", details.Description);
        Assert.True(details.IsAvailable);
        Assert.False(details.IsComingSoon);
        Assert.Equal("favsvalue123", details.Favs);
        Assert.Equal(42, details.CommentsCount);
        Assert.NotNull(details.VoiceActings);
        Assert.Equal(2, details.VoiceActings!.Count);
        Assert.Equal("128", details.VoiceActings[0].TranslatorId);
        Assert.True(details.VoiceActings[0].IsSelected);
        Assert.Equal("999", details.VoiceActings[0].VoiceId);
        Assert.Equal("2024", details.Year);
        Assert.NotNull(details.Countries);
        Assert.Equal("США", details.Countries![0].Name);
        Assert.NotNull(details.Genres);
        Assert.Equal("фантастика", details.Genres![0].Name);
        Assert.Equal(127, details.Duration);
        Assert.NotNull(details.Producers);
        Assert.Equal("Режиссёр Один", details.Producers![0].Name);
    }

    [Fact]
    public void ParseSeasonsResponse_Json_Works()
    {
        var seasonsHtml = "<ul id='simple-seasons-tabs'><li class='b-simple_season__item active' data-tab_id='1'>1 сезон</li><li class='b-simple_season__item' data-tab_id='2'>2 сезон</li></ul>";
        var episodesHtml = "<div id='simple-episodes-list-1'><ul><li class='b-simple_episode__item active' data-season_id='1' data-episode_id='1'>1</li><li class='b-simple_episode__item' data-season_id='1' data-episode_id='2'>2</li></ul></div><div id='simple-episodes-list-2'><ul><li class='b-simple_episode__item' data-season_id='2' data-episode_id='1'>1</li></ul></div>";
        var json = $$"""{"success":true,"seasons":{{System.Text.Json.JsonSerializer.Serialize(seasonsHtml)}},"episodes":{{System.Text.Json.JsonSerializer.Serialize(episodesHtml)}}}""";

        var seasons = Parsers.ParseSeasonsResponse(json);

        Assert.Equal(2, seasons.Count);
        Assert.Equal("1", seasons[0].SeasonId);
        Assert.True(seasons[0].IsSelected);
        Assert.Equal(2, seasons[0].Episodes.Count);
        Assert.Equal("1", seasons[0].Episodes[0].EpisodeId);
        Assert.True(seasons[0].Episodes[0].IsSelected);
        Assert.Equal(1, seasons[1].Episodes.Count);
    }

    [Fact]
    public void ParseVideoResponse_Plain_Works()
    {
        var json = """
        {"success":true,
         "url":"[360p]https://cdn1.example.org/hls/movie/360.mp4 or https://cdn2.example.org/hls/movie/360.mp4,[720p]https://cdn1.example.org/hls/movie/720.mp4 or https://cdn2.example.org/hls/movie/720.mp4,[1080p]https://secure.example.org/hls/abc.txt:hls:manifest.m3u8",
         "subtitle":"[original]https://sub.example.org/orig.vtt,[ua]https://sub.example.org/ua.vtt",
         "subtitle_lns":{"original":"en","ua":"uk"},
         "premium_content":0}
        """;

        var video = Parsers.ParseVideoResponse(json);

        Assert.Equal(3, video.Videos.Count);
        Assert.Equal("360p", video.Videos[0].Quality);
        Assert.Equal(2, video.Videos[0].Urls.Count);
        Assert.Equal("1080p", video.Videos[2].Quality);
        Assert.Equal("https://secure.example.org/hls/abc.txt:hls:manifest.m3u8", video.Videos[2].Urls[0]);
        Assert.Equal(2, video.Subtitles.Count);
        Assert.Equal("en", video.Subtitles[0].Lang);
        Assert.False(video.NeedPremium);
        Assert.Equal(3, video.AvailableQualities.Count);
    }

    [Fact]
    public void ParseCommentsResponse_Works()
    {
        var commentsHtml = """
        <div class="comments-tree-item" data-id="11" data-indent="0">
          <div class="b-comment">
            <div class="header"><a class="name">Юзер</a><span class="date">1 января, 10:00</span></div>
            <img src="https://avatar.example.org/u1.jpg">
            <div class="text">Первый <b>комментарий</b><div class="text_spoiler">спойлер</div></div>
            <div class="b-comment__like_it" data-likes_num="7">7</div>
            <div class="comments-tree-item" data-id="12" data-indent="1">
              <div class="b-comment">
                <div class="header"><a class="name">Юзер2</a><span class="date">1 января, 11:00</span></div>
                <div class="text">Ответ</div>
                <div class="b-comment__like_it disabled" data-likes_num="0">0</div>
              </div>
            </div>
          </div>
        </div>
        """;

        var body = $$"""{"comments":{{System.Text.Json.JsonSerializer.Serialize(commentsHtml)}}}""";

        var comments = Parsers.ParseCommentsResponse(body);

        Assert.Single(comments);
        Assert.Equal("11", comments[0].Id);
        Assert.Equal("Юзер", comments[0].Author);
        Assert.Equal(7, comments[0].Likes);
        Assert.Contains("комментарий", comments[0].Text);
        Assert.Single(comments[0].Replies);
        Assert.Equal("12", comments[0].Replies[0].Id);
        Assert.True(comments[0].Replies[0].IsLiked);
    }

    [Fact]
    public void ParseBookmarksCategories_Works()
    {
        var html = """
        <html><body><div id="wrapper">
        <div class="b-favorites_content__cats_list">
          <div class="b-favorites_content__cats_list_item" data-cat_id="1">
            <div class="name">Смотреть позже</div>
            <div class="num-holder"><b class="fb-1">10</b></div>
          </div>
          <div class="b-favorites_content__cats_list_item" data-cat_id="2">
            <div class="name">Любимое</div>
            <div class="num-holder"><b class="fb-1">3</b></div>
          </div>
        </div>
        </div></body></html>
        """;

        var cats = Parsers.ParseBookmarksCategories(html);

        Assert.Equal(2, cats.Count);
        Assert.Equal(1, cats[0].Id);
        Assert.Equal("Смотреть позже", cats[0].Name);
        Assert.Equal(10, cats[0].Count);
    }

    [Fact]
    public void ParseContinueWatching_Works()
    {
        var html = """
        <html><body><div id="wrapper">
        <div class="b-videosaves">
          <div class="b-videosaves__list_item" id="videosave12">
            <div class="td title">
              <a href="https://hdrzk.org/films/1234-movie-one.html" data-cover_url="https://cdn.example.org/p1.jpg">
                Фильм один<small>2024, США, фантастика</small>
              </a>
            </div>
            <div class="td info"><span>Смотреть онлайн</span>Осталось 35 мин</div>
            <div class="td date">Сегодня</div>
            <div class="i-sprt view watched"></div>
            <div class="i-sprt delete" data-id="42"></div>
          </div>
        </div>
        </div></body></html>
        """;

        var items = Parsers.ParseContinueWatching(html);

        Assert.Single(items);
        var item = items[0];
        Assert.Equal("films/1234-movie-one.html", item.Id);
        Assert.Equal("Фильм один", item.Title);
        Assert.Equal("https://cdn.example.org/p1.jpg", item.Poster);
        Assert.Equal("2024, США, фантастика", item.Details);
        Assert.Equal("Осталось 35 мин", item.Info);
        Assert.Equal("Сегодня", item.Date);
        Assert.Equal("42", item.DataId);
        Assert.True(item.Watched);
        Assert.Equal("Смотреть онлайн", item.ActionLabel);
    }

    [Fact]
    public void ParseLoginResponse_Failure_ThrowsSiteError()
    {
        var body = """{"success":false,"message":"<ul><li>Неверный логин</li></ul>"}""";
        var ex = Assert.Throws<RezkaException>(() => Parsers.ParseLoginResponse(body));
        Assert.Equal(RezkaError.Site, ex.Kind);
        Assert.Contains("Неверный логин", ex.Message);
    }

    [Fact]
    public void ParseCollectionsList_FoldersParsed()
    {
        var html = """
        <html><body><div id="wrapper">
        <div class="b-collections__list">
          <div class="b-collections__item">
            <a href="https://hdrzk.org/collections/filmy-2026-goda-12/">
              <img src="https://cdn.example.org/c1.jpg">
              <span class="title">Фильмы 2026 года</span>
            </a>
            <span class="num">150</span>
          </div>
          <div class="b-collections__item">
            <a href="https://hdrzk.org/collections/multfilmy-2025-goda-34/">
              <img data-src="https://cdn.example.org/c2.jpg">
              <span class="title">Мультфильмы 2025 года</span>
            </a>
          </div>
        </div>
        </div></body></html>
        """;

        var cols = Parsers.ParseCollectionsList(html);

        Assert.Equal(2, cols.Count);
        Assert.Equal("filmy-2026-goda-12", cols[0].Id);
        Assert.Equal("Фильмы 2026 года", cols[0].Title);
        Assert.Equal("https://cdn.example.org/c1.jpg", cols[0].Poster);
        Assert.Equal(150, cols[0].Count);
        Assert.Equal("multfilmy-2025-goda-34", cols[1].Id);
    }

    [Fact]
    public void ParseCollectionsList_IgnoresMovieSidebarItems()
    {        var html = """
        <html><body><div id="wrapper">
        <div class="b-collections__list">
          <div class="b-collections__item">
            <a href="https://hdrzk.org/collections/filmy-2026-goda-12/">Фильмы 2026 года</a>
          </div>
        </div>
        <div class="sidebar">
          <div class="b-content__inline_item">
            <div class="b-content__inline_item-cover">
              <a href="https://hdrzk.org/films/1234-movie-one.html"><img src="https://cdn.example.org/p1.jpg"></a>
            </div>
            <div class="b-content__inline_item-link"><a href="#">Фильм один</a></div>
          </div>
          <div class="b-content__inline_item">
            <div class="b-content__inline_item-link"><a href="https://hdrzk.org/collections/filmy-2026-goda-12/">Фильмы 2026 года</a></div>
          </div>
        </div>
        </div></body></html>
        """;

        var cols = Parsers.ParseCollectionsList(html);

        Assert.Single(cols);
        Assert.Equal("filmy-2026-goda-12", cols[0].Id);
    }

    [Fact]
    public void ParseCollectionsList_RealSiteMarkup()
    {
        // Shape observed on /collections/: .b-content__collections_item
        // with a.title folder links plus bare menu links.
        var html = """
        <html><body><div id="wrapper">
        <div class="b-content__collections_list clearfix">
          <div class="b-content__collections_item">
            <span class="num">3</span>
            <a class="title" href="https://hdrzk.org/collections/4652-filmy-2027-goda/">Фильмы 2027 года</a>
          </div>
          <div class="b-content__collections_item">
            <span class="num">715</span>
            <a class="title" href="https://hdrzk.org/collections/4074-filmy-2026-goda/">Фильмы 2026 года</a>
          </div>
          <div class="b-content__collections_item">
            <span class="num">214</span>
            <a class="title" href="https://hdrzk.org/collections/3836-anime-2025-goda/">Аниме 2025 года</a>
          </div>
          <div class="menu">
            <a href="/collections/834-filmy-netflix/"></a>
          </div>
        </div>
        </div></body></html>
        """;

        var cols = Parsers.ParseCollectionsList(html);

        Assert.Equal(3, cols.Count);
        Assert.Equal("4652-filmy-2027-goda", cols[0].Id);
        Assert.Equal("Фильмы 2027 года", cols[0].Title);
        Assert.Equal(3, cols[0].Count);
        Assert.Equal("4074-filmy-2026-goda", cols[1].Id);
        Assert.Equal("3836-anime-2025-goda", cols[2].Id);
        Assert.Equal("Аниме 2025 года", cols[2].Title);
    }
}
