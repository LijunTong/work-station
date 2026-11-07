using WorkStation.Core.Board;
using WorkStation.Core.Models;
using WorkStation.Core.Projects;
using WorkStation.Core.Time;

namespace WorkStation.Core.Dashboard;

/// <summary>仪表盘统计的时间窗口。</summary>
public enum DashboardRange
{
    /// <summary>今天到期。</summary>
    Today,

    /// <summary>本周（周一至周日）到期。</summary>
    Week,

    /// <summary>本月到期。</summary>
    Month
}

/// <summary>一个时间窗口内的任务统计。</summary>
public sealed record DashboardRangeStats
{
    /// <summary>该窗口内到期的未完成任务数。</summary>
    public int Due { get; init; }

    /// <summary>该窗口内已完成的任务数（按完成时间落窗）。</summary>
    public int Completed { get; init; }

    /// <summary>窗口内到期总数（含已完成）——完成率的分母。</summary>
    public int Total => Due + Completed;

    /// <summary>完成率 0–100。窗口内没有到期任务时为 0。</summary>
    public int CompletionRate => Total == 0 ? 0 : (int)Math.Round(Completed * 100.0 / Total);
}

/// <summary>近 N 天每天的完成量，用于画趋势柱状图。</summary>
public sealed record DashboardTrendPoint
{
    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public required string Date { get; init; }

    /// <summary>图表上的短标签，如「11-06」。</summary>
    public required string Label { get; init; }

    /// <summary>该日完成的任务数。</summary>
    public int Completed { get; init; }

    /// <summary>该日到期的任务数。</summary>
    public int Due { get; init; }

    /// <summary>柱高比例 0–1（相对窗口内最大值）。</summary>
    public double Ratio { get; init; }

    /// <summary>完成柱高度比例 0–1。</summary>
    public double CompletedRatio { get; init; }

    /// <summary>到期柱高度比例 0–1。</summary>
    public double DueRatio { get; init; }

    /// <summary>是否是今天（今天的柱子高亮）。</summary>
    public bool IsToday { get; init; }
}

/// <summary>状态分布中的一项。</summary>
public sealed record DashboardStatusSlice
{
    public required TaskStatus Status { get; init; }

    public required string Label { get; init; }

    public int Count { get; init; }

    /// <summary>占比 0–1。</summary>
    public double Ratio { get; init; }
}

/// <summary>优先级分布中的一项。</summary>
public sealed record DashboardPrioritySlice
{
    public required Priority Priority { get; init; }

    public required string Label { get; init; }

    public int Count { get; init; }

    /// <summary>占比 0–1。</summary>
    public double Ratio { get; init; }
}

/// <summary>仪表盘上的一个项目进度。</summary>
public sealed record DashboardProjectProgress
{
    /// <summary>项目 Id —— 点它要能弹到该项目的任务清单。</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>项目状态（规划中/进行中/已暂停/已完成）。</summary>
    public required string StatusLabel { get; init; }

    /// <summary>完成进度 0–100。</summary>
    public required int Progress { get; init; }

    /// <summary>项目下的任务总数。</summary>
    public required int Total { get; init; }

    /// <summary>已完成任务数。</summary>
    public required int Done { get; init; }

    /// <summary>进行中任务数。</summary>
    public required int Doing { get; init; }

    /// <summary>逾期未完成任务数。</summary>
    public required int Overdue { get; init; }

    /// <summary>未完成（含逾期）任务数。</summary>
    public int Open => Math.Max(0, Total - Done);
}

/// <summary>仪表盘上的近期任务（不是统计，是一条条能看的任务）。</summary>
public sealed record DashboardTaskRow
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>所属项目名；没有项目则为空。</summary>
    public string? ProjectName { get; init; }

    /// <summary>状态文案。</summary>
    public required string StatusLabel { get; init; }

    public required Priority Priority { get; init; }

    /// <summary>截止时间（本地墙钟串）。没有就空。</summary>
    public string DueAt { get; init; } = string.Empty;

    /// <summary>是否已逾期。</summary>
    public bool IsOverdue { get; init; }
}

