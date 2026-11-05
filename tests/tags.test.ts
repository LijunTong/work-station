import { DatabaseSync } from 'node:sqlite';
import { beforeEach, describe, expect, it } from 'vitest';
import { runMigrations } from '../electron/db';
import { createTagsRepo } from '../electron/repositories/tags';
import { createTasksRepo } from '../electron/repositories/tasks';

let db: DatabaseSync;
let tags: ReturnType<typeof createTagsRepo>;
let tasks: ReturnType<typeof createTasksRepo>;

beforeEach(() => {
  db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  runMigrations(db);
  tags = createTagsRepo(db);
  tasks = createTasksRepo(db);
});

describe('标签仓储', () => {
  it('创建后能按 id 取回', () => {
    const created = tags.create({ name: '后端' });
    expect(created.name).toBe('后端');
    expect(created.color).toBeNull();
  });

  it('名称唯一，重复插入会抛错', () => {
    tags.create({ name: '前端' });
    expect(() => tags.create({ name: '前端' })).toThrow();
  });

  it('按名称查询能命中', () => {
    tags.create({ name: '设计' });
    expect(tags.getByName('设计')?.name).toBe('设计');
    expect(tags.getByName('没有')).toBeNull();
  });

  it('列表按名称排序', () => {
    tags.create({ name: 'b' });
    tags.create({ name: 'a' });
    expect(tags.list().map((t) => t.name)).toEqual(['a', 'b']);
  });

  it('给任务设置多个标签', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    const b = tags.create({ name: 'b' });
    tags.setTaskTags(task.id, [a.id, b.id]);
    expect(tags.listTaskTags(task.id).map((t) => t.name)).toEqual(['a', 'b']);
  });

  it('重复设置同一标签不会产生重复关联', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    tags.setTaskTags(task.id, [a.id, a.id]);
    expect(tags.listTaskTags(task.id)).toHaveLength(1);
  });

  it('重新设置标签会替换旧关联', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    const b = tags.create({ name: 'b' });
    tags.setTaskTags(task.id, [a.id]);
    tags.setTaskTags(task.id, [b.id]);
    expect(tags.listTaskTags(task.id).map((t) => t.name)).toEqual(['b']);
  });

  it('清空标签关联后列表为空', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    tags.setTaskTags(task.id, [a.id]);
    tags.setTaskTags(task.id, []);
    expect(tags.listTaskTags(task.id)).toHaveLength(0);
  });

  it('删除标签会解除任务关联但不删任务', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    tags.setTaskTags(task.id, [a.id]);
    expect(tags.remove(a.id)).toBe(true);
    expect(tags.listTaskTags(task.id)).toHaveLength(0);
    expect(tasks.get(task.id)).not.toBeNull();
  });

  it('listTagTaskIds 返回关联的任务 id', () => {
    const task = tasks.create({ title: '任务' });
    const a = tags.create({ name: 'a' });
    tags.setTaskTags(task.id, [a.id]);
    expect(tags.listTagTaskIds(a.id)).toEqual([task.id]);
  });
});
