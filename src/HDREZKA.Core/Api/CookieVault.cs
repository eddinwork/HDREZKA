using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace HDREZKA.Core.Api;

/// <summary>
/// Хранилище cookies сессии. Файл cookies — это фактически токен аккаунта,
/// поэтому на диске он лежит не открытым JSON, а зашифрованным через DPAPI
/// (CurrentUser): другой пользователь ПК прочитать его не сможет.
/// Старые plaintext-файлы читаются и перешифровываются при следующем сохранении.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CookieVault
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HDREZKA.Cookies.v1");

    /// <summary>Результат чтения: JSON + признак, что файл был в старом открытом формате.</summary>
    public readonly record struct ReadResult(string Json, bool WasPlaintext);

    public static void Write(string path, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Атомарно: недописанный файл не должен остаться вместо валидного.
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, encrypted);
        File.Move(tmp, path, overwrite: true);
    }

    public static ReadResult Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) return new ReadResult("", false);

        // Старый формат: обычный JSON-массив/объект.
        var first = FirstMeaningfulByte(bytes);
        if (first is (byte)'[' or (byte)'{')
        {
            return new ReadResult(Encoding.UTF8.GetString(bytes), true);
        }

        var decrypted = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return new ReadResult(Encoding.UTF8.GetString(decrypted), false);
    }

    private static byte FirstMeaningfulByte(byte[] bytes)
    {
        foreach (var b in bytes)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
            return b;
        }

        return 0;
    }
}
