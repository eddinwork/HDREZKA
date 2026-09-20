using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

public class StreamDecryptorTests
{
    [Fact]
    public void PassThrough_WhenNotEncrypted()
    {
        const string plain = "[720p]https://cdn.example.org/video.mp4 or https://cdn2.example.org/video.mp4";
        Assert.Equal(plain, StreamDecryptor.Decrypt(plain));
    }

    [Fact]
    public void Decrypts_Base64_WithHashPrefix()
    {
        // base64("https://test/123.mp4")
        var encrypted = "#!" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("https://test/123.mp4"));
        Assert.Equal("https://test/123.mp4", StreamDecryptor.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypts_WithTrashMarkers()
    {
        // plain "https://a/b.m3u8"; b64 split in half with a "//_//" + trash-chunk insertion
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("https://a/b.m3u8"));
        var trash = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("@#"));
        var mid = b64.Length / 2;
        var encrypted = "#!" + b64[..mid] + "//_//" + trash + b64[mid..];
        var result = StreamDecryptor.Decrypt(encrypted);
        Assert.Equal("https://a/b.m3u8", result);
    }
}
