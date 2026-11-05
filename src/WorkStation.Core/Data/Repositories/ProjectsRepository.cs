using Microsoft.Data.Sqlite;
using WorkStation.Core.Models;
using WorkStation.Core.Time;

namespace WorkStation.Core.Data.Repositories;

/// <summary>
/// 项目仓储。SQL 与语义逐条对齐 Electron 版 electron/repositories/projects.ts。
/// </summary>
public sealed class ProjectsRepository
{
    private readonly SqliteConnection _connection;

    public ProjectsRepository(SqliteConnection connection) => _connection = connection;

    private static Project MapRow(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        Name = reader.Str("name"),
        Description = reader.Str("description"),
        Status = DbEnum.ParseProjectStatus(reader.Str("status")),
        Priority = DbEnum.ParsePriority(reader.Str("priority")),
        Color = reader.StrOrNull("color"),
        ProgressMode = DbEnum.ParseProgressMode(reader.Str("progress_mode")),
        ProgressManual = reader.IntOrNull("progress_manual"),
        StartDate = reader.StrOrNull("start_date"),
        DueDate = reader.StrOrNull("due_date"),
        SortOrder = reader.Int("sort_order"),
        CreatedAt = reader.Str("created_at"),
        UpdatedAt = reader.Str("updated_at"),
        ArchivedAt = reader.StrOrNull("archived_at")
    };

    /// <summary>默认只返回未归档项目；includeArchived 为 true 时连归档的一起返回。</summary>
    public IReadOnlyList<Project> List(bool includeArchived = false)
    {
        var sql = includeArchived
            ? "SELECT * FROM projects ORDER BY sort_order ASC, created_at ASC"
            : "SELECT * FROM projects WHERE archived_at IS NULL ORDER BY sort_order ASC, created_at ASC";
        return _connection.Command(sql).ReadAll(MapRow);
    }

    public Project? Get(string id)
    {
        using var command = _connection.Command("SELECT * FROM projects WHERE id = @id").Bind(("id", id));
        return command.ReadAll(MapRow).FirstOrDefault();
    }

    public Project? GetByName(string name)
    {
        using var command = _connection.Command("SELECT * FROM projects WHERE name = @name").Bind(("name", name));
        return command.ReadAll(MapRow).FirstOrDefault();
    }

    public Project Create(CreateProjectInput input)
    {
        var id = Ids.NewId();
        var now = LocalTime.NowIso();

        using var command = _connection.Command("""
            INSERT INTO projects (
              id, name, description, status, priority, color,
              progress_mode, progress_manual, start_date, due_date,
              sort_order, created_at, updated_at
            ) VALUES (
              @id, @name, @description, @status, @priority, @color,
              @progressMode, @progressManual, @startDate, @dueDate,
              @sortOrder, @createdAt, @updatedAt
            )
            """).Bind(
            ("id", id),
            ("name", input.Name),
            ("description", input.Description ?? string.Empty),
            ("status", (input.Status ?? ProjectStatus.Planning).ToDb()),
            ("priority", (input.Priority ?? Priority.None).ToDb()),
            ("color", input.Color),
            ("progressMode", (input.ProgressMode ?? ProgressMode.Auto).ToDb()),
            ("progressManual", input.ProgressManual),
            ("startDate", input.StartDate),
            ("dueDate", input.DueDate),
            ("sortOrder", 0),
            ("createdAt", now),
            ("updatedAt", now));
        command.ExecuteNonQuery();

        return Get(id) ?? throw new InvalidOperationException("项目创建后读取失败");
    }

