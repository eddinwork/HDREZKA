using System.Text;

namespace HDREZKA.Core.Api;

/// <summary>
/// Port of the stream url decryption used by HDrezka clients:
/// trash-symbols are stripped around "//_//" markers, then the result is base64-decoded.
/// </summary>
public static class StreamDecryptor
{
    private const string Marker = "//_//";

    private static readonly string[] Trash;

    static StreamDecryptor()
    {
        var symbols = new[] { "@", "#", "!", "^", "$" };
        var combos = new List<string>();
        foreach (var len in new[] { 2, 3 })
        {
            foreach (var combo in CartesianProduct(symbols, len))
            {
                combos.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(combo)));
            }
        }

        Trash = combos.ToArray();
    }

    private static IEnumerable<string> CartesianProduct(string[] items, int len)
    {
        if (len == 1)
        {
            foreach (var i in items) yield return i;
            yield break;
        }

        foreach (var prefix in CartesianProduct(items, len - 1))
        {
            foreach (var item in items)
            {
                yield return prefix + item;
            }
        }
    }

    public static string Decrypt(string encrypted)
    {
        if (!encrypted.StartsWith("#")) return encrypted;

        var cache = new Dictionary<string, string>();
        var decoded = DecryptRecursive(encrypted[2..], cache);
        return Base64Decode(decoded);
    }

    private static string DecryptRecursive(string input, Dictionary<string, string> cache)
    {
        if (cache.TryGetValue(input, out var cached)) return cached;

        var best = input;
        var found = false;

        foreach (var index in IndicesOf(input, Marker))
        {
            var restStart = Math.Min(index + Marker.Length, input.Length);
            var rest = input[restStart..];

            // divide at first occurrence of '/' or '=' (inclusive)
            var pos = rest.IndexOf('/');
            var eq = rest.IndexOf('=');
            if (eq >= 0 && (pos < 0 || eq < pos)) pos = eq;

            string before, after;
            if (pos >= 0)
            {
                before = rest[..(pos + 1)];
                after = rest[(pos + 1)..];
            }
            else
            {
                before = rest;
                after = string.Empty;
            }

            var candidateInput = input[..index] + ClearTrash(before) + after;
            var candidate = DecryptRecursive(candidateInput, cache);

            if (!found || candidate.Length < best.Length)
            {
                best = candidate;
                found = true;
            }
        }

        cache[input] = best;
        return best;
    }

    private static string ClearTrash(string s)
    {
        foreach (var t in Trash)
        {
            if (s.Contains(t, StringComparison.Ordinal))
            {
                s = s.Replace(t, string.Empty, StringComparison.Ordinal);
            }
        }

        return s;
    }

    private static IEnumerable<int> IndicesOf(string s, string substr)
    {
        var start = 0;
        while (true)
        {
            var idx = s.IndexOf(substr, start, StringComparison.Ordinal);
            if (idx < 0) yield break;
            yield return idx;
            start = idx + 1;
        }
    }

    private static string Base64Decode(string s)
    {
        try
        {
            s = s.Trim();
            var pad = s.Length % 4;
            if (pad == 2) s += "==";
            else if (pad == 3) s += "=";

            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
        catch
        {
            return string.Empty;
        }
    }
}
