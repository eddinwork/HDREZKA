using System.Net;
using System.Text;
using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

/// <summary>
/// Сетевые сценарии на фейковом HttpMessageHandler: ретраи, 403-flip,
/// редиректы и запрет понижения https→http для форм с паролем.
/// </summary>
public class RezkaClientTests
{
    private const string DetailsHtml = """
        <html><body><div id="wrapper">
          <div class="b-content__main"><div class="b-post__title">X</div></div>
        </div></body></html>
        """;

    [Fact]
    public async Task Get_403_FlipsHeaderVariant_AndRetriesOnce()
    {
        var androidSeen = new List<bool>();
        var flipped = 0;

        using var client = new RezkaClient(new RezkaClientOptions
        {
            Mirror = "https://mirror.example/",
            UseAndroidHeaders = true,
            MessageHandler = new FakeHandler((request, _) =>
            {
                var hasAndroid = request.Headers.Contains("X-Hdrezka-Android-App");
                androidSeen.Add(hasAndroid);
                return hasAndroid ? Status(HttpStatusCode.Forbidden) : Html(DetailsHtml);
            }),
        });
        client.HeadersFlipped += () => flipped++;

        var details = await client.GetDetailsAsync("films/1-x.html");

        Assert.Equal(2, androidSeen.Count);
        Assert.True(androidSeen[0]);
        Assert.False(androidSeen[1]);
        Assert.Equal(1, flipped);
        Assert.False(client.Options.UseAndroidHeaders);
        Assert.Equal("X", details.Name);
    }

    [Fact]
    public async Task Get_500_IsRetried()
    {
        var count = 0;

        using var client = CreateClient((_, _) =>
        {
            count++;
            return count == 1 ? Status(HttpStatusCode.InternalServerError) : Html(DetailsHtml);
        });

        var details = await client.GetDetailsAsync("films/1-x.html");

        Assert.Equal(2, count);
        Assert.Equal("X", details.Name);
    }

    [Fact]
    public async Task Get_404_IsNotRetried_AndMapsToNotFound()
    {
        var count = 0;

        using var client = CreateClient((_, _) =>
        {
            count++;
            return Status(HttpStatusCode.NotFound);
        });

        var ex = await Assert.ThrowsAsync<RezkaException>(() => client.GetDetailsAsync("films/404-x.html"));

        Assert.Equal(RezkaError.NotFound, ex.Kind);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Get_FollowsHttpsToHttpDowngrade_ForPublicPages()
    {
        var count = 0;

        using var client = CreateClient((_, index) =>
        {
            count++;
            return index == 1
                ? Redirect("http://mirror.example/films/1-x.html")
                : Html(DetailsHtml);
        });

        var details = await client.GetDetailsAsync("films/1-x.html");

        Assert.Equal(2, count);
        Assert.Equal("X", details.Name);
    }

    [Fact]
    public async Task SignIn_RefusesHttpMirror_WithoutAnyRequest()
    {
        var count = 0;
        using var client = new RezkaClient(new RezkaClientOptions
        {
            Mirror = "http://mirror.example/",
            MessageHandler = new FakeHandler((_, _) =>
            {
                count++;
                return Json("""{"success":true}""");
            }),
        });

        var ex = await Assert.ThrowsAsync<RezkaException>(() => client.SignInAsync("user", "pass"));

        Assert.Equal(RezkaError.InsecureMirror, ex.Kind);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SignIn_RefusesHttpsToHttpRedirect()
    {
        var count = 0;
        using var client = CreateClient((_, _) =>
        {
            count++;
            return Redirect("http://mirror.example/ajax/login/");
        });

        var ex = await Assert.ThrowsAsync<RezkaException>(() => client.SignInAsync("user", "pass"));

        Assert.Equal(RezkaError.InsecureMirror, ex.Kind);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SignIn_WorksOverHttps()
    {
        using var client = CreateClient((_, _) => Json("""{"success":true}"""));

        await client.SignInAsync("user", "pass");
    }

    private const string VideoJson = """
        {"success":true,"message":"","premium_content":0,
         "url":"[360p]https://cdn.example/360.mp4 or https://cdn2.example/360.mp4,[720p]https://cdn.example/720.mp4",
         "quality":"720p","subtitle":false,"subtitle_lns":false,"subtitle_def":false,"thumbnails":false}
        """;

    [Fact]
    public async Task GetMovieVideo_PostsToAjaxApi_EvenWhenEpisodeUrlIsSet()
    {
        // episode.Url/voice.Url — это HTML-страницы сайта, а не endpoint потока.
        // Раньше клиент шёл по ним GET-ом, получал HTML и падал с «ошибкой сети».
        HttpMethod? method = null;
        string? path = null;

        using var client = CreateClient((request, _) =>
        {
            method = request.Method;
            path = request.RequestUri?.AbsolutePath;
            return Json(VideoJson);
        });

        var episode = new MovieEpisode("6", "6", true, "series/drama/1730/13-fox/5-season/6-episode.html");
        var season = new MovieSeason("5", "5", [episode], true, null);
        var voice = new MovieVoiceActing(
            "Дубляж", "1730", "13", "", "", "", false, true, "series/drama/1730/13-fox.html");

        var video = await client.GetMovieVideoAsync(voice, season, episode, "favs");

        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/ajax/get_cdn_series/", path);
        Assert.Equal(2, video.Videos.Count);
        Assert.Equal(2, video.Videos[0].Urls.Count);
    }

    [Fact]
    public async Task GetSeriesSeasons_PostsGetEpisodes_EvenWhenVoiceHasPageUrl()
    {
        HttpMethod? method = null;

        using var client = CreateClient((request, _) =>
        {
            method = request.Method;
            const string seasonsHtml =
                "<ul id='simple-seasons-tabs'><li class='b-simple_season__item active' data-tab_id='1'>1 сезон</li></ul>";
            const string episodesHtml =
                "<div id='simple-episodes-list-1'><li class='b-simple_episode__item' data-season_id='1' data-episode_id='1'>1</li></div>";
            return Json(System.Text.Json.JsonSerializer.Serialize(new
            {
                success = true,
                seasons = seasonsHtml,
                episodes = episodesHtml,
            }));
        });

        var voice = new MovieVoiceActing(
            "Дубляж", "1730", "13", "", "", "", false, true, "series/drama/1730/13-fox.html");

        var seasons = await client.GetSeriesSeasonsAsync("1730", voice, "favs");

        Assert.Equal(HttpMethod.Post, method);
        Assert.Single(seasons);
        Assert.Single(seasons[0].Episodes);
    }

    private static RezkaClient CreateClient(Func<HttpRequestMessage, int, HttpResponseMessage> responder) =>
        new(new RezkaClientOptions
        {
            Mirror = "https://mirror.example/",
            MessageHandler = new FakeHandler(responder),
        });

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };

    private sealed class FakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        private int _count;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _count);
            return Task.FromResult(responder(request, index));
        }
    }
}
