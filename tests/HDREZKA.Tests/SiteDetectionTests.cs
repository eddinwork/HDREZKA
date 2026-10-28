using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

public class SiteDetectionTests
{
    private const string RealPage = """
        <html><body>
        <div id="wrapper">
          <div class="b-content__htitle"><h1>Новые фильмы</h1></div>
          <div class="b-content__inline_item" data-url="https://hdrzk.org/films/1-film.html"></div>
        </div>
        </body></html>
        """;

    [Fact]
    public void LooksLikeSite_RealCatalogPage_True()
    {
        Assert.True(Parsers.LooksLikeSite(RealPage));
    }

    [Fact]
    public void LooksLikeSite_BlockPageWithWrapper_StillFalse()
    {
        // Именно так выглядел ложный «рабочий» ответ: 200, есть #wrapper,
        // но никакой разметки каталога — заглушка/капча.
        const string block = """
            <html><body><div id="wrapper"><h1>Доступ ограничен</h1>
            <p>Проверка браузера…</p></div></body></html>
            """;

        Assert.False(Parsers.LooksLikeSite(block));
    }

    [Fact]
    public void LooksLikeSite_LoginInterstitial_False()
    {
        // Реальный ответ rezka.fi: 200, 24 КБ, «Вход на HDrezka», без каталога.
        const string login = """
            <html lang="ru"><head><title>Вход на HDrezka</title></head>
            <body><form><input name="login_name"></form></body></html>
            """;

        Assert.False(Parsers.LooksLikeSite(login));
    }

    [Fact]
    public void LooksLikeSite_EmptyOrGarbage_False()
    {
        Assert.False(Parsers.LooksLikeSite(null));
        Assert.False(Parsers.LooksLikeSite(""));
        Assert.False(Parsers.LooksLikeSite("not html at all"));
    }
}