    public Project? Update(string id, UpdateProjectInput patch)
    {
        var existing = Get(id);
        if (existing is null) return null;

        // 合并后的每个字段都已确定：没传保留原值，传 null 清空
        var name = patch.Name.Or(existing.Name);
        var description = patch.Description.HasValue ? patch.Description.Value : existing.Description;
        var status = (patch.Status.HasValue ? patch.Status.Value : existing.Status).ToDb();
        var priority = (patch.Priority.HasValue ? patch.Priority.Value : existing.Priority).ToDb();
        var color = patch.Color.HasValue ? patch.Color.Value : existing.Color;
        var progressMode = (patch.ProgressMode.HasValue ? patch.ProgressMode.Value : existing.ProgressMode).ToDb();
        var progressManual = patch.ProgressManual.HasValue ? patch.ProgressManual.Value : existing.ProgressManual;
        var startDate = patch.StartDate.HasValue ? patch.StartDate.Value : existing.StartDate;
        var dueDate = patch.DueDate.HasValue ? patch.DueDate.Value : existing.DueDate;
        var sortOrder = patch.SortOrder.HasValue ? patch.SortOrder.Value : existing.SortOrder;
        var archivedAt = patch.ArchivedAt.HasValue ? patch.ArchivedAt.Value : existing.ArchivedAt;

        using var command = _connection.Command("""
            UPDATE projects SET
              name = @name, description = @description, status = @status, priority = @priority,
              color = @color, progress_mode = @progressMode, progress_manual = @progressManual,
              start_date = @startDate, due_date = @dueDate, sort_order = @sortOrder,
              updated_at = @updatedAt, archived_at = @archivedAt
            WHERE id = @id
            """).Bind(
            ("name", name),
            ("description", description),
            ("status", status),
            ("priority", priority),
            ("color", color),
            ("progressMode", progressMode),
            ("progressManual", progressManual),
            ("startDate", startDate),
            ("dueDate", dueDate),
            ("sortOrder", sortOrder),
            ("updatedAt", LocalTime.NowIso()),
            ("archivedAt", archivedAt),
            ("id", id));
        command.ExecuteNonQuery();

        return Get(id);
    }

    public bool Archive(string id)
    {
        // archived_at 与 updated_at 语义相同，取一次时间戳复用，避免两次调用差 1ms
        var now = LocalTime.NowIso();
        using var command = _connection.Command(
            "UPDATE projects SET archived_at = @now, updated_at = @now WHERE id = @id").Bind(("now", now), ("id", id));
        return command.ExecuteNonQuery() > 0;
    }

    public bool Unarchive(string id)
    {
        using var command = _connection.Command(
            "UPDATE projects SET archived_at = NULL, updated_at = @now WHERE id = @id")
            .Bind(("now", LocalTime.NowIso()), ("id", id));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>永久删除。tasks 与 reminders 由外键 ON DELETE CASCADE 一并删除。</summary>
    public bool Remove(string id)
    {
        using var command = _connection.Command("DELETE FROM projects WHERE id = @id").Bind(("id", id));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 项目下的任务统计。
    ///
    /// 逾期用墙钟口径（due_at 与当前时刻同为 YYYY-MM-DDTHH:mm，字典序即时间序），
    /// 与渲染层的 isOverdue、看板概览条一致；已完成/已取消不计入逾期。
    /// </summary>
    public TaskCounts CountTasks(string projectId)
    {
        var now = LocalTime.ToLocalDateTime(DateTime.Now);
        using var command = _connection.Command("""
            SELECT
              COUNT(*) AS total,
              SUM(CASE WHEN status = 'done' THEN 1 ELSE 0 END) AS done,
              SUM(CASE WHEN status = 'doing' THEN 1 ELSE 0 END) AS doing,
              SUM(CASE WHEN status NOT IN ('done','canceled') AND due_at IS NOT NULL
                       AND due_at < @now THEN 1 ELSE 0 END) AS overdue
            FROM tasks WHERE project_id = @projectId
            """).Bind(("now", now), ("projectId", projectId));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new TaskCounts();
        }

        return new TaskCounts
        {
            Total = reader.Int("total"),
            // SUM 在无匹配行时返回 NULL，这里必须兜底成 0
            Done = reader.IntOrNull("done") ?? 0,
            Doing = reader.IntOrNull("doing") ?? 0,
            Overdue = reader.IntOrNull("overdue") ?? 0
        };
    }
}
