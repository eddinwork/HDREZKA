using System.Text;
using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

/// <summary>
/// Сайт отдаёт ссылки IMDb/Кинопоиска в base64 от percent-encoded URL
/// (например, "https%3A%2F%2Fwww.imdb.com%2Ftitle%2Ftt0455275%2F").
/// Без раскодирования ссылка не открывалась в браузере.
/// </summary>
public class RatingLinkTests
{
    private static string Encode(string url) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.EscapeDataString(url)));

    private static string DetailsHtml(string imdbHref, string kpHref) => $"""
        <html><body><div id="wrapper"><div class="b-content__main">
          <div class="b-post__title">Побег</div>
          <table class="b-post__info"><tr>
            <td class="l">Рейтинги:</td>
            <td>
              <span class="b-post__info_rates imdb"><span class="bold">8.3</span><i>684K</i><a href="{imdbHref}"></a></span>
              <span class="b-post__info_rates kp"><span class="bold">8.36</span><i>326K</i><a href="{kpHref}"></a></span>
            </td>
          </tr></table>
        </div></div></body></html>
        """;

    [Fact]
    public void ParseDetails_DecodesImdbAndKpLinks()
    {
        const string imdb = "https://www.imdb.com/title/tt0455275/";
        const string kp = "https://www.kinopoisk.ru/film/258048/";

        var details = Parsers.ParseDetails(
            DetailsHtml($"/go/{Encode(imdb)}", $"/go/{Encode(kp)}"),
            "series/drama/1730-pobeg-2005-latest.html");

        Assert.Equal(imdb, details.ImdbRating?.Link);
        Assert.Equal(kp, details.KpRating?.Link);
        Assert.Equal(8.3f, details.ImdbRating?.Value);
        Assert.Null(details.SiteRating?.Link);
    }

    [Fact]
    public void ParseDetails_RejectsNonHttpRatingLink()
    {
        var evil = Convert.ToBase64String(Encoding.UTF8.GetBytes("javascript:alert(1)"));

        var details = Parsers.ParseDetails(
            DetailsHtml($"/go/{evil}", $"/go/{evil}"),
            "films/1-x.html");

        Assert.Null(details.ImdbRating?.Link);
        Assert.Null(details.KpRating?.Link);
    }
}
