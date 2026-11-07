namespace WorkStation.Core.Time;

/// <summary>
/// 新建任务的默认时间口径。表单和顶部「快速添加」共用这一份，
/// 免得同一个「新建任务」两条入口给出两种默认值。
/// </summary>
public static class TaskDefaults
{
    /// <summary>正常情况下的默认截止时刻：当天 18:00。</summary>
    public static readonly TimeSpan DueHour = new(18, 0, 0);

    /// <summary>已经过了 18 点时的顺延时刻：当天 23:59。</summary>
    public static readonly TimeSpan LateDueHour = new(23, 59, 0);

    /// <summary>
    /// 默认截止时刻。过了 18 点就顺延到今天 23:59 ——
    /// 否则晚上新建一条任务，保存瞬间就挂着红色的「已逾期」，看着像 bug。
    /// </summary>
    public static TimeSpan DueTimeFor(DateTime now) => now.TimeOfDay < DueHour ? DueHour : LateDueHour;

    /// <summary>默认截止时间（本地墙钟串），直接喂给 <c>CreateTaskInput.DueAt</c>。</summary>
    public static string DueAtFor(DateTime now) => LocalTime.ToLocalDateTime(now.Date.Add(DueTimeFor(now)));
}
