using Microsoft.Data.Sqlite;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>报告提醒设置的存取：默认值、改完能存回来、旧的站会设置能接上。</summary>
public class ReportReminderSettingsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public ReportReminderSettingsTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:", "0.1.0-test");
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Get_ReturnsDefaultsWhenNothingStored()
    {
        var settings = _service.ReportRemindersGet().Data!;

        Assert.True(settings.Daily.Enabled);
        Assert.Equal("09:00", settings.Daily.Time);
        Assert.True(settings.Weekly.Enabled);
        Assert.Equal("17:00", settings.Weekly.Time);
        Assert.Equal(5, settings.Weekly.DayOfWeek);
        Assert.True(settings.Monthly.Enabled);
        Assert.Equal(31, settings.Monthly.DayOfMonth);
    }

    [Fact]
    public void Set_ThenGet_RoundTrips()
    {
        var saved = _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "08:30" },
            Weekly = new ReportReminderEntry { Enabled = true, Time = "18:00", DayOfWeek = 3 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "10:00", DayOfMonth = 5 }
        });

        Assert.True(saved.Ok);

        var settings = _service.ReportRemindersGet().Data!;
        Assert.False(settings.Daily.Enabled);
        Assert.Equal("08:30", settings.Daily.Time);
        Assert.Equal(3, settings.Weekly.DayOfWeek);
        Assert.Equal(5, settings.Monthly.DayOfMonth);
    }

    [Fact]
    public void Set_InvalidTime_ReturnsValidationFailed()
    {
        var result = _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = true, Time = "9:00" }
        });

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error?.Code);
        Assert.Contains("HH:mm", result.Error?.Message);
    }

    [Fact]
    public void LastFired_StartsEmptyAndStoresKey()
    {
        Assert.Null(_service.ReportReminderLastFired(ReportKind.Daily).Data);

        _service.ReportReminderMarkFired(ReportKind.Daily, "2026-11-08");

        Assert.Equal("2026-11-08", _service.ReportReminderLastFired(ReportKind.Daily).Data);
        Assert.Null(_service.ReportReminderLastFired(ReportKind.Weekly).Data);
    }

    /// <summary>老用户库里的 daily_review 设置要能接上：日报沿用原来的开关与时间。</summary>
    [Fact]
    public void Get_FallsBackToLegacyDailyReviewSetting()
    {
        TestDb.Execute(_connection, """
            INSERT INTO settings (key, value) VALUES ('daily_review', '{"Enabled":true,"Time":"07:45"}')
            """);

        var settings = _service.ReportRemindersGet().Data!;

        Assert.True(settings.Daily.Enabled);
        Assert.Equal("07:45", settings.Daily.Time);
    }

    /// <summary>老键的属性名可能是小写（老设计约定 enabled / time），一样要认。</summary>
    [Fact]
    public void Get_FallsBackToLegacySettingWithLowerCaseKeys()
    {
        TestDb.Execute(_connection, """
            INSERT INTO settings (key, value) VALUES ('daily_review', '{"enabled":true,"time":"06:30"}')
            """);

        var settings = _service.ReportRemindersGet().Data!;

        Assert.True(settings.Daily.Enabled);
        Assert.Equal("06:30", settings.Daily.Time);
    }

    /// <summary>老键缺 Enabled 字段时按默认值（启用）算，不能把关掉用户的日报。</summary>
    [Fact]
    public void Get_LegacySettingWithoutEnabled_KeepsDailyEnabled()
    {
        TestDb.Execute(_connection, """
            INSERT INTO settings (key, value) VALUES ('daily_review', '{"Time":"06:30"}')
            """);

        var settings = _service.ReportRemindersGet().Data!;

        Assert.True(settings.Daily.Enabled);
        Assert.Equal("06:30", settings.Daily.Time);
    }

    /// <summary>老键的时间不是 HH:mm 时退回默认时间：一条永远不会响的日报等于没设。</summary>
    [Fact]
    public void Get_LegacySettingWithInvalidTime_FallsBackToDefaultTime()
    {
        TestDb.Execute(_connection, """
            INSERT INTO settings (key, value) VALUES ('daily_review', '{"Enabled":true,"Time":"6:30"}')
            """);

        var settings = _service.ReportRemindersGet().Data!;

        Assert.Equal("09:00", settings.Daily.Time);
    }

    /// <summary>老键坏了（新键没写）时给默认值，设置页不能整个打不开。</summary>
    [Fact]
    public void Get_BrokenLegacySetting_FallsBackToDefaults()
    {
        TestDb.Execute(_connection, """
            INSERT INTO settings (key, value) VALUES ('daily_review', '"不是对象"')
            """);

        var settings = _service.ReportRemindersGet().Data!;

        Assert.True(settings.Daily.Enabled);
        Assert.Equal("09:00", settings.Daily.Time);
    }

    /// <summary>存了新设置之后，老键仍然留着 —— 回退路径随时可能用到。</summary>
    [Fact]
    public void Set_KeepsLegacyKeyIntact()
    {
        TestDb.Execute(_connection, """
            INSERT INTO settings (key, value) VALUES ('daily_review', '{"Enabled":true,"Time":"07:45"}')
            """);

        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "08:30" }
        });

        var legacy = TestDb.Scalar(_connection, "SELECT value FROM settings WHERE key = 'daily_review'");
        Assert.Equal("""{"Enabled":true,"Time":"07:45"}""", legacy);
    }

    /// <summary>去重键不能是空串：空键会让「已经报过了」永远判不出来。</summary>
    [Fact]
    public void MarkFired_EmptyKey_ReturnsValidationFailed()
    {
        var result = _service.ReportReminderMarkFired(ReportKind.Daily, " ");

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error?.Code);
    }
}
