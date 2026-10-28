using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

public class SiteDatesTests
{
    private static readonly DateTime Today = new(2026, 10, 28);

    [Theory]
    [InlineData("12.09.2026", 2026, 9, 12)]
    [InlineData("12.09.2026 15:30", 2026, 9, 12)]
    [InlineData("2026-09-12", 2026, 9, 12)]
    [InlineData("12/09/2026", 2026, 9, 12)]
    public void ParseExactFormats(string input, int year, int month, int day)
    {
        var parsed = SiteDates.ParseRelativeOrExact(input, Today);

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(year, month, day).Date, parsed!.Value.Date);
    }

    [Theory]
    [InlineData("сегодня")]
    [InlineData("Сегодня, 12:00")]
    [InlineData("today")]
    [InlineData("сьогодні")]
    public void RelativeToday(string input)
    {
        Assert.Equal(Today, SiteDates.ParseRelativeOrExact(input, Today)!.Value.Date);
    }

    [Theory]
    [InlineData("вчера")]
    [InlineData("Вчера в 20:15")]
    [InlineData("yesterday")]
    [InlineData("вчора")]
    public void RelativeYesterday(string input)
    {
        Assert.Equal(Today.AddDays(-1), SiteDates.ParseRelativeOrExact(input, Today)!.Value.Date);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("не дата")]
    public void Garbage_ReturnsNull(string? input)
    {
        Assert.Null(SiteDates.ParseRelativeOrExact(input, Today));
    }
}
