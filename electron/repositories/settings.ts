import type { DatabaseSync } from 'node:sqlite';

// 用 type 而非 interface：node:sqlite 的 all() 返回 Record<string, SQLOutputValue>[]，
// 类型别名（对象类型字面量）才有隐式索引签名，interface 没有，直接断言会报 TS2352
type SettingsValueRow = {
  value: string;
};

type SettingsRow = {
  key: string;
  value: string;
};

export function createSettingsRepo(db: DatabaseSync) {
  const selectOne = db.prepare('SELECT value FROM settings WHERE key = ?');
  const selectAll = db.prepare('SELECT key, value FROM settings');
  const upsert = db.prepare(
    `INSERT INTO settings (key, value) VALUES (?, ?)
     ON CONFLICT(key) DO UPDATE SET value = excluded.value`
  );

  // 局部查询函数：内部调用不依赖返回对象的 this 绑定，
  // 解构（const { get } = repo）或把方法当回调传都不会炸
  function readValue(key: string): unknown {
    const row = selectOne.get(key) as SettingsValueRow | undefined;
    if (!row) return null;
    try {
      return JSON.parse(row.value) as unknown;
    } catch {
      // 损坏的值当作不存在
      return null;
    }
  }

  return {
    get<T>(key: string): T | null {
      return readValue(key) as T | null;
    },

    set(key: string, value: unknown): void {
      // undefined 不能绑定到 node:sqlite（会报 "cannot be bound"），
      // 按 lib/result.ts 的 nullable() 约定统一归一成 JSON null
      upsert.run(key, JSON.stringify(value ?? null));
    },

    remove(key: string): boolean {
      const result = db.prepare('DELETE FROM settings WHERE key = ?').run(key);
      return Number(result.changes) > 0;
    },

    getAll(): Record<string, unknown> {
      const rows = selectAll.all() as SettingsRow[];
      const result: Record<string, unknown> = {};
      for (const row of rows) {
        try {
          result[row.key] = JSON.parse(row.value);
        } catch {
          // 损坏的值直接跳过，不影响其他设置
        }
      }
      return result;
    },

    getString(key: string, fallback: string): string {
      const value = readValue(key);
      return typeof value === 'string' ? value : fallback;
    },

    getBoolean(key: string, fallback: boolean): boolean {
      const value = readValue(key);
      return typeof value === 'boolean' ? value : fallback;
    }
  };
}

export type SettingsRepo = ReturnType<typeof createSettingsRepo>;
