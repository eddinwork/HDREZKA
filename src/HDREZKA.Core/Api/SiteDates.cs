using System.Globalization;

namespace HDREZKA.Core.Api;

/// <summary>
/// Даты «Досмотреть» с сайта: "12.09.2026", "сегодня", "вчера", "сьогодні".
/// Вынесено в Core, чтобы форматы были покрыты тестами (в UI это была
/// дублирующая логика внутри страницы).
/// </summary>
public static class SiteDates
{
    private static readonly string[] Formats =
    [
        "dd.MM.yyyy HH:mm", "dd.MM.yyyy H:mm", "dd.MM.yyyy",
        "dd-MM-yyyy HH:mm", "dd-MM-yyyy",
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
        "dd/MM/yyyy",
    ];

    public static DateTime? ParseRelativeOrExact(string? date, DateTime? today = null)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;

        var text = date.Trim();
        var day = (today ?? DateTime.Today).Date;

        if (StartsWithAny(text, "сегодня", "today", "сьогодні")) return day;
        if (StartsWithAny(text, "вчера", "yesterday", "вчора")) return day.AddDays(-1);

        if (DateTime.TryParseExact(text, Formats,
                CultureInfo.GetCultureInfo("ru-RU"),
                DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateTime.TryParse(text, out var generic) ? generic : null;
    }

    private static bool StartsWithAny(string text, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
