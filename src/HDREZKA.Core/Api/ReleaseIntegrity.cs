using System.Security.Cryptography;

namespace HDREZKA.Core.Api;

/// <summary>Проверка целостности файла обновления: SHA-256 из релиза GitHub.</summary>
public static class ReleaseIntegrity
{
    /// <summary>
    /// Приводит значение к нижнему hex без префикса: принимает "sha256:AB12…",
    /// "AB12…", "ab12…". Возвращает null, если это не 64-символьный hex.
    /// </summary>
    public static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();

        const string prefix = "sha256:";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[prefix.Length..].Trim();
        }

        if (text.Length != 64) return null;
        foreach (var ch in text)
        {
            if (!Uri.IsHexDigit(ch)) return null;
        }

        return text.ToLowerInvariant();
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool Matches(string filePath, string expectedSha256)
    {
        var expected = NormalizeSha256(expectedSha256);
        if (expected == null) return false;
        return string.Equals(ComputeSha256(filePath), expected, StringComparison.Ordinal);
    }
}
