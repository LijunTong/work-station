using WorkStation.Core;
using WorkStation.Core.Models;

namespace WorkStation.Tests;

/// <summary>
/// 任务排序口径：优先级 → 逾期 → 截止时间。
///
/// 三个页面（仪表盘清单、任务页分组、看板分列）共用这一套，
/// 所以口径钉在 Core 里直接单测，不必绕 UI 验证。
/// </summary>
public class TaskOrderingTests
{
    // 固定时钟：2026-11-05 10:00
    private static readonly DateTime Now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Local);

    private static TaskItem Task(
        string title,
        Priority priority = Priority.None,
        string? dueAt = null,
        string createdAt = "2026-11-01T00:00:00.000Z") => new()
    {
        Id = title,
        Title = title,
        Priority = priority,
        DueAt = dueAt,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };

    private static string[] Sorted(IEnumerable<TaskItem> tasks) =>
        tasks.OrderBy(t => t, TaskOrdering.OpenTaskComparer(Now)).Select(t => t.Title).ToArray();

    /// <summary>
    /// 优先级权重必须「高的权重小」：Priority 的枚举值是高=0、无=3，
    /// 直接拿枚举值当权重会把「无优先级」排到最前面。
    /// </summary>
    [Fact]
    public void PriorityWeight_HighestIsSmallest()
    {
        Assert.True(TaskOrdering.PriorityWeight(Priority.High) < TaskOrdering.PriorityWeight(Priority.Medium));
        Assert.True(TaskOrdering.PriorityWeight(Priority.Medium) < TaskOrdering.PriorityWeight(Priority.Low));
        Assert.True(TaskOrdering.PriorityWeight(Priority.Low) < TaskOrdering.PriorityWeight(Priority.None));
    }

    [Fact]
    public void OpenTaskComparer_SortsByPriorityFirst()
    {
        var tasks = new[]
        {
            Task("无优先级", Priority.None, "2026-11-05T18:00"),
            Task("低", Priority.Low, "2026-11-05T18:00"),
            Task("高", Priority.High, "2026-11-05T18:00"),
            Task("中", Priority.Medium, "2026-11-05T18:00")
        };

        Assert.Equal(["高", "中", "低", "无优先级"], Sorted(tasks));
    }

    /// <summary>同优先级时，逾期的排前面。</summary>
    [Fact]
    public void OpenTaskComparer_SamePriority_OverdueFirst()
    {
        var tasks = new[]
        {
            Task("还没到期", Priority.High, "2026-11-05T18:00"),
            Task("已经逾期", Priority.High, "2026-11-04T18:00")
        };

        Assert.Equal(["已经逾期", "还没到期"], Sorted(tasks));
    }

    /// <summary>同优先级、同样没逾期时，截止时间早的在前。</summary>
    [Fact]
    public void OpenTaskComparer_SamePriorityAndUrgency_EarlierDueFirst()
    {
        var tasks = new[]
        {
            Task("晚的", Priority.High, "2026-11-05T18:00"),
            Task("早的", Priority.High, "2026-11-05T12:00")
        };

        Assert.Equal(["早的", "晚的"], Sorted(tasks));
    }

    /// <summary>没有截止时间的沉到最后，别让它插到有期限的任务前面。</summary>
    [Fact]
    public void OpenTaskComparer_NoDueDate_SinksToBottom()
    {
        var tasks = new[]
        {
            Task("没期限", Priority.High, null),
            Task("有期限", Priority.High, "2026-11-05T18:00")
        };

        Assert.Equal(["有期限", "没期限"], Sorted(tasks));
    }

    /// <summary>完全同键时按创建时间兜底，保证顺序稳定可预期。</summary>
    [Fact]
    public void OpenTaskComparer_SameEverything_FallsBackToCreatedAt()
    {
        var tasks = new[]
        {
            Task("后建的", Priority.High, "2026-11-05T18:00", "2026-11-02T00:00:00.000Z"),
            Task("先建的", Priority.High, "2026-11-05T18:00", "2026-11-01T00:00:00.000Z")
        };

        Assert.Equal(["先建的", "后建的"], Sorted(tasks));
    }

    /// <summary>已完成的任务按完成时间倒序：刚做完的排在最上面。</summary>
    [Fact]
    public void CompletedTaskComparer_NewestFirst()
    {
        var older = Task("早做完") with { CompletedAt = "2026-11-01T00:00:00.000Z" };
        var newer = Task("晚做完") with { CompletedAt = "2026-11-04T00:00:00.000Z" };

        var sorted = new[] { older, newer }
            .OrderBy(t => t, TaskOrdering.CompletedTaskComparer())
            .Select(t => t.Title)
            .ToArray();

        Assert.Equal(["晚做完", "早做完"], sorted);
    }
}
