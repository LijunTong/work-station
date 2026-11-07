namespace WorkStation.Core.Data;

/// <summary>一条版本化迁移。</summary>
public sealed record Migration(int Version, string Name, string Sql);

/// <summary>
/// 版本化迁移。SQL 与 Electron 版 electron/migrations.ts 逐字一致 —— 表结构、索引、
/// CHECK 约束全部照搬，这样新应用能直接打开旧版留下的 data.db，不需要任何数据迁移。
/// </summary>
public static class Migrations
{
    public static IReadOnlyList<Migration> All { get; } = new[]
    {
        new Migration(1, "init-core-schema", InitCoreSchema),

        // 任务补一个「开始时间」：项目有起止，任务也需要起止，
        // 新增任务时默认继承所属项目的区间。ALTER TABLE 是加列，老库直接升。
        new Migration(2, "task-start-at", AddTaskStartAt)
    };

    private const string AddTaskStartAt = """
        ALTER TABLE tasks ADD COLUMN start_at TEXT;
        """;

    private const string InitCoreSchema = """
        CREATE TABLE projects (
          id              TEXT PRIMARY KEY,
          name            TEXT NOT NULL,
          description     TEXT NOT NULL DEFAULT '',
          status          TEXT NOT NULL DEFAULT 'planning'
                          CHECK (status IN ('planning','active','paused','done')),
          priority        TEXT NOT NULL DEFAULT 'none'
                          CHECK (priority IN ('high','medium','low','none')),
          color           TEXT,
          progress_mode   TEXT NOT NULL DEFAULT 'auto'
                          CHECK (progress_mode IN ('auto','manual')),
          progress_manual INTEGER CHECK (progress_manual IS NULL OR (progress_manual >= 0 AND progress_manual <= 100)),
          start_date      TEXT,
          due_date        TEXT,
          sort_order      INTEGER NOT NULL DEFAULT 0,
          created_at      TEXT NOT NULL,
          updated_at      TEXT NOT NULL,
          archived_at     TEXT
        );

        CREATE TABLE tasks (
          id              TEXT PRIMARY KEY,
          project_id      TEXT REFERENCES projects(id) ON DELETE CASCADE,
          title           TEXT NOT NULL,
          notes           TEXT NOT NULL DEFAULT '',
          status          TEXT NOT NULL DEFAULT 'todo'
                          CHECK (status IN ('todo','doing','blocked','done','canceled')),
          priority        TEXT NOT NULL DEFAULT 'none'
                          CHECK (priority IN ('high','medium','low','none')),
          due_at          TEXT,
          remind_before   INTEGER NOT NULL DEFAULT 0,
          estimate        REAL,
          spent           REAL,
          blocked_reason  TEXT,
          sort_order      INTEGER NOT NULL DEFAULT 0,
          created_at      TEXT NOT NULL,
          updated_at      TEXT NOT NULL,
          started_at      TEXT,
          completed_at    TEXT
        );

        CREATE TABLE tags (
          id    TEXT PRIMARY KEY,
          name  TEXT NOT NULL UNIQUE,
          color TEXT
        );

        CREATE TABLE task_tags (
          task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
          tag_id  TEXT NOT NULL REFERENCES tags(id)  ON DELETE CASCADE,
          PRIMARY KEY (task_id, tag_id)
        );

        CREATE TABLE reminders (
          id           TEXT PRIMARY KEY,
          kind         TEXT NOT NULL CHECK (kind IN ('task_due','custom')),
          task_id      TEXT REFERENCES tasks(id) ON DELETE CASCADE,
          title        TEXT NOT NULL,
          remind_at    TEXT NOT NULL,
          repeat_rule  TEXT NOT NULL DEFAULT 'none'
                       CHECK (repeat_rule IN ('none','daily','weekly')),
          channel      TEXT NOT NULL DEFAULT 'system'
                       CHECK (channel IN ('system','inapp')),
          status       TEXT NOT NULL DEFAULT 'pending'
                       CHECK (status IN ('pending','fired','dismissed','failed','done')),
          fired_at     TEXT,
          created_at   TEXT NOT NULL,
          updated_at   TEXT NOT NULL
        );

        CREATE TABLE reports (
          id             TEXT PRIMARY KEY,
          kind           TEXT NOT NULL CHECK (kind IN ('weekly','monthly')),
          period_start   TEXT NOT NULL,
          period_end     TEXT NOT NULL,
          auto_markdown  TEXT NOT NULL DEFAULT '',
          manual_text    TEXT NOT NULL DEFAULT '',
          final_markdown TEXT NOT NULL DEFAULT '',
          created_at     TEXT NOT NULL,
          updated_at     TEXT NOT NULL
        );

        CREATE TABLE settings (
          key   TEXT PRIMARY KEY,
          value TEXT NOT NULL
        );

        CREATE INDEX idx_tasks_project  ON tasks(project_id);
        CREATE INDEX idx_tasks_status   ON tasks(status);
        CREATE INDEX idx_tasks_due      ON tasks(due_at);
        CREATE INDEX idx_task_tags_tag  ON task_tags(tag_id);
        CREATE INDEX idx_reminders_due  ON reminders(status, remind_at);
        CREATE INDEX idx_reports_period ON reports(kind, period_start);
        """;
}
