import { DatabaseSync } from 'node:sqlite';
import { mkdtempSync, renameSync, rmSync, unlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterAll, describe, expect, it } from 'vitest';
import {
  getUserVersion,
  isHealthy,
  openDatabase,
  runMigrations,
  withTransaction
} from '../electron/db';
import { MIGRATIONS } from '../electron/migrations';
import type { Migration } from '../electron/migrations';

const tempDirs: string[] = [];

/** 真实文件库路径：openDatabase 的外键/句柄行为只在文件库上可验证 */
function tempDbPath(): string {
  const dir = mkdtempSync(join(tmpdir(), 'work-station-db-'));
  tempDirs.push(dir);
  return join(dir, 'data.db');
}

afterAll(() => {
  for (const dir of tempDirs) {
    rmSync(dir, { recursive: true, force: true });
  }
});

function freshDb(): DatabaseSync {
  return new DatabaseSync(':memory:');
}

function tableNames(db: DatabaseSync): string[] {
  return db
    .prepare("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
    .all()
    .map((r) => (r as { name: string }).name)
    .sort();
}

describe('数据库迁移', () => {
  it('空库的 user_version 为 0', () => {
    const db = freshDb();
    expect(getUserVersion(db)).toBe(0);
    db.close();
  });

  it('迁移后 user_version 等于最新迁移版本号', () => {
    const db = freshDb();
    runMigrations(db);
    const latest = Math.max(...MIGRATIONS.map((m) => m.version));
    expect(getUserVersion(db)).toBe(latest);
    db.close();
  });

  it('迁移会创建全部七张核心表', () => {
    const db = freshDb();
    runMigrations(db);
    expect(tableNames(db)).toEqual(
      ['projects', 'reminders', 'reports', 'settings', 'tags', 'task_tags', 'tasks'].sort()
    );
    db.close();
  });

  it('迁移会创建全部六个索引', () => {
    const db = freshDb();
    runMigrations(db);
    const names = db
      .prepare("SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%'")
      .all()
      .map((r) => (r as { name: string }).name)
      .sort();
    expect(names).toEqual(
      [
        'idx_tasks_project',
        'idx_tasks_status',
        'idx_tasks_due',
        'idx_task_tags_tag',
        'idx_reminders_due',
        'idx_reports_period'
      ].sort()
    );
    db.close();
  });

  it('重复执行迁移是幂等的', () => {
    const db = freshDb();
    runMigrations(db);
    runMigrations(db);
    const latest = Math.max(...MIGRATIONS.map((m) => m.version));
    expect(getUserVersion(db)).toBe(latest);
    db.close();
  });

  it('乱序追加的迁移按版本号升序执行，user_version 不回退且再次执行不失败', () => {
    const db = freshDb();
    const originalLength = MIGRATIONS.length;
    MIGRATIONS.push({ version: 3, name: 'v3', sql: 'CREATE TABLE t_v3 (id TEXT PRIMARY KEY);' });
    MIGRATIONS.push({ version: 2, name: 'v2', sql: 'CREATE TABLE t_v2 (id TEXT PRIMARY KEY);' });
    try {
      runMigrations(db);
      expect(getUserVersion(db)).toBe(3);
      expect(tableNames(db)).toContain('t_v2');
      expect(tableNames(db)).toContain('t_v3');

      // user_version 若被写低，这次就会重复建表而失败
      runMigrations(db);
      expect(getUserVersion(db)).toBe(3);
    } finally {
      MIGRATIONS.length = originalLength;
    }
    db.close();
  });

  it('迁移失败时回滚已执行的 DDL、不推进 user_version，且错误信息带版本号', () => {
    const db = freshDb();
    const originalLength = MIGRATIONS.length;
    const failing: Migration = {
      version: 99,
      name: 'failing-migration',
      sql: `
        CREATE TABLE tmp_ok (id TEXT PRIMARY KEY);
        CREATE TABLE tmp_bad (id TEXT PRIMARY KEY, v TEXT NOT NULL UNIQUE);
        INSERT INTO tmp_bad (id, v) VALUES ('a', 'x');
        INSERT INTO tmp_bad (id, v) VALUES ('b', 'x');
      `
    };
    MIGRATIONS.push(failing);
    try {
      expect(() => runMigrations(db)).toThrow(/version=99/);
    } finally {
      MIGRATIONS.length = originalLength;
    }

    const names = tableNames(db);
    expect(names).not.toContain('tmp_ok');
    expect(names).not.toContain('tmp_bad');
    expect(getUserVersion(db)).toBe(1);
    db.close();
  });

  it('迁移后的库通过完整性检查', () => {
    const db = freshDb();
    runMigrations(db);
    expect(isHealthy(db)).toBe(true);
    db.close();
  });

  it('CHECK 约束拒绝非法的任务状态', () => {
    const db = freshDb();
    runMigrations(db);
    const now = new Date().toISOString();
    expect(() =>
      db
        .prepare(
          'INSERT INTO tasks (id, title, status, created_at, updated_at) VALUES (?, ?, ?, ?, ?)'
        )
        .run('t2', '非法状态', 'archived', now, now)
    ).toThrow();
    db.close();
  });
});

describe('withTransaction', () => {
  it('正常提交后写入可见并返回回调结果', () => {
    const db = freshDb();
    runMigrations(db);
    const result = withTransaction(db, () => {
      db.prepare('INSERT INTO settings (key, value) VALUES (?, ?)').run('k1', 'v1');
      return 42;
    });
    expect(result).toBe(42);
    const row = db.prepare('SELECT value FROM settings WHERE key = ?').get('k1') as {
      value: string;
    };
    expect(row.value).toBe('v1');
    db.close();
  });

  it('回调抛错时回滚写入并重抛原始错误', () => {
    const db = freshDb();
    runMigrations(db);
    expect(() =>
      withTransaction(db, () => {
        db.prepare('INSERT INTO settings (key, value) VALUES (?, ?)').run('k2', 'v2');
        throw new Error('original-error');
      })
    ).toThrow('original-error');
    const count = db.prepare('SELECT COUNT(*) AS c FROM settings').get() as { c: number };
    expect(count.c).toBe(0);
    db.close();
  });

  it('事务已被 SQLite 自动回滚时，重抛的仍是原始错误而不是 cannot rollback', () => {
    const db = freshDb();
    runMigrations(db);
    expect(() =>
      withTransaction(db, () => {
        // 模拟 SQLite 在 SQLITE_FULL / SQLITE_IOERR 等致命错误下的自动回滚
        db.exec('ROLLBACK');
        throw new Error('original-error');
      })
    ).toThrow('original-error');
    db.close();
  });
});

describe('openDatabase', () => {
  it('打开内存库不抛错', () => {
    const db = openDatabase(':memory:');
    expect(isHealthy(db)).toBe(true);
    db.close();
  });

  it('开启外键约束（PRAGMA foreign_keys 回读为 1）', () => {
    const db = openDatabase(tempDbPath());
    const row = db.prepare('PRAGMA foreign_keys').get() as { foreign_keys: number };
    expect(row.foreign_keys).toBe(1);
    db.close();
  });

  it('删除项目会级联删除其任务', () => {
    const db = openDatabase(tempDbPath());
    runMigrations(db);
    const now = new Date().toISOString();
    db.prepare('INSERT INTO projects (id, name, created_at, updated_at) VALUES (?, ?, ?, ?)').run(
      'p1',
      '项目一',
      now,
      now
    );
    db.prepare(
      'INSERT INTO tasks (id, project_id, title, created_at, updated_at) VALUES (?, ?, ?, ?, ?)'
    ).run('t1', 'p1', '任务一', now, now);
    db.prepare('DELETE FROM projects WHERE id = ?').run('p1');
    const count = db.prepare('SELECT COUNT(*) AS c FROM tasks').get() as { c: number };
    expect(count.c).toBe(0);
    db.close();
  });

  it('打开损坏文件失败后不会锁住该文件', () => {
    const dbPath = tempDbPath();
    writeFileSync(dbPath, 'this is definitely not a sqlite database');
    expect(() => openDatabase(dbPath)).toThrow();

    // 句柄必须已经释放，否则改名/删除会报 EBUSY
    const renamed = `${dbPath}.renamed`;
    expect(() => renameSync(dbPath, renamed)).not.toThrow();
    expect(() => unlinkSync(renamed)).not.toThrow();
  });
});
