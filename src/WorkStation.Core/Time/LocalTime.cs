using System.Globalization;
using System.Text.RegularExpressions;

namespace WorkStation.Core.Time;

/// <summary>
/// 三类时间格式，不可混用（Electron 版 electron/util/time.ts 的等价实现）：
/// <list type="bullet">
///   <item>记录时间戳（created/updated/started/completed）：ISO 8601 UTC，如 2026-11-05T04:05:00.000Z</item>
///   <item>日期（start_date/due_date/period_start/period_end）：本地日历日，如 2026-11-05</item>
///   <item>本地日期时间（due_at/remind_at）：本地墙钟无时区，如 2026-11-05T14:30</item>
/// </list>
/// 所有「到期/截止」类比较都基于墙钟串做字典序比较 —— 格式定长且本地有序，
/// 字符串序即时间序。
/// </summary>
public static class LocalTime
{
    private static readonly Regex LocalDateTimePattern =
        new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$", RegexOptions.Compiled);

    /// <summary>ISO 8601 UTC 时间戳，毫秒精度。</summary>
    public static string NowIso() =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>本地日历日 YYYY-MM-DD。</summary>
    public static string ToLocalDate(DateTime value) => AsLocal(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>本地墙钟 YYYY-MM-DDTHH:mm。</summary>
    public static string ToLocalDateTime(DateTime value) =>
        AsLocal(value).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// 只校验格式，不校验日历有效性（2026-99-99T99:99 也会通过）。
    /// 这是 Electron 版冻结的契约，本次迁移不改。
    /// </summary>
    public static bool IsLocalDateTime(string? value) =>
        value is not null && LocalDateTimePattern.IsMatch(value);

    /// <summary>
    /// 把入参统一成本地时间：Utc 就地换算，Unspecified 当作已经是本地墙钟。
    /// Electron 版用 Date 的本地取值器，语义与此处一致。
    /// </summary>
    public static DateTime AsLocal(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value.ToLocalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Local),
        _ => value
    };

    /// <summary>
    /// ISO 8601 UTC 时间戳 → 本地日历日。解析不了返回 null。
    ///
    /// 任务的 completed_at 是 UTC 时间戳，要落到「哪一天」必须先换算成本地时间，
    /// 否则晚上完成的事会被算到第二天。
    /// </summary>
    public static string? ToLocalDateOrNull(string? isoUtc)
    {
        if (string.IsNullOrWhiteSpace(isoUtc)) return null;
        if (!DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return null;
        }

        return ToLocalDate(AsLocal(parsed));
    }
}
