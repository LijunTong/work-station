using Microsoft.Data.Sqlite;
using WorkStation.Core.Models;

namespace WorkStation.Core.Data.Repositories;

/// <summary>
/// 标签仓储。SQL 与语义逐条对齐 Electron 版 electron/repositories/tags.ts。
/// </summary>
public sealed class TagsRepository
{
    private readonly SqliteConnection _connection;

    public TagsRepository(SqliteConnection connection) => _connection = connection;

    private static Tag MapRow(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        Name = reader.Str("name"),
        Color = reader.StrOrNull("color")
    };

    public IReadOnlyList<Tag> List()
    {
        using var command = _connection.Command("SELECT * FROM tags ORDER BY name ASC");
        return command.ReadAll(MapRow);
    }

    public Tag? Get(string id)
    {
        using var command = _connection.Command("SELECT * FROM tags WHERE id = @id").Bind(("id", id));
        return command.ReadAll(MapRow).FirstOrDefault();
    }

    public Tag? GetByName(string name)
    {
        using var command = _connection.Command("SELECT * FROM tags WHERE name = @name").Bind(("name", name));
        return command.ReadAll(MapRow).FirstOrDefault();
    }

    public Tag Create(CreateTagInput input)
    {
        var id = Ids.NewId();
        using var command = _connection.Command(
            "INSERT INTO tags (id, name, color) VALUES (@id, @name, @color)")
            .Bind(("id", id), ("name", input.Name), ("color", input.Color));
        command.ExecuteNonQuery();

        return Get(id) ?? throw new InvalidOperationException("标签创建后读取失败");
    }

    public Tag? Update(string id, UpdateTagInput patch)
    {
        var existing = Get(id);
        if (existing is null) return null;

        // color 可空：没传保留原值，传 null 是显式清空
        var name = patch.Name.Or(existing.Name);
        var color = patch.Color.HasValue ? patch.Color.Value : existing.Color;

        using var command = _connection.Command(
            "UPDATE tags SET name = @name, color = @color WHERE id = @id")
            .Bind(("name", name), ("color", color), ("id", id));
        command.ExecuteNonQuery();

        return Get(id);
    }

    /// <summary>删除标签。task_tags 上的关联由外键级联解除，任务本身不受影响。</summary>
    public bool Remove(string id)
    {
        using var command = _connection.Command("DELETE FROM tags WHERE id = @id").Bind(("id", id));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 给任务设置标签（整体替换）。
    ///
    /// 用事务而不是手写 BEGIN/COMMIT/ROLLBACK：SQLite 在致命错误（SQLITE_FULL / SQLITE_IOERR）
    /// 下会自动回滚，此时手写的 ROLLBACK 自身会抛错，把原始异常顶掉。
    /// 未知 id 会撞外键约束 → 整个事务回滚，原有标签关联保留。
    /// </summary>
    public void SetTaskTags(string taskId, IReadOnlyList<string> tagIds)
    {
        Database.WithTransaction(_connection, transaction =>
        {
            using (var delete = _connection.Command(
                       "DELETE FROM task_tags WHERE task_id = @taskId", transaction)
                       .Bind(("taskId", taskId)))
            {
                delete.ExecuteNonQuery();
            }

            // 去重：task_tags 主键是 (task_id, tag_id)，重复插入会撞唯一约束
            var distinct = tagIds.Distinct().ToArray();
            foreach (var tagId in distinct)
            {
                using var insert = _connection.Command(
                    "INSERT OR IGNORE INTO task_tags (task_id, tag_id) VALUES (@taskId, @tagId)", transaction)
                    .Bind(("taskId", taskId), ("tagId", tagId));
                insert.ExecuteNonQuery();
            }
        });
    }

    public IReadOnlyList<Tag> ListTaskTags(string taskId)
    {
        using var command = _connection.Command("""
            SELECT t.* FROM tags t
            JOIN task_tags tt ON tt.tag_id = t.id
            WHERE tt.task_id = @taskId
            ORDER BY t.name ASC
            """).Bind(("taskId", taskId));
        return command.ReadAll(MapRow);
    }

    /// <summary>某个标签下的任务 id 列表。显式 ORDER BY，不靠索引碰巧有序。</summary>
    public IReadOnlyList<string> ListTagTaskIds(string tagId)
    {
        using var command = _connection.Command(
            "SELECT task_id FROM task_tags WHERE tag_id = @tagId ORDER BY task_id ASC")
            .Bind(("tagId", tagId));
        return command.ReadAll(reader => reader.Str("task_id"));
    }
}
