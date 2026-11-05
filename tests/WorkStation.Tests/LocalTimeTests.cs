using WorkStation.Core.Time;

namespace WorkStation.Tests;

public class LocalTimeTests
{
    [Fact]
    public void NowIso_MatchesIso8601UtcWithMilliseconds()
    {
        var value = LocalTime.NowIso();
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", value);
    }

    [Fact]
    public void ToLocalDate_OutputsLocalCalendarDay()
    {
        var value = LocalTime.ToLocalDate(new DateTime(2026, 11, 5, 14, 30, 0, DateTimeKind.Local));
        Assert.Equal("2026-11-05", value);
    }

    [Fact]
    public void ToLocalDateTime_OutputsLocalWallClockString()
    {
        var value = LocalTime.ToLocalDateTime(new DateTime(2026, 11, 5, 14, 30, 0, DateTimeKind.Local));
        Assert.Equal("2026-11-05T14:30", value);
    }

    [Fact]
    public void AsLocal_UtcInput_IsConvertedToLocal()
    {
        var utc = new DateTime(2026, 11, 5, 4, 5, 0, DateTimeKind.Utc);
        var local = LocalTime.AsLocal(utc);
        Assert.Equal(DateTimeKind.Local, local.Kind);
        Assert.Equal(utc, local.ToUniversalTime());
    }

    [Fact]
    public void AsLocal_UnspecifiedInput_IsTreatedAsLocalWallClock()
    {
        var value = new DateTime(2026, 11, 5, 14, 30, 0, DateTimeKind.Unspecified);
        var local = LocalTime.AsLocal(value);
        Assert.Equal(DateTimeKind.Local, local.Kind);
        Assert.Equal(value.Ticks, local.Ticks);
    }

    [Theory]
    [InlineData("2026-11-05T14:30", true)]
    [InlineData("2026-01-01T00:00", true)]
    [InlineData("2026-11-05", false)]
    [InlineData("2026-11-05T14:30:00", false)]
    [InlineData("2026-11-05 14:30", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLocalDateTime_OnlyAcceptsWallClockFormat(string? value, bool expected)
    {
        Assert.Equal(expected, LocalTime.IsLocalDateTime(value));
    }

    [Fact]
    public void IsLocalDateTime_DoesNotValidateCalendarDate()
    {
        // 冻结契约：只校验格式，2026-99-99T99:99 也会通过
        Assert.True(LocalTime.IsLocalDateTime("2026-99-99T99:99"));
    }
}
