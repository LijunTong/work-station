using WorkStation.Core;

namespace WorkStation.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData(0, "0%")]
    [InlineData(45.4, "45%")]
    [InlineData(45.5, "46%")]
    [InlineData(100, "100%")]
    [InlineData(-10, "0%")]
    [InlineData(150, "100%")]
    public void FormatPercent_ClampsToRangeAndRoundsHalfAwayFromZero(double value, string expected)
    {
        Assert.Equal(expected, Formatting.FormatPercent(value));
    }

    [Fact]
    public void FormatPercent_NaN_ReturnsZero()
    {
        Assert.Equal("0%", Formatting.FormatPercent(double.NaN));
    }

    [Fact]
    public void FormatHours_Null_ReturnsDash()
    {
        Assert.Equal("—", Formatting.FormatHours(null));
    }

    [Theory]
    [InlineData(0, "0h")]
    [InlineData(1.25, "1.3h")]
    [InlineData(2.4, "2.4h")]
    public void FormatHours_KeepsOneDecimal(double value, string expected)
    {
        Assert.Equal(expected, Formatting.FormatHours(value));
    }

    [Fact]
    public void FormatHours_Negative_ClampsToZero()
    {
        Assert.Equal("0h", Formatting.FormatHours(-2.5));
    }

    [Fact]
    public void FormatHours_NaN_ReturnsDash()
    {
        Assert.Equal("—", Formatting.FormatHours(double.NaN));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    [InlineData("2026-11-05T14:30", "11-05 14:30")]
    [InlineData("2026-11-05", "11-05")]
    public void FormatLocalDateTime_TruncatesByLength(string? value, string expected)
    {
        Assert.Equal(expected, Formatting.FormatLocalDateTime(value));
    }

    [Fact]
    public void IsOverdue_OnlyStrictlyEarlierCounts()
    {
        Assert.True(Formatting.IsOverdue("2026-11-05T09:00", "2026-11-05T10:00"));
        Assert.False(Formatting.IsOverdue("2026-11-05T10:00", "2026-11-05T10:00"));
        Assert.False(Formatting.IsOverdue("2026-11-05T11:00", "2026-11-05T10:00"));
    }

    [Fact]
    public void IsOverdue_NoDueAt_IsNeverOverdue()
    {
        Assert.False(Formatting.IsOverdue(null, "2026-11-05T10:00"));
        Assert.False(Formatting.IsOverdue("", "2026-11-05T10:00"));
    }
}