/// <summary>
/// 仪表盘统计 —— 纯函数，放 Core 里可直接单测。
///
/// 口径与看板**刻意不同**，因为两者回答的是不同问题：
/// <list type="bullet">
///   <item>仪表盘回答「这段时间要扛多少活」——按**截止日期落在窗口内**累计，
///         今日 ⊂ 本周 ⊂ 本月，是嵌套的工作量视图</item>
///   <item>看板回答「现在该做什么」——按 TaskGrouping 分组（今日/逾期/本周），
///         逾期任务单独成组，不混在今日里</item>
/// </list>
/// 所以仪表盘的「本周 3」和看板的「本周 0」不矛盾：一个含今天和已过期的本周任务，
/// 一个只算本周还没到期的那部分。
///
/// 已完成一律按**完成时间**落窗 —— 今天完成的活儿哪怕原定截止在上个月，
/// 也算今天完成，这才对得上「今天干了多少」。
/// </summary>
public static class DashboardStats
{
    /// <summary>统计三个时间窗口。</summary>
    public static IReadOnlyDictionary<DashboardRange, DashboardRangeStats> ComputeRanges(
        IReadOnlyList<TaskItem> tasks,
        DateTime now)
    {
        var open = tasks.Where(TaskBoard.IsOpen).ToList();
        var localNow = LocalTime.AsLocal(now);

        return new Dictionary<DashboardRange, DashboardRangeStats>
        {
            [DashboardRange.Today] = ComputeRange(open, tasks, localNow, RangeKind.Today),
            [DashboardRange.Week] = ComputeRange(open, tasks, localNow, RangeKind.Week),
            [DashboardRange.Month] = ComputeRange(open, tasks, localNow, RangeKind.Month)
        };
    }

    /// <summary>
    /// 某个窗口内到期的未完成任务清单。
    ///
    /// 与 <see cref="ComputeRanges"/> 用同一套判定，所以「卡片上的数字」
    /// 与「展开出来的条数」永远一致。
    /// </summary>
    public static IReadOnlyList<TaskItem> TasksInRange(
        IReadOnlyList<TaskItem> tasks,
        DashboardRange range,
        DateTime now)
    {
        var localNow = LocalTime.AsLocal(now);
        var kind = range switch
        {
            DashboardRange.Today => RangeKind.Today,
            DashboardRange.Week => RangeKind.Week,
            _ => RangeKind.Month
        };

        return tasks
            .Where(TaskBoard.IsOpen)
            .Where(t => InRange(t, localNow, kind))
            // 优先级在前（高→中→低→无），同优先级内再按逾期、截止时间
            .OrderBy(t => t, TaskOrdering.OpenTaskComparer(localNow))
            .ToList();
    }

    /// <summary>近 N 天的完成趋势（含今天）。</summary>
    public static IReadOnlyList<DashboardTrendPoint> ComputeTrend(
        IReadOnlyList<TaskItem> tasks,
        DateTime now,
        int days = 7)
    {
        var localNow = LocalTime.AsLocal(now);
        var today = localNow.Date;

        var points = new List<DashboardTrendPoint>(days);
        for (var i = days - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var date = LocalTime.ToLocalDate(day);

            points.Add(new DashboardTrendPoint
            {
                Date = date,
                Label = day.ToString("MM-dd"),
                Completed = tasks.Count(t => CompletedOn(t, day)),
                Due = tasks.Count(t => DueOn(t, day)),
                IsToday = i == 0
            });
        }

        // 完成柱与到期柱各自按自己的最大值定比例，两根柱才可比、也不会互相盖住
        var maxCompleted = points.Count == 0 ? 0 : points.Max(p => p.Completed);
        var maxDue = points.Count == 0 ? 0 : points.Max(p => p.Due);
        return points
            .Select(p => p with
            {
                Ratio = maxCompleted == 0 ? 0 : p.Completed / (double)maxCompleted,
                CompletedRatio = maxCompleted == 0 ? 0 : p.Completed / (double)maxCompleted,
                DueRatio = maxDue == 0 ? 0 : p.Due / (double)maxDue
            })
            .ToList();
    }

