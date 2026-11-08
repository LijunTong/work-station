using Microsoft.Data.Sqlite;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Services;
using WorkStation.Core.Time;

namespace WorkStation.Tests;

/// <summary>
/// 报告服务层：幂等生成、重新汇总不覆盖人工补充、失败一律转 Result。
/// </summary>
public class WorkStationServiceReportsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public WorkStationServiceReportsTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:", "0.1.0-test");
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void ReportsEnsure_CreatesReportWithAutoSummary()
    {
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "写周报",
            DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(18))
        });

        var result = _service.ReportsEnsure(ReportKind.Daily, LocalTime.ToLocalDate(DateTime.Today));

        Assert.True(result.Ok);
        var report = result.Data!;
        Assert.Equal(ReportKind.Daily, report.Kind);
        Assert.Contains("### 统计", report.AutoMarkdown);
        Assert.Equal(report.AutoMarkdown, report.FinalMarkdown);
    }

    [Fact]
    public void ReportsEnsure_IsIdempotent()
    {
        var start = LocalTime.ToLocalDate(DateTime.Today);

        var first = _service.ReportsEnsure(ReportKind.Daily, start).Data!;
        var second = _service.ReportsEnsure(ReportKind.Daily, start).Data!;

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_service.ReportsList(ReportKind.Daily).Data!);
    }

    [Fact]
    public void ReportsEnsure_KeepsManualTextOfExistingReport()
    {
        var start = LocalTime.ToLocalDate(DateTime.Today);
        var created = _service.ReportsEnsure(ReportKind.Daily, start).Data!;
        _service.ReportsUpdate(created.Id, "我写的补充");

        var again = _service.ReportsEnsure(ReportKind.Daily, start).Data!;

        Assert.Equal("我写的补充", again.ManualText);
    }

    [Fact]
    public void ReportsUpdate_SavesManualTextAndRebuildsFinalMarkdown()
    {
        var start = LocalTime.ToLocalDate(DateTime.Today);
        var created = _service.ReportsEnsure(ReportKind.Daily, start).Data!;

        var updated = _service.ReportsUpdate(created.Id, "我写的补充").Data!;

        Assert.Equal("我写的补充", updated.ManualText);
        Assert.EndsWith("我写的补充", updated.FinalMarkdown);
        Assert.Contains(created.AutoMarkdown, updated.FinalMarkdown);
    }

    [Fact]
    public void ReportsRegenerate_KeepsManualTextAndRefreshesAutoMarkdown()
    {
        var start = LocalTime.ToLocalDate(DateTime.Today);
        var created = _service.ReportsEnsure(ReportKind.Daily, start).Data!;
        _service.ReportsUpdate(created.Id, "我写的补充");

        // 汇总之后又加了一件事：重新汇总必须把它算进来。
        // 少了这一步，「没重算、原样返回旧记录」的实现照样能通过断言。
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "重新汇总之后才加的事",
            DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(18))
        });

        var regenerated = _service.ReportsRegenerate(created.Id).Data!;

        Assert.Equal("我写的补充", regenerated.ManualText);
        Assert.Contains("重新汇总之后才加的事", regenerated.AutoMarkdown);
        Assert.Contains("### 统计", regenerated.AutoMarkdown);
        Assert.EndsWith("我写的补充", regenerated.FinalMarkdown);
    }

    [Fact]
    public void ReportsUpdate_UnknownId_ReturnsFailure()
    {
        var result = _service.ReportsUpdate("不存在", "随便写点");

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error?.Message));
    }

    [Fact]
    public void ReportsStats_ReturnsCurrentPeriodNumbers()
    {
        // 上周到期的不算今天的日报 —— 统计必须按当前周期取数，
        // 少了这条，用错周期（或者干脆统计全库）的实现照样能过。
        _service.TasksCreate(new CreateTaskInput
        {
            Title = "上周到期的",
            DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddDays(-7).AddHours(18))
        });

        _service.TasksCreate(new CreateTaskInput
        {
            Title = "今天到期的",
            DueAt = LocalTime.ToLocalDateTime(DateTime.Today.AddHours(18))
        });

        var stats = _service.ReportsStats(ReportKind.Daily).Data!;

        Assert.Equal(0, stats.Completed);
        Assert.Equal(1, stats.CarriedOver);
        Assert.Equal(1, stats.Total);
        // Overdue 不断言：到期时刻过了没有取决于测试跑在几点
    }

    [Fact]
    public void ReportsList_EmptyAtStart()
    {
        Assert.Empty(_service.ReportsList().Data!);
    }

    [Fact]
    public void ReportsEnsure_InvalidPeriodStart_ReturnsValidationFailure()
    {
        var result = _service.ReportsEnsure(ReportKind.Daily, "2026/11/08");

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error?.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error?.Message));
    }
}
