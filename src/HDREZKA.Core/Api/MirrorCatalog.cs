using System.Text.Json;

namespace HDREZKA.Core.Api;

/// <summary>Одно зеркало из mirrors.json: URL + регион (ISO 3166-1 alpha-2) или "ALL".</summary>
public sealed record MirrorEntry(string Url, string Region = MirrorCatalog.AllRegions, string? Note = null);

/// <summary>
/// Список зеркал и порядок их проверки. Вынесено в Core без WinRT-зависимостей,
/// чтобы порядок (регион → все → остальные → кастомное зеркало пользователя)
/// можно было покрыть обычными юнит-тестами.
/// </summary>
public static class MirrorCatalog
{
    public const string AllRegions = "ALL";

    /// <summary>
    /// Разбирает mirrors.json. Поддерживает как массив строк, так и массив
    /// объектов { "url": "...", "region": "UA", "note": "..." }, а также
    /// обёртку { "mirrors": [...] }. Невалидные записи пропускаются.
    /// </summary>
    public static IReadOnlyList<MirrorEntry> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (doc)
        {
            var root = doc.RootElement;
            JsonElement array;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!root.TryGetProperty("mirrors", out array) || array.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else
            {
                return [];
            }

            var result = new List<MirrorEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in array.EnumerateArray())
            {
                string? url = null;
                var region = AllRegions;
                string? note = null;

                if (item.ValueKind == JsonValueKind.String)
                {
                    url = item.GetString();
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
                    {
                        url = urlEl.GetString();
                    }

                    if (item.TryGetProperty("region", out var regionEl) && regionEl.ValueKind == JsonValueKind.String)
                    {
                        var parsed = (regionEl.GetString() ?? "").Trim();
                        if (parsed.Length > 0) region = parsed.ToUpperInvariant();
                    }

                    if (item.TryGetProperty("note", out var noteEl) && noteEl.ValueKind == JsonValueKind.String)
                    {
                        note = noteEl.GetString();
                    }
                }

                var normalized = NormalizeUrl(url);
                if (normalized == null) continue;
                if (!seen.Add(Host(normalized) ?? normalized)) continue;
                result.Add(new MirrorEntry(normalized, region, note));
            }

            return result;
        }
    }

    /// <summary>
    /// Порядок проверки зеркал:
    /// 1) кастомное зеркало пользователя, если его нет в каталоге (не теряем рабочую настройку);
    /// 2) зеркала региона пользователя (например, UA) — там гео-блокировки обходятся;
    /// 3) зеркала "ALL";
    /// 4) остальные в порядке каталога;
    /// 5) текущее зеркало, если оно ещё не попало в список.
    /// Дубликаты отсекаются по хосту, порядок сохраняется.
    /// </summary>
    public static IReadOnlyList<string> Order(
        IEnumerable<MirrorEntry>? entries,
        string? currentMirror = null,
        string? userRegion = null)
    {
        var list = (entries ?? []).Where(e => NormalizeUrl(e.Url) != null).ToList();
        var catalogHosts = new HashSet<string>(
            list.Select(e => Host(NormalizeUrl(e.Url)!)).Where(h => h != null)!,
            StringComparer.OrdinalIgnoreCase);

        var result = new List<string>();
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? url)
        {
            var normalized = NormalizeUrl(url);
            if (normalized == null) return;
            var host = Host(normalized) ?? normalized;
            if (!seenHosts.Add(host)) return;
            result.Add(normalized);
        }

        var current = NormalizeUrl(currentMirror);
        if (current != null)
        {
            var host = Host(current);
            if (host == null || !catalogHosts.Contains(host))
            {
                Add(current); // кастомное зеркало — приоритет
            }
        }

        var region = string.IsNullOrWhiteSpace(userRegion) ? null : userRegion.Trim().ToUpperInvariant();

        if (region != null)
        {
            foreach (var entry in list.Where(e => Region(e) == region)) Add(entry.Url);
        }

        foreach (var entry in list.Where(e => Region(e) == AllRegions)) Add(entry.Url);

        foreach (var entry in list) Add(entry.Url);

        Add(current);
        return result;
    }

    /// <summary>http(s) URL с завершающим слешем, либо null для мусора.</summary>
    public static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrEmpty(uri.Host)) return null;
        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }

    public static bool SameMirror(string? a, string? b)
    {
        var left = Host(NormalizeUrl(a));
        var right = Host(NormalizeUrl(b));
        return left != null && right != null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string Region(MirrorEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Region) ? AllRegions : entry.Region.Trim().ToUpperInvariant();

    private static string? Host(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;
    }
}
