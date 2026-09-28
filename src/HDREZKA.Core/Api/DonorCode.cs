using System.Security.Cryptography;
using System.Text;

namespace HDREZKA.Core.Api;

/// <summary>
/// Donor perk codes, NOT DRM: anyone can rebuild the client without the
/// check, and shared codes can't be revoked. Format XXXX-XXXX-XXXX
/// (8 payload chars + 4 checksum chars). Random codes don't validate.
/// </summary>
public static class DonorCode
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    // Embedded salt. Theater-grade: visible in open source by design.
    internal const string Salt = "HDREZKA-DONOR-v1";

    public static string Generate()
    {
        var payload = new char[8];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return Format(new string(payload) + Checksum(new string(payload)));
    }

    public static bool Validate(string? code)
    {
        var clean = Normalize(code);
        if (clean == null || clean.Length != 12) return false;
        foreach (var ch in clean)
        {
            if (!Alphabet.Contains(ch)) return false;
        }

        return Checksum(clean[..8]) == clean[8..];
    }

    internal static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var sb = new StringBuilder();
        foreach (var ch in code.Trim().ToUpperInvariant())
        {
            if (ch == '-' || ch == ' ') continue;
            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string Format(string clean) => $"{clean[..4]}-{clean.Substring(4, 4)}-{clean.Substring(8, 4)}";

    private static string Checksum(string payload8)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Salt + payload8));
        var sb = new StringBuilder(4);
        for (var i = 0; i < 4; i++)
        {
            sb.Append(Alphabet[hash[i] % Alphabet.Length]);
        }

        return sb.ToString();
    }
}
