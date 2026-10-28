using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

public class AuthenticodeVerifierTests
{
    [Fact]
    public void MissingOrGarbageFile_IsNotTrusted()
    {
        Assert.False(AuthenticodeVerifier.IsTrusted(null));
        Assert.False(AuthenticodeVerifier.IsTrusted(Path.Combine(Path.GetTempPath(), "no-such-file.exe")));

        var path = Path.Combine(Path.GetTempPath(), "hdrezka-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllText(path, "definitely not a signed PE");
            Assert.False(AuthenticodeVerifier.IsTrusted(path));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void SignedSystemBinary_IsTrusted()
    {
        // kernel32.dll подписан Microsoft (встроенная подпись). notepad/cmd
        // подписаны через каталог, поэтому WTD_CHOICE_FILE их не подтверждает.
        var signed = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
        if (!File.Exists(signed)) return;

        Assert.True(AuthenticodeVerifier.IsTrusted(signed));
    }
}
