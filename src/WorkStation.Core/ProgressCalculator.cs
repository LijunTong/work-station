using WorkStation.Core.Models;

namespace WorkStation.Core;

/// <summary>
/// 进度计算，全项目唯一实现（主进程与渲染进程共用同一份 Core 代码）。
///
/// 口径：auto 模式 = 已完成数 ÷ (总任务数 − 已取消数)，分母为 0 时进度记 0；
/// 统计口径排除 canceled，避免取消操作稀释进度。
/// </summary>
public static class ProgressCalculator
{
    public static int Compute(IEnumerable<TaskItem> tasks)
    {
        var counted = tasks.Where(t => t.Status != TaskStatus.Canceled).ToList();
        if (counted.Count == 0) return 0;
        var done = counted.Count(t => t.Status == TaskStatus.Done);
        return (int)Math.Round(done / (double)counted.Count * 100, MidpointRounding.AwayFromZero);
    }
}
