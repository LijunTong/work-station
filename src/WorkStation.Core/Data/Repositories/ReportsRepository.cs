using Microsoft.Data.Sqlite;
using WorkStation.Core;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Data.Repositories;

/// <summary>
/// 报告仓储。一个周期一条报告 —— (kind, period_start) 上的唯一索引负责保证。
///
/// 仓储只管存取，不碰「报告里该写什么」：那是 <c>ReportComposer</c> 的事。
/// </summary>
public sealed class ReportsRepository
{
    private readonly SqliteConnection _connection;
    private readonly Func<string> _nowIso;

    public ReportsRepository(SqliteConnection connection, Func<string>? nowIso = null)
    {
        _connection = connection;
        _nowIso = nowIso ?? (() => LocalTime.NowIso());
    }

    private static Report MapRow(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        Kind = DbEnum.ParseReportKind(reader.Str("kind")),
        PeriodStart = reader.Str("period_start"),
        PeriodEnd = reader.Str("period_end"),
        AutoMarkdown = reader.Str("auto_markdown"),
        ManualText = reader.Str("manual_text"),
        FinalMarkdown = reader.Str("final_markdown"),
        CreatedAt = reader.Str("created_at"),
        UpdatedAt = reader.Str("updated_at")
    };

    /// <summary>报告列表，按周期起点倒序 —— 最近的在最前。</summary>
    public IReadOnlyList<Report> List(ReportKind? kind = null)
    {
        if (kind is null)
        {
            return _connection.Command("SELECT * FROM reports ORDER BY period_start DESC, kind ASC")
                .ReadAll(MapRow);
        }

        using var command = _connection.Command(
            "SELECT * FROM reports WHERE kind = @kind ORDER BY period_start DESC")
            .Bind(("kind", kind.Value.ToDb()));
        return command.ReadAll(MapRow);
    }

    public Report? Get(string id)
    {
        using var command = _connection.Command("SELECT * FROM reports WHERE id = @id").Bind(("id", id));
        return command.ReadAll(MapRow).FirstOrDefault();
    }

    public Report? GetByPeriod(ReportKind kind, string periodStart)
    {
        using var command = _connection.Command(
            "SELECT * FROM reports WHERE kind = @kind AND period_start = @start")
            .Bind(("kind", kind.ToDb()), ("start", periodStart));
        return command.ReadAll(MapRow).FirstOrDefault();
    }

    /// <summary>新建一条报告。Id 与时间戳由仓储生成。</summary>
    public Report Create(Report draft)
    {
        var id = Ids.NewId();
        var now = _nowIso();

        using var command = _connection.Command("""
            INSERT INTO reports (
              id, kind, period_start, period_end,
              auto_markdown, manual_text, final_markdown,
              created_at, updated_at
            ) VALUES (
              @id, @kind, @periodStart, @periodEnd,
              @autoMarkdown, @manualText, @finalMarkdown,
              @createdAt, @updatedAt
            )
            """).Bind(
            ("id", id),
            ("kind", draft.Kind.ToDb()),
            ("periodStart", draft.PeriodStart),
            ("periodEnd", draft.PeriodEnd),
            ("autoMarkdown", draft.AutoMarkdown),
            ("manualText", draft.ManualText),
            ("finalMarkdown", draft.FinalMarkdown),
            ("createdAt", now),
            ("updatedAt", now));
        command.ExecuteNonQuery();

        return Get(id) ?? throw new InvalidOperationException("报告创建后读取失败");
    }

    /// <summary>
    /// 覆盖报告正文。周期与类型不动 —— 那是这条报告的身份。
    /// </summary>
    public Report Update(Report report)
    {
        using var command = _connection.Command("""
            UPDATE reports SET
              auto_markdown = @autoMarkdown,
              manual_text = @manualText,
              final_markdown = @finalMarkdown,
              updated_at = @updatedAt
            WHERE id = @id
            """).Bind(
            ("id", report.Id),
            ("autoMarkdown", report.AutoMarkdown),
            ("manualText", report.ManualText),
            ("finalMarkdown", report.FinalMarkdown),
            ("updatedAt", _nowIso()));
        command.ExecuteNonQuery();

        return Get(report.Id) ?? throw new InvalidOperationException("报告更新后读取失败");
    }

    public bool Remove(string id)
    {
        using var command = _connection.Command("DELETE FROM reports WHERE id = @id").Bind(("id", id));
        return command.ExecuteNonQuery() > 0;
    }
}
