import { DatabaseSync } from 'node:sqlite';
import { describe, expect, it } from 'vitest';
import { getUserVersion, isHealthy, openDatabase, runMigrations } from '../electron/db';
import { MIGRATIONS } from '../electron/migrations';

function freshDb(): DatabaseSync {
  const db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  return db;
}

describe('数据库迁移', () => {
  it('空库的 user_version 为 0', () => {
    expect(getUserVersion(freshDb())).toBe(0);
  });

  it('迁移后 user_version 等于最新迁移版本号', () => {
    const db = freshDb();
    runMigrations(db);
    const latest = Math.max(...MIGRATIONS.map((m) => m.version));
    expect(getUserVersion(db)).toBe(latest);
  });

  it('迁移会创建全部七张核心表', () => {
    const db = freshDb();
    runMigrations(db);
    const names = db
      .prepare("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
      .all()
      .map((r) => (r as { name: string }).name)
      .sort();
    expect(names).toEqual(
      ['projects', 'reminders', 'reports', 'settings', 'tags', 'task_tags', 'tasks'].sort()
    );
  });

  it('重复执行迁移是幂等的', () => {
    const db = freshDb();
    runMigrations(db);
    runMigrations(db);
    const latest = Math.max(...MIGRATIONS.map((m) => m.version));
    expect(getUserVersion(db)).toBe(latest);
  });

  it('迁移后的库通过完整性检查', () => {
    const db = freshDb();
    runMigrations(db);
    expect(isHealthy(db)).toBe(true);
  });

  it('外键约束生效：删除项目会级联删除其任务', () => {
    const db = freshDb();
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
  });
});

describe('openDatabase', () => {
  it('打开内存库不抛错', () => {
    const db = openDatabase(':memory:');
    expect(isHealthy(db)).toBe(true);
    db.close();
  });
});
