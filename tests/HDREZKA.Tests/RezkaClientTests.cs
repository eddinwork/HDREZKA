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
