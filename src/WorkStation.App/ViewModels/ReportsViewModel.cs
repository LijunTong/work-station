using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkStation.Core.Models;
using WorkStation.Core.Reports;
using WorkStation.Core.Services;

namespace WorkStation.App.ViewModels;

/// <summary>报告类型选项（日报 / 周报 / 月报）。</summary>
public sealed partial class ReportKindOption : ObservableObject
{
    public ReportKindOption(ReportKind kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    public ReportKind Kind { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>历史报告列表里的一项。</summary>
public sealed class ReportHistoryItem
{
    public required ReportKind Kind { get; init; }

    /// <summary>列表上显示的标题（周期区间文字）。</summary>
    public required string Title { get; init; }

    /// <summary>周期起点 YYYY-MM-DD。</summary>
    public required string PeriodStart { get; init; }

    public override string ToString() => Title;
}

/// <summary>
/// 报告预览里的一行。
///
/// Core 的 <see cref="ReportMarkdownLine"/> 带着标题层级与列表标记，这里转成界面直接能用的形状：
/// 标题行走标题样式，列表项前面补上项目符号 —— 全按正文渲染，报告就是一堵等大的字。
/// </summary>
public sealed class ReportLineItem
{
    public required string Text { get; init; }

    /// <summary>是不是列表项。</summary>
    public bool IsBullet { get; init; }

    /// <summary>是不是小节标题（Markdown 的 ###）。</summary>
    public bool IsHeading { get; init; }

    /// <summary>是不是整篇的标题（Markdown 的 ##）。</summary>
    public bool IsTitle { get; init; }

    public static ReportLineItem From(ReportMarkdownLine line) => new()
    {
        Text = line.IsBullet ? $"• {line.Text}" : line.Text,
        IsBullet = line.IsBullet,
        IsHeading = line.Level == 2,
        IsTitle = line.Level == 1
    };
}

/// <summary>
/// 报告页：日报 / 周报 / 月报。
///
/// 左边是自动汇总（机器算的，随时可以重新汇总），
/// 右边是人工补充（手写的，**任何自动流程都不覆盖**），
/// 底下是两者拼起来的最终稿 —— 复制与导出的就是它。
/// </summary>
public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly WorkStationService _service;
    private readonly Action<string, bool> _toast;
    private readonly Func<DateTime> _clock;

    private ReportPeriod _period;

    /// <summary>
    /// 历史列表正在重建时的闸门。
    ///
    /// 重建集合会让下拉框把选中项置空、随后自动挑第一项 —— 那一下会回调
    /// <see cref="OnSelectedHistoryChanged"/>，又触发一次加载，来回打转。
    /// 重建期间关掉这个闸门。
    /// </summary>
    private bool _suppressHistorySelection;

    public ReportsViewModel(
        WorkStationService service,
        Action<string, bool> toast,
        Func<DateTime>? clock = null)
    {
        _service = service;
        _toast = toast;
        _clock = clock ?? (() => DateTime.Now);

        Kinds =
        [
            new ReportKindOption(ReportKind.Daily, "日报"),
            new ReportKindOption(ReportKind.Weekly, "周报"),
            new ReportKindOption(ReportKind.Monthly, "月报")
        ];

        Kinds[0].IsSelected = true;
        _period = ReportPeriods.Current(ReportKind.Daily, _clock());
    }

    public ObservableCollection<ReportKindOption> Kinds { get; }

    public ObservableCollection<ReportHistoryItem> History { get; } = [];

    public ObservableCollection<ReportLineItem> AutoLines { get; } = [];

    public ObservableCollection<ReportLineItem> FinalLines { get; } = [];

    /// <summary>人工补充说明。</summary>
    [ObservableProperty]
    private string _manualText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    /// <summary>历史列表里选中的那一期。选中即跳到那一期。</summary>
    [ObservableProperty]
    private ReportHistoryItem? _selectedHistory;

    public bool HasError => Error is not null;

    public bool HasHistory => History.Count > 0;

    /// <summary>周期区间文字。</summary>
    public string PeriodTitle => ReportPeriods.Range(_period);

    /// <summary>能不能再往后翻一期：最多到当前周期，未来没有报告。</summary>
    public bool CanGoNext =>
        _period.PeriodStart != ReportPeriods.Current(SelectedKind, _clock()).PeriodStart;

    /// <summary>复制到剪贴板。由视图注入（剪贴板是平台能力，VM 不该直接碰）。</summary>
    public Func<string, Task<bool>>? CopyToClipboard { get; set; }

    /// <summary>导出 .md。由视图注入（文件选择器是平台能力）。</summary>
    public Func<(string FileName, string Content), Task<bool>>? ExportToFile { get; set; }

    private ReportKind SelectedKind =>
        Kinds.FirstOrDefault(k => k.IsSelected)?.Kind ?? ReportKind.Daily;

    private Report? Current { get; set; }

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnSelectedHistoryChanged(ReportHistoryItem? value)
    {
        if (_suppressHistorySelection) return;
        if (value is null) return;

        var period = ReportPeriods.FromStart(value.Kind, value.PeriodStart);
        if (period.PeriodStart == _period.PeriodStart) return;

        LoadPeriod(period);
    }

    /// <summary>取数刷新：进页面与切类型、切周期时都走这里。</summary>
    public void Reload()
    {
        LoadPeriod(ReportPeriods.Current(SelectedKind, _clock()));
    }

    /// <summary>切换报告类型。</summary>
    [RelayCommand]
    private void SelectKind(ReportKind kind)
    {
        foreach (var option in Kinds)
        {
            option.IsSelected = option.Kind == kind;
        }

        LoadPeriod(ReportPeriods.Current(kind, _clock()));
    }

    [RelayCommand]
    private void PreviousPeriod() => LoadPeriod(ReportPeriods.Shift(_period, -1));

    [RelayCommand]
    private void NextPeriod()
    {
        if (!CanGoNext) return;
        LoadPeriod(ReportPeriods.Shift(_period, 1));
    }

    /// <summary>重新汇总：重算自动汇总，**保留人工补充**。</summary>
    [RelayCommand]
    private void Regenerate()
    {
        if (Current is null) return;

        var result = _service.ReportsRegenerate(Current.Id);
        if (!result.Ok || result.Data is not { } report)
        {
            Error = result.Error?.Message ?? "重新汇总失败";
            return;
        }

        Current = report;
        Apply(report);
        _toast("已重新汇总", false);
    }

    /// <summary>保存人工补充。</summary>
    [RelayCommand]
    private void Save()
    {
        if (Current is null) return;

        var result = _service.ReportsUpdate(Current.Id, ManualText);
        if (!result.Ok || result.Data is not { } report)
        {
            Error = result.Error?.Message ?? "保存失败";
            return;
        }

        Current = report;
        Apply(report);
        _toast("已保存", false);
    }

    /// <summary>复制最终稿到剪贴板。</summary>
    [RelayCommand]
    private async Task CopyAsync()
    {
        if (Current is null) return;
        if (CopyToClipboard is null)
        {
            _toast("复制失败：当前环境没有剪贴板", true);
            return;
        }

        var ok = await CopyToClipboard(Current.FinalMarkdown);
        _toast(ok ? "已复制 Markdown" : "复制失败", !ok);
    }

    /// <summary>导出最终稿为 .md 文件。</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Current is null) return;
        if (ExportToFile is null)
        {
            _toast("导出失败：当前环境没有文件选择器", true);
            return;
        }

        var fileName = $"{ReportPeriods.Label(Current.Kind)}-{Current.PeriodStart}.md";
        var ok = await ExportToFile((fileName, Current.FinalMarkdown));
        _toast(ok ? "已导出" : "已取消导出", !ok);
    }

    private void LoadPeriod(ReportPeriod period)
    {
        IsLoading = true;
        Error = null;

        try
        {
            _period = period;

            var result = _service.ReportsEnsure(period.Kind, period.PeriodStart);
            if (!result.Ok || result.Data is not { } report)
            {
                Error = result.Error?.Message ?? "生成报告失败";
                return;
            }

            Current = report;
            Apply(report);
            LoadHistory(period.Kind);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(PeriodTitle));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    private void Apply(Report report)
    {
        ManualText = report.ManualText;

        AutoLines.Clear();
        foreach (var line in ReportMarkdown.Parse(report.AutoMarkdown))
        {
            AutoLines.Add(ReportLineItem.From(line));
        }

        FinalLines.Clear();
        foreach (var line in ReportMarkdown.Parse(report.FinalMarkdown))
        {
            FinalLines.Add(ReportLineItem.From(line));
        }
    }

    private void LoadHistory(ReportKind kind)
    {
        _suppressHistorySelection = true;
        try
        {
            History.Clear();

            var result = _service.ReportsList(kind);
            if (result.Ok && result.Data is { } reports)
            {
                foreach (var report in reports)
                {
                    History.Add(new ReportHistoryItem
                    {
                        Kind = report.Kind,
                        Title = ReportPeriods.Range(ReportPeriods.FromStart(report.Kind, report.PeriodStart)),
                        PeriodStart = report.PeriodStart
                    });
                }
            }

            // 选中当前这一期：下拉框里的选中项与正在看的内容必须一致
            SelectedHistory = History.FirstOrDefault(h => h.PeriodStart == _period.PeriodStart);
        }
        finally
        {
            _suppressHistorySelection = false;
        }

        OnPropertyChanged(nameof(HasHistory));
    }
}
