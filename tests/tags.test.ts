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

  it('更新只覆盖传入的字段', () => {
    const created = tags.create({ name: '原名', color: '#ff0000' });
    const updated = tags.update(created.id, { name: '新名' });
    expect(updated?.name).toBe('新名');
    // 没传 color：必须保留原值，不能被 undefined 覆盖
    expect(updated?.color).toBe('#ff0000');
  });

  it('更新可空字段时区分「没传」与「传了 null」', () => {
    const created = tags.create({ name: '可空字段', color: '#ff0000' });
    // 没传 color：保持原值
    expect(tags.update(created.id, { name: '改名' })?.color).toBe('#ff0000');
    // 传了 color: null：显式清空
    expect(tags.update(created.id, { color: null })?.color).toBeNull();
  });

  it('更新不存在的标签返回 null', () => {
    expect(tags.update('missing', { name: 'x' })).toBeNull();
  });

  it('仓储方法被解构后仍能调用（不依赖 this 绑定）', () => {
    const { create, update, get } = tags;
    const created = create({ name: '解构调用' });
    expect(get(created.id)?.name).toBe('解构调用');
    // 查不到的行返回 null（stmt.get() 返回 undefined，仓储统一归一成 null）
    expect(get('missing')).toBeNull();
    expect(update(created.id, { name: '解构改名' })?.name).toBe('解构改名');
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
