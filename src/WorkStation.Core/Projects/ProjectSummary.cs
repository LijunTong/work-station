using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Projects;

/// <summary>
/// 项目的任务统计与进度口径。
///
/// 项目列表卡片与看板卡片都要这一组数字，早先各自算一遍，
/// 结果「逾期」口径在两边漂移。抽到这里之后两边共用同一份实现。
/// </summary>
public static class ProjectSummary
{
    public static (TaskCounts Counts, int Progress) Compute(
        Project project,
        IReadOnlyList<TaskItem> tasks,
        DateTime now)
    {
        var nowLocal = LocalTime.ToLocalDateTime(now);

        var counts = new TaskCounts
        {
            Total = tasks.Count,
            Done = tasks.Count(t => t.Status == TaskStatus.Done),
            Doing = tasks.Count(t => t.Status == TaskStatus.Doing),
            Overdue = tasks.Count(t => t.Status is not (TaskStatus.Done or TaskStatus.Canceled)
                                       && Formatting.IsOverdue(t.DueAt, nowLocal))
        };

        var progress = project.ProgressMode == ProgressMode.Manual
            ? Math.Clamp(project.ProgressManual ?? 0, 0, 100)
            : ProgressCalculator.Compute(tasks);

        return (counts, progress);
    }
}
