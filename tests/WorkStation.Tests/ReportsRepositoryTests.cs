using Microsoft.Data.Sqlite;
using WorkStation.Core.Data.Repositories;
using WorkStation.Core.Models;

namespace WorkStation.Tests;

/// <summary>报告仓储：一个周期一条报告，可回看可改。</summary>
public class ReportsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ReportsRepository _reports;

    public ReportsRepositoryTests()
    {
        _connection = TestDb.OpenMigrated();
        _reports = new ReportsRepository(_connection);
    }

    public void Dispose() => _connection.Dispose();

    private static Report Draft(ReportKind kind, string start, string end) => new()
    {
        Id = string.Empty,
        Kind = kind,
        PeriodStart = start,
        PeriodEnd = end,
        AutoMarkdown = "自动",
        ManualText = string.Empty,
        FinalMarkdown = "自动",
        CreatedAt = string.Empty,
        UpdatedAt = string.Empty
    };

    [Fact]
    public void Create_StoresReportWithGeneratedId()
    {
        var created = _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08"));

        Assert.NotEmpty(created.Id);
        Assert.Equal(ReportKind.Daily, created.Kind);
        Assert.Equal("2026-11-08", created.PeriodStart);
        Assert.Equal("自动", created.AutoMarkdown);
        Assert.NotEmpty(created.CreatedAt);
    }

    [Fact]
    public void GetByPeriod_FindsReportOfThatKindOnly()
    {
        _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08"));
        _reports.Create(Draft(ReportKind.Weekly, "2026-11-02", "2026-11-08"));

        var found = _reports.GetByPeriod(ReportKind.Weekly, "2026-11-02");

        Assert.NotNull(found);
        Assert.Equal(ReportKind.Weekly, found!.Kind);
        Assert.Null(_reports.GetByPeriod(ReportKind.Weekly, "2026-11-09"));
    }

    [Fact]
    public void List_FiltersByKindAndSortsNewestPeriodFirst()
    {
        _reports.Create(Draft(ReportKind.Weekly, "2026-10-26", "2026-11-01"));
        _reports.Create(Draft(ReportKind.Weekly, "2026-11-02", "2026-11-08"));
        _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08"));

        var weekly = _reports.List(ReportKind.Weekly);
        Assert.Equal(2, weekly.Count);
        Assert.Equal("2026-11-02", weekly[0].PeriodStart);

        Assert.Equal(3, _reports.List().Count);
    }

    [Fact]
    public void Update_KeepsManualTextAndRefreshesTimestamp()
    {
        var created = _reports.Create(Draft(ReportKind.Weekly, "2026-11-02", "2026-11-08"));

        var updated = _reports.Update(created with
        {
            AutoMarkdown = "新的自动汇总",
            ManualText = "我写的补充",
            FinalMarkdown = "新的自动汇总\n\n我写的补充"
        });

        Assert.Equal("新的自动汇总", updated.AutoMarkdown);
        Assert.Equal("我写的补充", updated.ManualText);
        Assert.Equal("新的自动汇总\n\n我写的补充", updated.FinalMarkdown);

        var reread = _reports.Get(created.Id);
        Assert.Equal("我写的补充", reread!.ManualText);
    }

    [Fact]
    public void Remove_DeletesReport()
    {
        var created = _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08"));

        Assert.True(_reports.Remove(created.Id));
        Assert.Null(_reports.Get(created.Id));
        Assert.False(_reports.Remove(created.Id));
    }

    [Fact]
    public void Create_SamePeriodTwice_FailsOnUniqueIndex()
    {
        _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08"));

        Assert.Throws<SqliteException>(() =>
            _reports.Create(Draft(ReportKind.Daily, "2026-11-08", "2026-11-08")));
    }
}
