using System.Text.Json;
using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

/// <summary>
/// Сайт отдаёт ajax-ответы то чистым JSON, то JSONP/скриптом с префиксом.
/// Раньше вырезание по маркерам ломало такие ответы («ошибка сети» в плеере).
/// </summary>
public class JsonParsingTests
{
    [Fact]
    public void Loose_ParsesPlainJson()
    {
        using var doc = Parsers.ParseJsonLoose("""{"success":true,"url":"x"}""", "test", "url");
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Loose_ParsesJsonpWrapper()
    {
        const string body = """jQuery1234567890_123({"success":true,"url":"[360p]https://cdn/x.mp4"});""";

        using var doc = Parsers.ParseJsonLoose(body, "test", "url");

        Assert.Equal("[360p]https://cdn/x.mp4", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public void Loose_SkipsLeadingScriptObject()
    {
        // Перед данными стоит JS-объект конфигурации: первый {...} — не наш.
        const string body = """
            (function(){ var cfg = {a:1, b:{c:2}}; return {"success":true,"url":"[720p]https://cdn/y.mp4"}; })();
            """;

        using var doc = Parsers.ParseJsonLoose(body, "test", "url");

        Assert.Equal("[720p]https://cdn/y.mp4", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public void Loose_HandlesBracesInsideStrings()
    {
        const string body = """callback({"message":"a } b { c","url":"[360p]https://cdn/z.mp4"});""";

        using var doc = Parsers.ParseJsonLoose(body, "test", "url");

        Assert.Equal("[360p]https://cdn/z.mp4", doc.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public void Loose_NoJson_ThrowsRezkaParse()
    {
        var ex = Assert.Throws<RezkaException>(() => Parsers.ParseJsonLoose("<html>no json</html>", "test", "url"));
        Assert.Equal(RezkaError.Parse, ex.Kind);
    }

    [Fact]
    public void VideoResponse_ParsesJsonpWrapped()
    {
        var json = JsonSerializer.Serialize(new
        {
            success = true,
            message = "",
            premium_content = 0,
            url = "[360p]https://cdn/a/360.mp4 or https://cdn2/a/360.mp4,[720p]https://cdn/a/720.mp4",
            quality = "720p",
            subtitle = false,
            subtitle_lns = false,
            thumbnails = false,
        });

        var video = Parsers.ParseVideoResponse("jQuery_" + json + ");");

        Assert.Equal(2, video.Videos.Count);
        Assert.Equal("360p", video.Videos[0].Quality);
        Assert.Equal(2, video.Videos[0].Urls.Count);
        Assert.Equal("https://cdn/a/720.mp4", video.Videos[1].Urls[0]);
    }

    [Fact]
    public void VideoResponse_UrlFalse_IsStreamUnavailable()
    {
        const string body = """
            {"success":true,"message":"","premium_content":0,"url":false,"quality":false,
             "subtitle":false,"subtitle_lns":false,"subtitle_def":false,"thumbnails":false}
            """;

        var ex = Assert.Throws<RezkaException>(() => Parsers.ParseVideoResponse(body));

        Assert.Equal(RezkaError.StreamUnavailable, ex.Kind);
    }
}
