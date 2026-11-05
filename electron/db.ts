import { DatabaseSync } from 'node:sqlite';
import { copyFileSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { MIGRATIONS } from './migrations';

export function openDatabase(filePath: string): DatabaseSync {
  mkdirSync(dirname(filePath), { recursive: true });
  const db = new DatabaseSync(filePath);
  db.exec('PRAGMA journal_mode = WAL');
  db.exec('PRAGMA foreign_keys = ON');
  return db;
}

export function getUserVersion(db: DatabaseSync): number {
  const row = db.prepare('PRAGMA user_version').get() as
    | { user_version?: number }
    | undefined;
  return row?.user_version ?? 0;
}

export function runMigrations(db: DatabaseSync): void {
  const current = getUserVersion(db);
  for (const migration of MIGRATIONS) {
    if (migration.version <= current) continue;
    db.exec('BEGIN');
    try {
      db.exec(migration.sql);
      db.exec(`PRAGMA user_version = ${migration.version}`);
      db.exec('COMMIT');
    } catch (error) {
      db.exec('ROLLBACK');
      throw new Error(
        `迁移失败 (version=${migration.version} ${migration.name}): ${(error as Error).message}`
      );
    }
  }
}

export function isHealthy(db: DatabaseSync): boolean {
  const rows = db.prepare('PRAGMA integrity_check').all() as Array<{
    integrity_check: string;
  }>;
  return rows.length === 1 && rows[0].integrity_check === 'ok';
}

export function withTransaction<T>(db: DatabaseSync, fn: () => T): T {
  db.exec('BEGIN');
  try {
    const result = fn();
    db.exec('COMMIT');
    return result;
  } catch (error) {
    db.exec('ROLLBACK');
    throw error;
  }
}

export function backupDatabase(db: DatabaseSync, targetPath: string): void {
  db.exec('PRAGMA wal_checkpoint(TRUNCATE)');
  // db.location() 在 :memory: 库上返回 null（@types/node 的类型为 string | null）；
  // 生产路径永远传真实文件路径，此处收窄类型以通过类型检查。
  copyFileSync(db.location() as string, targetPath);
}