    /// <summary>任务状态分布。已取消不计入 —— 它不是「待办」的一部分。</summary>
    public static IReadOnlyList<DashboardStatusSlice> ComputeStatusBreakdown(
        IReadOnlyList<TaskItem> tasks)
    {
        var counted = tasks.Where(t => t.Status != TaskStatus.Canceled).ToList();
        var total = counted.Count;

        return new[]
        {
            TaskStatus.Todo,
            TaskStatus.Doing,
            TaskStatus.Blocked,
            TaskStatus.Done
        }
        .Select(status =>
        {
            var count = counted.Count(t => t.Status == status);
            return new DashboardStatusSlice
            {
                Status = status,
                Label = TaskBoard.StatusTitle(status),
                Count = count,
                Ratio = total == 0 ? 0 : count / (double)total
            };
        })
        .ToList();
    }

    /// <summary>优先级分布（只看未完成的任务）。</summary>
    public static IReadOnlyList<DashboardPrioritySlice> ComputePriorityBreakdown(
        IReadOnlyList<TaskItem> tasks)
    {
        var open = tasks.Where(TaskBoard.IsOpen).ToList();
        var total = open.Count;

        return new[]
        {
            Priority.High,
            Priority.Medium,
            Priority.Low,
            Priority.None
        }
        .Select(priority =>
        {
            var count = open.Count(t => t.Priority == priority);
            return new DashboardPrioritySlice
            {
                Priority = priority,
                Label = priority switch
                {
                    Priority.High => "高",
                    Priority.Medium => "中",
                    Priority.Low => "低",
                    _ => "无"
                },
                Count = count,
                Ratio = total == 0 ? 0 : count / (double)total
            };
        })
        .ToList();
    }

    /// <summary>
    /// 未完成任务里，逾期 + 已阻塞的数量 —— 最需要立刻处理的两类。
    ///
    /// 口径与看板概览条**完全一致**（直接复用 <see cref="TaskBoard.ComputeOverview"/>），
    /// 这样「去看板处理」跳转过去看到的数字就是这里显示的数字。
    /// </summary>
    public static (int Overdue, int Blocked) ComputeAttention(
        IReadOnlyList<TaskItem> tasks,
        DateTime now)
    {
        var overview = TaskBoard.ComputeOverview(tasks, now);
        return (overview.Overdue, overview.Blocked);
    }

