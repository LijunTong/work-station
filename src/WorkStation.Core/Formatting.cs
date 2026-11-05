namespace WorkStation.Core;

/// <summary>
/// 与 Electron 版 src/lib/format.ts 同口径的格式化工具。
/// </summary>
public static class Formatting
{
    /// <summary>百分比：clamp 到 0–100 后四舍五入（半数进位，与 JS Math.round 一致）。</summary>
    public static string FormatPercent(double value)
    {
        if (double.IsNaN(value)) value = 0;
        var clamped = Math.Clamp(value, 0, 100);
        return $"{(int)Math.Round(clamped, MidpointRounding.AwayFromZero)}%";
    }

    /// <summary>
    /// 工时：保留一位小数。负数归零、NaN 显示为「—」——
    /// Electron 版这两处会输出「-1.5h」与「NaNh」，是已记录的缺陷。
    /// </summary>
    public static string FormatHours(double? value)
    {
        if (value is null || double.IsNaN(value.Value)) return "—";
        var clamped = Math.Max(0, value.Value);
        return $"{Math.Round(clamped * 10, MidpointRounding.AwayFromZero) / 10}h";
    }

    /// <summary>把墙钟串显示成「MM-DD HH:mm」；已是日历日则显示「MM-DD」。</summary>
    public static string FormatLocalDateTime(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        if (value.Length == 16) return value.Substring(5).Replace("T", " ", StringComparison.Ordinal);
        if (value.Length == 10) return value.Substring(5);
        return value;
    }

    /// <summary>
    /// 逾期判定：墙钟串严格小于当前墙钟即为逾期。
    /// 严格小于（&lt;）不是 &lt;=：到期时刻恰好等于当前时刻时不算逾期，
    /// 相等边界有用例钉死。
    /// </summary>
    public static bool IsOverdue(string? dueAt, string nowLocalDateTime)
    {
        if (string.IsNullOrEmpty(dueAt)) return false;
        return string.CompareOrdinal(dueAt, nowLocalDateTime) < 0;
    }
}
