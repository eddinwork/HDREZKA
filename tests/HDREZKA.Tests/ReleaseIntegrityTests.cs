using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

public class ReleaseIntegrityTests
{
    // SHA-256("hello")
    private const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

    [Theory]
    [InlineData("sha256:" + HelloSha256)]
    [InlineData(HelloSha256)]
    [InlineData("  " + HelloSha256 + "  ")]
    public void NormalizeSha256_AcceptsCommonFormats(string value)
    {
        Assert.Equal(HelloSha256, ReleaseIntegrity.NormalizeSha256(value));
    }

    [Fact]
    public void NormalizeSha256_IsCaseInsensitive()
    {
        Assert.Equal(HelloSha256, ReleaseIntegrity.NormalizeSha256(HelloSha256.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:")]
    [InlineData("not-a-hash")]
    [InlineData("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b982")] // 63 chars
    [InlineData("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824z")] // 65/not hex
    public void NormalizeSha256_RejectsGarbage(string? value)
    {
        Assert.Null(ReleaseIntegrity.NormalizeSha256(value));
    }

    [Fact]
    public void ComputeSha256_MatchesKnownHash()
    {
        var path = Path.Combine(Path.GetTempPath(), "hdrezka-sha-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "hello");
            Assert.Equal(HelloSha256, ReleaseIntegrity.ComputeSha256(path));
            Assert.True(ReleaseIntegrity.Matches(path, "sha256:" + HelloSha256));
            Assert.False(ReleaseIntegrity.Matches(path, new string('0', 64)));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