    /// <summary>
    /// 各项目的进度与任务数。
    ///
    /// 进度与计数**直接复用 <see cref="ProjectSummary.Compute"/>**，
    /// 保证仪表盘上的「完成 %」「逾期数」与项目页卡片逐字一致 ——
    /// 同一份数据两处显示，数字对不上就会让人以为是 bug。
    ///
    /// 排序：进行中的项目在前（那是眼下在跑的），其次按任务数多的在前。
    /// </summary>
    public static IReadOnlyList<DashboardProjectProgress> ComputeProjectProgress(
        IReadOnlyList<Project> projects,
        IReadOnlyList<TaskItem> tasks,
        DateTime now)
    {
        var tasksByProject = tasks
            .Where(t => t.ProjectId is not null)
            .GroupBy(t => t.ProjectId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        return projects
            .Select(project =>
            {
                tasksByProject.TryGetValue(project.Id, out var projectTasks);
                var (counts, progress) = ProjectSummary.Compute(project, projectTasks ?? [], now);

                return new DashboardProjectProgress
                {
                    Id = project.Id,
                    Name = project.Name,
                    StatusLabel = ProjectSummary.StatusTitle(project.Status),
                    Progress = progress,
                    Total = counts.Total,
                    Done = counts.Done,
                    Doing = counts.Doing,
                    Overdue = counts.Overdue
                };
            })
            .OrderByDescending(p => p.Doing)
            .ThenByDescending(p => p.Total)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 近期任务清单：最需要先看的那些 —— 逾期的排最前，其次按截止时间近的在前。
    ///
    /// 只取未完成的任务（已完成的进「近 7 天完成」趋势图，不再占清单位置）。
    /// </summary>
    public static IReadOnlyList<DashboardTaskRow> ComputeUpcomingTasks(
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyDictionary<string, string> projectNames,
        DateTime now,
        int limit = 8)
    {
        var localNow = LocalTime.AsLocal(now);
        var nowLocalDateTime = LocalTime.ToLocalDateTime(localNow);

        return tasks
            .Where(TaskBoard.IsOpen)
            .OrderBy(t => Formatting.IsOverdue(t.DueAt, nowLocalDateTime) ? 0 : 1)
            .ThenBy(t => t.DueAt ?? string.Empty, StringComparer.Ordinal)
            // 优先级当第三级：同一天到期、同样紧急的，高优先级在前。
            // 用显式权重而不是枚举值本身 —— Priority 的枚举值是无=3、高=0，
            // 直接降序排会把「无优先级」顶到最前面。
            .ThenBy(t => TaskOrdering.PriorityWeight(t.Priority))
            .Take(limit)
            .Select(task =>
            {
                string? projectName = null;
                if (task.ProjectId is not null)
                {
                    projectNames.TryGetValue(task.ProjectId, out projectName);
                }

                return new DashboardTaskRow
                {
                    Id = task.Id,
                    Title = task.Title,
                    ProjectName = projectName,
                    StatusLabel = TaskBoard.StatusTitle(task.Status),
                    Priority = task.Priority,
                    DueAt = task.DueAt ?? string.Empty,
                    IsOverdue = Formatting.IsOverdue(task.DueAt, nowLocalDateTime)
                };
            })
            .ToList();
    }

    private enum RangeKind
    {
        Today,
        Week,
        Month
    }

    private static DashboardRangeStats ComputeRange(
        List<TaskItem> open,
        IReadOnlyList<TaskItem> all,
        DateTime localNow,
        RangeKind kind)
    {
        var due = open.Count(t => InRange(t, localNow, kind));
        var completed = all.Count(t => CompletedInRange(t, localNow, kind));

        return new DashboardRangeStats
        {
            Due = due,
            Completed = completed
        };
    }

    /// <summary>未完成任务的截止时间是否落在窗口内（累计口径：本周含今天，本月含本周）。</summary>
    private static bool InRange(TaskItem task, DateTime localNow, RangeKind kind)
    {
        var due = task.DueAt;
        if (string.IsNullOrEmpty(due)) return false;

        var dueDate = due.Substring(0, 10);
        var today = LocalTime.ToLocalDate(localNow);

        return kind switch
        {
            // 今日：按日历日（含今天已过点的那部分），与「本周含今天」保持嵌套一致
            RangeKind.Today => dueDate == today,
            RangeKind.Week => string.CompareOrdinal(dueDate, WeekStart(localNow)) >= 0
                              && string.CompareOrdinal(dueDate, WeekEnd(localNow)) <= 0,
            RangeKind.Month => dueDate.StartsWith(today.Substring(0, 7), StringComparison.Ordinal),
            _ => false
        };
    }

    /// <summary>已完成任务的完成时间是否落在窗口内。</summary>
    private static bool CompletedInRange(TaskItem task, DateTime localNow, RangeKind kind)
    {
        var completed = CompletedDate(task);
        if (completed is null) return false;

        var date = completed;
        var today = LocalTime.ToLocalDate(localNow);

        return kind switch
        {
            RangeKind.Today => date == today,
            RangeKind.Week => string.CompareOrdinal(date, WeekStart(localNow)) >= 0
                              && string.CompareOrdinal(date, WeekEnd(localNow)) <= 0,
            RangeKind.Month => date.StartsWith(today.Substring(0, 7), StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool CompletedOn(TaskItem task, DateTime day) =>
        CompletedDate(task) == LocalTime.ToLocalDate(day);

    private static bool DueOn(TaskItem task, DateTime day)
    {
        var due = task.DueAt;
        if (string.IsNullOrEmpty(due)) return false;
        return due.Substring(0, 10) == LocalTime.ToLocalDate(day);
    }

    /// <summary>任务的完成日期（本地日历日）。解析不了返回 null。</summary>
    private static string? CompletedDate(TaskItem task)
    {
        var stamp = task.CompletedAt;
        if (string.IsNullOrWhiteSpace(stamp)) return null;

        if (!DateTime.TryParse(
                stamp.Trim(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return null;
        }

        return LocalTime.ToLocalDate(LocalTime.AsLocal(parsed));
    }

    /// <summary>本周第一天（周一）。</summary>
    private static string WeekStart(DateTime localNow)
    {
        var day = localNow.Date;
        var offset = (int)day.DayOfWeek - (int)DayOfWeek.Monday;
        if (offset < 0) offset += 7;
        return LocalTime.ToLocalDate(day.AddDays(-offset));
    }

    /// <summary>本周最后一天（周日）。与看板的周口径保持一致。</summary>
    private static string WeekEnd(DateTime localNow) => TaskGrouping.WeekEndDate(localNow);
}
