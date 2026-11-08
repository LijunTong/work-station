using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using WorkStation.App.ViewModels;
using WorkStation.Core.Models;
using WorkStation.Core.Services;

namespace WorkStation.Tests;

/// <summary>报告页：切类型、切周期、重新汇总不丢人工补充。</summary>
public class ReportsViewModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WorkStationService _service;

    public ReportsViewModelTests()
    {
        _connection = TestDb.OpenMigrated();
        _service = new WorkStationService(_connection, ":memory:");
    }

    public void Dispose() => _connection.Dispose();

    private ReportsViewModel NewViewModel(DateTime? now = null) =>
        new(_service, (_, _) => { }, () => now ?? DateTime.Now);

    [AvaloniaFact]
    public void Reload_LoadsTodayReport()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();

        Assert.Equal(ReportKind.Daily, vm.Kinds[0].Kind);
        Assert.True(vm.Kinds[0].IsSelected);
        Assert.NotEmpty(vm.AutoLines);
        Assert.Equal("2026-11-08", vm.PeriodTitle);
        Assert.False(vm.HasError);
    }

    [AvaloniaFact]
    public void SelectKind_SwitchesPeriodKind()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();
        vm.SelectKindCommand.Execute(ReportKind.Weekly);

        Assert.Equal("2026-11-02 ~ 2026-11-08", vm.PeriodTitle);
        Assert.True(vm.Kinds.Single(k => k.Kind == ReportKind.Weekly).IsSelected);
    }

    [AvaloniaFact]
    public void PreviousPeriod_MovesOneStepBack()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();
        vm.PreviousPeriodCommand.Execute(null);

        Assert.Equal("2026-11-07", vm.PeriodTitle);
    }

    [AvaloniaFact]
    public void NextPeriod_IsBlockedAtCurrentPeriod()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();

        Assert.False(vm.CanGoNext);
    }

    [AvaloniaFact]
    public void Save_KeepsManualTextInDatabase()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();
        vm.ManualText = "今天把接口联调通了";
        vm.SaveCommand.Execute(null);

        var saved = _service.ReportsList(ReportKind.Daily).Data!;
        Assert.Single(saved);
        Assert.Equal("今天把接口联调通了", saved[0].ManualText);
        Assert.Contains("今天把接口联调通了", saved[0].FinalMarkdown);
    }

    [AvaloniaFact]
    public void Regenerate_KeepsManualText()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();
        vm.ManualText = "手写的补充";
        vm.SaveCommand.Execute(null);

        vm.RegenerateCommand.Execute(null);

        var saved = _service.ReportsList(ReportKind.Daily).Data!;
        Assert.Equal("手写的补充", saved[0].ManualText);
    }

    [AvaloniaFact]
    public void History_ListsExistingReportsOfCurrentKind()
    {
        _service.ReportsEnsure(ReportKind.Daily, "2026-11-07");

        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));
        vm.Reload();

        Assert.True(vm.HasHistory);
        Assert.Contains(vm.History, h => h.PeriodStart == "2026-11-07");
    }

    /// <summary>自动汇总的行要分出标题、小节标题与列表项 —— 全按正文渲染，报告就是一堵等大的字。</summary>
    [AvaloniaFact]
    public void AutoLines_DistinguishTitleHeadingAndBullet()
    {
        var vm = NewViewModel(new DateTime(2026, 11, 8, 10, 0, 0));

        vm.Reload();

        Assert.Contains(vm.AutoLines, l => l.IsTitle);
        Assert.Contains(vm.AutoLines, l => l.IsHeading);
        Assert.Contains(vm.AutoLines, l => l.IsBullet && l.Text.StartsWith("• ", StringComparison.Ordinal));
    }
}
