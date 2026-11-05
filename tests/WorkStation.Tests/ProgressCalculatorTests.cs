using WorkStation.Core;
using WorkStation.Core.Models;

namespace WorkStation.Tests;

public class ProgressCalculatorTests
{
    private static TaskItem Make(TaskStatus status) => new()
    {
        Id = Ids.NewId(),
        Title = "任务",
        Status = status,
        CreatedAt = "2026-11-05T00:00:00.000Z",
        UpdatedAt = "2026-11-05T00:00:00.000Z"
    };

    [Fact]
    public void Compute_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, ProgressCalculator.Compute(Array.Empty<TaskItem>()));
    }

    [Fact]
    public void Compute_AllCanceled_ReturnsZero()
    {
        var tasks = new[] { Make(TaskStatus.Canceled), Make(TaskStatus.Canceled) };
        Assert.Equal(0, ProgressCalculator.Compute(tasks));
    }

    [Fact]
    public void Compute_HalfDone_Returns50()
    {
        var tasks = new[] { Make(TaskStatus.Done), Make(TaskStatus.Todo) };
        Assert.Equal(50, ProgressCalculator.Compute(tasks));
    }

    [Fact]
    public void Compute_CanceledIsExcludedFromDenominator()
    {
        // 1 done / (3 - 1 canceled) = 50
        var tasks = new[] { Make(TaskStatus.Done), Make(TaskStatus.Todo), Make(TaskStatus.Canceled) };
        Assert.Equal(50, ProgressCalculator.Compute(tasks));
    }

    [Fact]
    public void Compute_AllDone_Returns100()
    {
        var tasks = new[] { Make(TaskStatus.Done), Make(TaskStatus.Done) };
        Assert.Equal(100, ProgressCalculator.Compute(tasks));
    }

    [Fact]
    public void Compute_OneThirdDone_Returns33()
    {
        var tasks = new[] { Make(TaskStatus.Done), Make(TaskStatus.Todo), Make(TaskStatus.Doing) };
        Assert.Equal(33, ProgressCalculator.Compute(tasks));
    }

    [Fact]
    public void Compute_DoingAndBlockedCountAsNotDone()
    {
        var tasks = new[] { Make(TaskStatus.Doing), Make(TaskStatus.Blocked) };
        Assert.Equal(0, ProgressCalculator.Compute(tasks));
    }
}
