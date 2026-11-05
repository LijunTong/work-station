import { DatabaseSync } from 'node:sqlite';
import { copyFileSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { MIGRATIONS } from './migrations';

export function openDatabase(filePath: string): DatabaseSync {
  mkdirSync(dirname(filePath), { recursive: true });
  const db = new DatabaseSync(filePath);
  try {
    db.exec('PRAGMA journal_mode = WAL');
    db.exec('PRAGMA foreign_keys = ON');
  } catch (error) {
    // 打开失败时必须关闭句柄，否则文件仍被占用（Windows 上改名/删除会报 EBUSY）
    try {
      db.close();
    } catch {
      // 关闭失败不掩盖原始错误
    }
    throw error;
  }
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
  // 必须按版本号升序执行：数组顺序可能是乱的，且 user_version 是绝对值，
  // 乱序执行会把 user_version 写低，导致下次启动重复建表而失败
  const pending = MIGRATIONS.filter((migration) => migration.version > current).sort(
    (a, b) => a.version - b.version
  );
  let applied = current;
  for (const migration of pending) {
    if (migration.version <= applied) {
      throw new Error(
        `迁移版本号必须严格递增 (version=${migration.version} ${migration.name} <= ${applied})`
      );
    }
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
    applied = migration.version;
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
    try {
      db.exec('ROLLBACK');
    } catch {
      // 事务可能已被 SQLite 在致命错误（SQLITE_FULL / SQLITE_IOERR）下自动回滚，
      // 此时 ROLLBACK 本身会抛错；不能让它顶掉原始异常
    }
    throw error;
  }
}

export function backupDatabase(db: DatabaseSync, targetPath: string): void {
  db.exec('PRAGMA wal_checkpoint(TRUNCATE)');
  // db.location() 在 :memory: 库上返回 null（@types/node 的类型为 string | null）；
  // 生产路径永远传真实文件路径，此处收窄类型以通过类型检查。
  copyFileSync(db.location() as string, targetPath);
}
