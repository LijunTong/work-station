import { DatabaseSync } from 'node:sqlite';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { runMigrations } from '../electron/db';
import { createProjectsRepo } from '../electron/repositories/projects';

// 冻结时间源，让时间相关断言完全确定：
// - nowIso 每次调用返回不同的时刻，用来断言 archive 只取了一次时间戳
// - toLocalDate / toLocalDateTime 固定在同一天，用来断言 countTasks 用的是墙钟口径
//   （固定同一天才能让「跨天口径」与「墙钟口径」产生可区分的结果）
const clock = vi.hoisted(() => ({
  nowIsoCalls: 0,
  toLocalDate: '2026-11-05',
  toLocalDateTime: '2026-11-05T10:00'
}));

vi.mock('../electron/util/time', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../electron/util/time')>();
  return {
    ...actual,
    nowIso: () => {
      clock.nowIsoCalls += 1;
      return new Date(Date.UTC(2026, 10, 5, 9, 0, 0) + clock.nowIsoCalls).toISOString();
    },
    toLocalDate: () => clock.toLocalDate,
    toLocalDateTime: () => clock.toLocalDateTime
  };
});

let db: DatabaseSync;
let repo: ReturnType<typeof createProjectsRepo>;

beforeEach(() => {
  db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  runMigrations(db);
  repo = createProjectsRepo(db);
});

describe('项目仓储', () => {
  it('创建后能按 id 取回', () => {
    const created = repo.create({ name: '重构工作台' });
    expect(created.name).toBe('重构工作台');
    expect(created.status).toBe('planning');
    expect(created.progressMode).toBe('auto');
    expect(created.archivedAt).toBeNull();
    expect(repo.get(created.id)?.id).toBe(created.id);
  });

  it('默认列表不包含已归档项目', () => {
    const a = repo.create({ name: '在做的' });
    const b = repo.create({ name: '做完的' });
    repo.archive(b.id);
    expect(repo.list().map((p) => p.id)).toEqual([a.id]);
  });

  it('带 includeArchived 时返回全部项目', () => {
    repo.create({ name: '在做的' });
    const b = repo.create({ name: '做完的' });
    repo.archive(b.id);
    expect(repo.list({ includeArchived: true })).toHaveLength(2);
  });

  it('取消归档后重新出现在默认列表', () => {
    const b = repo.create({ name: '做完的' });
    repo.archive(b.id);
    repo.unarchive(b.id);
    expect(repo.list()).toHaveLength(1);
  });

  it('更新只覆盖传入的字段', () => {
    const created = repo.create({ name: '原名', description: '原描述', priority: 'high' });
    const updated = repo.update(created.id, { name: '新名' });
    expect(updated?.name).toBe('新名');
    expect(updated?.description).toBe('原描述');
    expect(updated?.priority).toBe('high');
  });

  it('更新可空字段时区分「没传」与「传了 null」', () => {
    const created = repo.create({ name: '可空字段', color: '#ff0000', dueDate: '2026-12-01' });
    // 没传 color：保持原值
    expect(repo.update(created.id, { name: '改名' })?.color).toBe('#ff0000');
    // 传了 color: null：显式清空
    expect(repo.update(created.id, { color: null })?.color).toBeNull();
    // 没传 dueDate：保持原值（不能被 undefined 覆盖成 null）
    expect(repo.update(created.id, { name: '再改名' })?.dueDate).toBe('2026-12-01');
  });

  it('更新不存在的项目返回 null', () => {
    expect(repo.update('missing', { name: 'x' })).toBeNull();
  });

  it('删除项目返回 true，重复删除返回 false', () => {
    const created = repo.create({ name: '待删' });
    expect(repo.remove(created.id)).toBe(true);
    expect(repo.remove(created.id)).toBe(false);
  });

  it('按名称查询能命中', () => {
    repo.create({ name: '唯一名字' });
    expect(repo.getByName('唯一名字')?.name).toBe('唯一名字');
    expect(repo.getByName('不存在')).toBeNull();
  });

  it('手动进度模式可以写入百分比', () => {
    const created = repo.create({ name: '手动进度', progressMode: 'manual', progressManual: 42 });
    expect(repo.get(created.id)?.progressManual).toBe(42);
  });

  it('仓储方法被解构后仍能调用（不依赖 this 绑定）', () => {
    const { create, update, get } = repo;
    const created = create({ name: '解构调用' });
    expect(get(created.id)?.name).toBe('解构调用');
    expect(update(created.id, { name: '解构改名' })?.name).toBe('解构改名');
  });

  it('archive 用同一个时间戳写 archived_at 与 updated_at', () => {
    const created = repo.create({ name: '归档时间戳' });
    repo.archive(created.id);
    const archived = repo.get(created.id);
    expect(archived?.archivedAt).not.toBeNull();
    expect(archived?.archivedAt).toBe(archived?.updatedAt);
  });

  it('countTasks 统计总数、完成数与进行中数', () => {
    const project = repo.create({ name: '统计项目' });
    const now = new Date().toISOString();
    const ins = db.prepare(
      'INSERT INTO tasks (id, project_id, title, status, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?)'
    );
    ins.run('t1', project.id, '一', 'done', now, now);
    ins.run('t2', project.id, '二', 'doing', now, now);
    ins.run('t3', project.id, '三', 'todo', now, now);
    const counts = repo.countTasks(project.id);
    expect(counts.total).toBe(3);
    expect(counts.done).toBe(1);
    expect(counts.doing).toBe(1);
  });

  it('countTasks 对空任务集返回全 0', () => {
    const project = repo.create({ name: '空项目' });
    expect(repo.countTasks(project.id)).toEqual({ total: 0, done: 0, doing: 0, overdue: 0 });
  });

  it('countTasks 把过期未完成的任务计为逾期', () => {
    const project = repo.create({ name: '逾期项目' });
    const now = new Date().toISOString();
    db.prepare(
      'INSERT INTO tasks (id, project_id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?)'
    ).run('t1', project.id, '旧的', 'todo', '2000-01-01T10:00', now, now);
    expect(repo.countTasks(project.id).overdue).toBe(1);
  });

  it('countTasks 不把已过期但已 done/canceled 的任务计为逾期', () => {
    const project = repo.create({ name: '过期但已结束' });
    const now = new Date().toISOString();
    const ins = db.prepare(
      'INSERT INTO tasks (id, project_id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?)'
    );
    ins.run('t1', project.id, '做完了', 'done', '2000-01-01T10:00', now, now);
    ins.run('t2', project.id, '取消了', 'canceled', '2000-01-01T10:00', now, now);
    const counts = repo.countTasks(project.id);
    expect(counts.overdue).toBe(0);
    expect(counts.total).toBe(2);
    expect(counts.done).toBe(1);
  });

  it('countTasks 按本地墙钟判定：今天到期但时刻已过算逾期', () => {
    const project = repo.create({ name: '墙钟口径' });
    const now = new Date().toISOString();
    db.prepare(
      'INSERT INTO tasks (id, project_id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?)'
    ).run('t1', project.id, '今天早上的', 'todo', '2026-11-05T09:00', now, now);
    expect(repo.countTasks(project.id).overdue).toBe(1);
  });

  it('countTasks 按本地墙钟判定：今天到期但时刻未到不算逾期', () => {
    const project = repo.create({ name: '今天稍后' });
    const now = new Date().toISOString();
    db.prepare(
      'INSERT INTO tasks (id, project_id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?)'
    ).run('t1', project.id, '今天晚上的', 'todo', '2026-11-05T23:00', now, now);
    expect(repo.countTasks(project.id).overdue).toBe(0);
  });
});
