using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

public class MirrorCatalogTests
{
    private const string CatalogJson = """
        {
          "mirrors": [
            "https://hdrzk.org/",
            { "url": "https://rezka-ua.net", "region": "ua", "note": "ukraine" },
            { "url": "https://rezka-kz.tv/", "region": "KZ" },
            { "url": "ftp://not-http.example/", "region": "ALL" },
            "definitely not a url"
          ]
        }
        """;

    [Fact]
    public void Parse_AcceptsStringsAndObjects_SkipsGarbage()
    {
        var entries = MirrorCatalog.Parse(CatalogJson);

        Assert.Equal(3, entries.Count);
        Assert.Equal("https://hdrzk.org/", entries[0].Url);
        Assert.Equal(MirrorCatalog.AllRegions, entries[0].Region);

        // Регион нормализуется в верхний регистр, слэш добавляется.
        Assert.Equal("https://rezka-ua.net/", entries[1].Url);
        Assert.Equal("UA", entries[1].Region);
        Assert.Equal("KZ", entries[2].Region);
    }

    [Fact]
    public void Parse_BrokenJson_ReturnsEmpty()
    {
        Assert.Empty(MirrorCatalog.Parse("{ not json"));
        Assert.Empty(MirrorCatalog.Parse(null));
        Assert.Empty(MirrorCatalog.Parse(""));
    }

    [Fact]
    public void Order_PutsUserRegionFirst_ThenAll()
    {
        var entries = MirrorCatalog.Parse(CatalogJson);

        var ordered = MirrorCatalog.Order(entries, currentMirror: "https://hdrzk.org/", userRegion: "UA");

        Assert.Equal(["https://rezka-ua.net/", "https://hdrzk.org/", "https://rezka-kz.tv/"], ordered);
    }

    [Fact]
    public void Order_WithoutRegion_KeepsCatalogOrder()
    {
        var entries = MirrorCatalog.Parse(CatalogJson);

        var ordered = MirrorCatalog.Order(entries, currentMirror: null, userRegion: null);

        Assert.Equal("https://hdrzk.org/", ordered[0]);
        Assert.Equal(3, ordered.Count);
    }

    [Fact]
    public void Order_CustomMirrorNotInCatalog_GoesFirst()
    {
        var entries = MirrorCatalog.Parse(CatalogJson);

        var ordered = MirrorCatalog.Order(entries, currentMirror: "https://my-vpn-mirror.example/", userRegion: "UA");

        Assert.Equal("https://my-vpn-mirror.example/", ordered[0]);
        Assert.Contains("https://rezka-ua.net/", ordered);
    }

    [Fact]
    public void Order_DeduplicatesByHost()
    {
        MirrorEntry[] entries =
        [
            new("https://dup.example/", MirrorCatalog.AllRegions),
            new("https://dup.example/other/", MirrorCatalog.AllRegions),
        ];

        var ordered = MirrorCatalog.Order(entries);

        Assert.Single(ordered);
    }

    [Fact]
    public void SameMirror_IgnoresSchemeCaseAndTrailingSlash()
    {
        Assert.True(MirrorCatalog.SameMirror("https://HDRZK.org", "https://hdrzk.org/"));
        Assert.False(MirrorCatalog.SameMirror("https://a.example/", "https://b.example/"));
    }
}
