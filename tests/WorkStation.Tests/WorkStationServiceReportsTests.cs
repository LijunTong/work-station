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

        var regenerated = _service.ReportsRegenerate(created.Id).Data!;

        Assert.Equal("我写的补充", regenerated.ManualText);
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
        var stats = _service.ReportsStats(ReportKind.Daily).Data!;

        Assert.Equal(0, stats.Completed);
        Assert.Equal(0, stats.CarriedOver);
        Assert.Equal(0, stats.Total);
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
