import type { DatabaseSync } from 'node:sqlite';
import type { Tag } from '../../src/types/entities';
import { newId } from '../util/id';
import { nullable } from '../lib/result';
import { withTransaction } from '../db';

// 用 type 而非 interface：node:sqlite 的 all() 返回 Record<string, SQLOutputValue>[]，
// 类型别名（对象类型字面量）才有隐式索引签名，interface 没有，直接断言会报 TS2352
type TagRow = {
  id: string;
  name: string;
  color: string | null;
};

type TaskTagRow = {
  task_id: string;
};

function mapRow(row: TagRow): Tag {
  return { id: row.id, name: row.name, color: row.color };
}

export function createTagsRepo(db: DatabaseSync) {
  const selectAll = db.prepare('SELECT * FROM tags ORDER BY name ASC');
  const selectById = db.prepare('SELECT * FROM tags WHERE id = ?');
  const selectByName = db.prepare('SELECT * FROM tags WHERE name = ?');

  // 局部查询函数：内部调用不依赖返回对象的 this 绑定，
  // 解构（const { create } = repo）或把方法当回调传（list().map(repo.get)）都不会炸
  function getById(id: string): Tag | null {
    const row = selectById.get(id) as TagRow | undefined;
    return row ? mapRow(row) : null;
  }

  return {
    list(): Tag[] {
      return (selectAll.all() as TagRow[]).map(mapRow);
    },

    get(id: string): Tag | null {
      return getById(id);
    },

    getByName(name: string): Tag | null {
      const row = selectByName.get(name) as TagRow | undefined;
      return row ? mapRow(row) : null;
    },

    create(input: { name: string; color?: string | null }): Tag {
      const id = newId();
      db.prepare('INSERT INTO tags (id, name, color) VALUES (?, ?, ?)').run(
        id,
        input.name,
        nullable(input.color)
      );
      const created = getById(id);
      if (!created) throw new Error('标签创建后读取失败');
      return created;
    },

    update(id: string, patch: { name?: string; color?: string | null }): Tag | null {
      const existing = getById(id);
      if (!existing) return null;

      // color 可空：没传（undefined）保留原值，传 null 是显式清空
      const name = patch.name ?? existing.name;
      const color = patch.color === undefined ? existing.color : patch.color;

      db.prepare('UPDATE tags SET name = ?, color = ? WHERE id = ?').run(name, color, id);

      return getById(id);
    },

    remove(id: string): boolean {
      const result = db.prepare('DELETE FROM tags WHERE id = ?').run(id);
      return Number(result.changes) > 0;
    },

    setTaskTags(taskId: string, tagIds: string[]): void {
      // 用 withTransaction 而不是手写 BEGIN/COMMIT/ROLLBACK：
      // SQLite 在致命错误（SQLITE_FULL / SQLITE_IOERR）下会自动回滚，
      // 此时手写的 ROLLBACK 自身会抛错，把原始异常顶掉。
      // withTransaction 内部已处理该场景，保证抛出的是真正的失败原因。
      withTransaction(db, () => {
        db.prepare('DELETE FROM task_tags WHERE task_id = ?').run(taskId);
        const insert = db.prepare(
          'INSERT OR IGNORE INTO task_tags (task_id, tag_id) VALUES (?, ?)'
        );
        // Set 去重：task_tags 主键是 (task_id, tag_id)，重复插入会撞唯一约束
        for (const tagId of new Set(tagIds)) insert.run(taskId, tagId);
      });
    },

    listTaskTags(taskId: string): Tag[] {
      const rows = db
        .prepare(
          `SELECT t.* FROM tags t
           JOIN task_tags tt ON tt.tag_id = t.id
           WHERE tt.task_id = ?
           ORDER BY t.name ASC`
        )
        .all(taskId) as TagRow[];
      return rows.map(mapRow);
    },

    listTagTaskIds(tagId: string): string[] {
      const rows = db
        .prepare('SELECT task_id FROM task_tags WHERE tag_id = ?')
        .all(tagId) as TaskTagRow[];
      return rows.map((r) => r.task_id);
    }
  };
}

export type TagsRepo = ReturnType<typeof createTagsRepo>;
