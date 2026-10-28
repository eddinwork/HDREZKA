using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

public class CookieVaultTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "hdrezka-cookies-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        const string json = """[{"Name":"dle_user_id","Value":"42"}]""";

        CookieVault.Write(_path, json);
        var result = CookieVault.Read(_path);

        Assert.Equal(json, result.Json);
        Assert.False(result.WasPlaintext);
    }

    [Fact]
    public void FileOnDisk_IsNotPlainJson()
    {
        CookieVault.Write(_path, """[{"Name":"a","Value":"b"}]""");

        var bytes = File.ReadAllBytes(_path);
        Assert.NotEqual((byte)'[', bytes[0]);
    }

    [Fact]
    public void LegacyPlaintext_IsReadAndReported()
    {
        // Файлы, сохранённые до 1.8.0, лежали открытым JSON.
        const string json = """[{"Name":"dle_user_id","Value":"42"}]""";
        File.WriteAllText(_path, json);

        var result = CookieVault.Read(_path);

        Assert.Equal(json, result.Json);
        Assert.True(result.WasPlaintext);
    }

    [Fact]
    public void GarbageFile_Throws()
    {
        File.WriteAllBytes(_path, [0x01, 0x02, 0x03, 0x04, 0x05]);

        Assert.ThrowsAny<Exception>(() => CookieVault.Read(_path));
    }
}
