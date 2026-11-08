using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>设置页的三类报告提醒：既要能读回来，也要能存回去。</summary>
public class SettingsViewModelReportReminderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public SettingsViewModelReportReminderTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    // 构造函数要读系统字体，必须跑在 Avalonia 宿主里
    private SettingsViewModel NewViewModel() =>
        new(_service, _ => { }, _ => { }, (_, _) => { });

    [AvaloniaFact]
    public void Load_ShowsStoredReminderSettings()
    {
        _service.ReportRemindersSet(new ReportReminderSettings
        {
            Daily = new ReportReminderEntry { Enabled = false, Time = "08:30" },
            Weekly = new ReportReminderEntry { Enabled = true, Time = "18:00", DayOfWeek = 3 },
            Monthly = new ReportReminderEntry { Enabled = false, Time = "10:00", DayOfMonth = 5 }
        });

        var settings = NewViewModel();
        settings.Reload();

        Assert.False(settings.ReportDailyEnabled);
        Assert.Equal("08:30", settings.ReportDailyTime);
        Assert.Equal("18:00", settings.ReportWeeklyTime);
        Assert.Equal(3, settings.SelectedReportWeekday?.Value);
        Assert.Equal(5, settings.SelectedReportMonthDay?.Value);
    }

    [AvaloniaFact]
    public void Change_StoresReminderSettings()
    {
        var settings = NewViewModel();
        settings.Reload();

        settings.ReportDailyEnabled = false;
        settings.ReportDailyTime = "07:15";
        settings.SelectedReportWeekday = settings.ReportWeekdayOptions.First(o => o.Value == 2);
        settings.SelectedReportMonthDay = settings.ReportMonthDayOptions.First(o => o.Value == 20);

        var saved = _service.ReportRemindersGet().Data!;
        Assert.False(saved.Daily.Enabled);
        Assert.Equal("07:15", saved.Daily.Time);
        Assert.Equal(2, saved.Weekly.DayOfWeek);
        Assert.Equal(20, saved.Monthly.DayOfMonth);
    }

    [AvaloniaFact]
    public void Options_CoverEveryWeekdayAndDayOfMonth()
    {
        var settings = NewViewModel();

        Assert.Equal(7, settings.ReportWeekdayOptions.Count);
        Assert.Equal(31, settings.ReportMonthDayOptions.Count);
        Assert.Contains(settings.ReportTimeOptions, t => t == "09:00");
    }
}
