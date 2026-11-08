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
        new Migration(2, "task-start-at", AddTaskStartAt),

        // 报告加日报类型：CHECK 约束改不了，只能重建表。
        // 顺带把 (kind, period_start) 变成唯一索引 —— 一个周期只允许一条报告。
        new Migration(3, "reports-daily-kind", ReportsDailyKind)
    };

    /// <summary>
    /// 重建 reports 表：放开 kind 约束到 daily，并把 (kind, period_start) 变成唯一索引。
    ///
    /// SQLite 不能 ALTER 一个 CHECK 约束，只能建新表 → 拷数据 → 删旧表 → 改名。
    /// 拷贝时按 (kind, period_start) 去重（保留最早的一行）：唯一索引建在
    /// 有重复数据的旧库上会失败，那属于「数据本来就不合法」，不能让整个应用起不来。
    /// </summary>
    private const string ReportsDailyKind = """
        CREATE TABLE reports_new (
          id             TEXT PRIMARY KEY,
          kind           TEXT NOT NULL CHECK (kind IN ('daily','weekly','monthly')),
          period_start   TEXT NOT NULL,
          period_end     TEXT NOT NULL,
          auto_markdown  TEXT NOT NULL DEFAULT '',
          manual_text    TEXT NOT NULL DEFAULT '',
          final_markdown TEXT NOT NULL DEFAULT '',
          created_at     TEXT NOT NULL,
          updated_at     TEXT NOT NULL
        );

        INSERT INTO reports_new (id, kind, period_start, period_end, auto_markdown, manual_text, final_markdown, created_at, updated_at)
        SELECT r.id, r.kind, r.period_start, r.period_end, r.auto_markdown, r.manual_text, r.final_markdown, r.created_at, r.updated_at
        FROM reports r
        WHERE r.rowid = (
          SELECT MIN(r2.rowid) FROM reports r2
          WHERE r2.kind = r.kind AND r2.period_start = r.period_start
        );

        DROP TABLE reports;

        ALTER TABLE reports_new RENAME TO reports;

        CREATE UNIQUE INDEX idx_reports_period ON reports(kind, period_start);
        """;

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
