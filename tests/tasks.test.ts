import { DatabaseSync } from 'node:sqlite';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { runMigrations } from '../electron/db';
import { createProjectsRepo } from '../electron/repositories/projects';
import { createTasksRepo } from '../electron/repositories/tasks';

// 冻结时钟：nowIso 每次调用返回一个递增且互不相同的时刻。
// 「状态迁移时已有时间戳保留原值」这类语义必须靠这个才能被验证 ——
// 若实现每次都重写 now，真实时钟在同一次测试里可能落在同一毫秒，
// 保留与重写会得到同一个字符串，断言就成了恒真。
// 其余导出（isLocalDateTime 等）保持真实实现。
const clock = vi.hoisted(() => ({
  nowIsoCalls: 0
}));

vi.mock('../electron/util/time', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../electron/util/time')>();
  return {
    ...actual,
    nowIso: () => {
      clock.nowIsoCalls += 1;
      return new Date(Date.UTC(2026, 10, 5, 9, 0, 0) + clock.nowIsoCalls).toISOString();
    }
  };
});

let db: DatabaseSync;
let tasks: ReturnType<typeof createTasksRepo>;
let projects: ReturnType<typeof createProjectsRepo>;

beforeEach(() => {
  db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  runMigrations(db);
  tasks = createTasksRepo(db);
  projects = createProjectsRepo(db);
});

describe('任务仓储', () => {
  it('创建后能按 id 取回，默认状态为 todo', () => {
    const created = tasks.create({ title: '写周报' });
    expect(created.title).toBe('写周报');
    expect(created.status).toBe('todo');
    expect(created.projectId).toBeNull();
    expect(created.startedAt).toBeNull();
    expect(created.completedAt).toBeNull();
  });

  it('独立任务的项目 id 为 null', () => {
    const project = projects.create({ name: 'P' });
    const task = tasks.create({ title: '独立' });
    expect(task.projectId).toBeNull();
    expect(project.id).not.toBe(task.projectId);
  });

  it('可关联到项目', () => {
    const project = projects.create({ name: 'P' });
    const task = tasks.create({ title: '关联任务', projectId: project.id });
    expect(task.projectId).toBe(project.id);
    expect(tasks.list({ projectId: project.id })).toHaveLength(1);
  });

  it('按项目筛选与按 null 筛选互不干扰', () => {
    const project = projects.create({ name: 'P' });
    tasks.create({ title: 'A', projectId: project.id });
    tasks.create({ title: 'B' });
    expect(tasks.list({ projectId: null })).toHaveLength(1);
    expect(tasks.list({ projectId: project.id })).toHaveLength(1);
  });

  it('切到 doing 会写入 started_at', () => {
    const task = tasks.create({ title: '开始' });
    const updated = tasks.setStatus(task.id, 'doing');
    expect(updated?.status).toBe('doing');
    expect(updated?.startedAt).not.toBeNull();
  });

  it('切到 done 会写入 completed_at', () => {
    const task = tasks.create({ title: '完成' });
    const updated = tasks.setStatus(task.id, 'done');
    expect(updated?.completedAt).not.toBeNull();
  });

  it('从 done 退回 todo 会清空 completed_at', () => {
    const task = tasks.create({ title: '反悔' });
    tasks.setStatus(task.id, 'done');
    const updated = tasks.setStatus(task.id, 'todo');
    expect(updated?.completedAt).toBeNull();
  });

  it('按状态筛选', () => {
    tasks.create({ title: 'A', status: 'todo' });
    tasks.create({ title: 'B', status: 'doing' });
    expect(tasks.list({ status: ['doing'] })).toHaveLength(1);
  });

  it('includeDone 为 false 时排除已完成', () => {
    tasks.create({ title: 'A', status: 'done' });
    tasks.create({ title: 'B', status: 'todo' });
    expect(tasks.list({ includeDone: false })).toHaveLength(1);
  });

  it('搜索匹配标题与备注', () => {
    tasks.create({ title: '写周报' });
    tasks.create({ title: '别的', notes: '顺便写周报' });
    expect(tasks.list({ search: '周报' })).toHaveLength(2);
  });

  it('countByStatus 统计各状态数量', () => {
    tasks.create({ title: 'A', status: 'todo' });
    tasks.create({ title: 'B', status: 'todo' });
    tasks.create({ title: 'C', status: 'done' });
    const counts = tasks.countByStatus();
    expect(counts.todo).toBe(2);
    expect(counts.done).toBe(1);
  });

  it('countOverdue 统计过期未完成任务', () => {
    const now = new Date().toISOString();
    db.prepare(
      'INSERT INTO tasks (id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?)'
    ).run('t1', '过期', 'todo', '2000-01-01T10:00', now, now);
    db.prepare(
      'INSERT INTO tasks (id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?)'
    ).run('t2', '未过期', 'todo', '2099-01-01T10:00', now, now);
    db.prepare(
      'INSERT INTO tasks (id, title, status, due_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?)'
    ).run('t3', '已完成过期', 'done', '2000-01-01T10:00', now, now);
    expect(tasks.countOverdue('2026-11-05T12:00')).toBe(1);
  });

  it('删除任务返回 true', () => {
    const task = tasks.create({ title: '待删' });
    expect(tasks.remove(task.id)).toBe(true);
    expect(tasks.get(task.id)).toBeNull();
  });

  it('更新可空字段时区分「没传」与「传了 null」，且仓储方法解构后仍能调用', () => {
    // 解构后调用：实现若依赖 this 绑定，这里会直接抛错
    const { create, update, setStatus, listByProject } = tasks;
    const created = create({
      title: '可空字段',
      dueAt: '2026-12-01T10:00',
      blockedReason: '等待接口'
    });
    // 没传 dueAt / blockedReason：保持原值（不能被 undefined 覆盖成 null）
    expect(update(created.id, { title: '改名' })?.dueAt).toBe('2026-12-01T10:00');
    expect(update(created.id, { title: '再改名' })?.blockedReason).toBe('等待接口');
    // 传了 null：显式清空
    expect(update(created.id, { dueAt: null })?.dueAt).toBeNull();
    expect(update(created.id, { blockedReason: null })?.blockedReason).toBeNull();
    // setStatus / listByProject 同样不依赖 this 绑定
    expect(setStatus(created.id, 'doing')?.status).toBe('doing');
    expect(listByProject('不存在的项目')).toEqual([]);
  });

  it('更新不存在的任务返回 null', () => {
    expect(tasks.update('missing', { title: 'x' })).toBeNull();
  });

  it('dueAt 不是本地墙钟格式时 create/update 都抛错，不写入脏数据', () => {
    // ISO UTC 串与墙钟串字典序不可比：'2026-11-05T06:30:00.000Z' < '2026-11-05T14:30'
    // 会把当天 18:00 到期的任务判成逾期，必须在写入前就失败
    expect(() => tasks.create({ title: 'UTC 串', dueAt: '2026-11-05T06:30:00.000Z' })).toThrow();
    expect(tasks.list()).toHaveLength(0);

    // 纯日期同样不是墙钟时刻格式
    expect(() => tasks.create({ title: '纯日期', dueAt: '2026-11-05' })).toThrow();
    expect(tasks.list()).toHaveLength(0);

    // 合法墙钟串可以正常写入
    const ok = tasks.create({ title: '合法', dueAt: '2026-11-05T18:00' });
    expect(ok.dueAt).toBe('2026-11-05T18:00');

    // update 走同一条校验：抛错时不能把脏数据写进去，也不能动到原有截止时间
    expect(() => tasks.update(ok.id, { dueAt: '2026-11-05T06:30:00.000Z' })).toThrow();
    expect(tasks.get(ok.id)?.dueAt).toBe('2026-11-05T18:00');

    // dueAt 为 null 表示无截止时间，不校验
    expect(tasks.create({ title: '无截止', dueAt: null }).dueAt).toBeNull();
    expect(tasks.update(ok.id, { dueAt: null })?.dueAt).toBeNull();
  });

  it('状态迁移「已有则保留原值」：started_at 与 completed_at 都不被重写', () => {
    const task = tasks.create({ title: '时间戳' });

    // doing→done：started_at 保留首次进入 doing 写入的值，不被重写
    const doing = tasks.setStatus(task.id, 'doing');
    const startedAt = doing?.startedAt;
    expect(startedAt).not.toBeNull();
    const done = tasks.setStatus(task.id, 'done');
    expect(done?.startedAt).toBe(startedAt);

    // done→done：completed_at 保留第一次写入的值，不被重写
    const completedAt = done?.completedAt;
    expect(completedAt).not.toBeNull();
    const doneAgain = tasks.setStatus(task.id, 'done');
    expect(doneAgain?.completedAt).toBe(completedAt);

    // doing→blocked：started_at 同样保留（状态变了，但开始时间不变）
    const other = tasks.create({ title: '卡住' });
    const otherDoing = tasks.setStatus(other.id, 'doing');
    const otherStartedAt = otherDoing?.startedAt;
    expect(otherStartedAt).not.toBeNull();
    const blocked = tasks.setStatus(other.id, 'blocked');
    expect(blocked?.status).toBe('blocked');
    expect(blocked?.startedAt).toBe(otherStartedAt);
  });

  it('dueBefore / dueAfter 按墙钟字符串过滤，边界相等归 dueAfter', () => {
    const yesterday = tasks.create({ title: '昨天', dueAt: '2026-11-04T12:00' });
    const today = tasks.create({ title: '今天', dueAt: '2026-11-05T12:00' });
    const tomorrow = tasks.create({ title: '明天', dueAt: '2026-11-06T12:00' });

    // due_at < 边界：只返回昨天那条
    expect(tasks.list({ dueBefore: '2026-11-05T12:00' }).map((t) => t.id)).toEqual([yesterday.id]);

    // due_at >= 边界：今天与明天都返回（等于边界的归 dueAfter，因为 SQL 是 >=）
    expect(tasks.list({ dueAfter: '2026-11-05T12:00' }).map((t) => t.id)).toEqual([
      today.id,
      tomorrow.id
    ]);

    // 边界值相等时的归属钉死：同一时刻只出现在 dueAfter 结果里
    expect(tasks.list({ dueBefore: '2026-11-05T12:00' }).map((t) => t.title)).not.toContain('今天');
    expect(tasks.list({ dueAfter: '2026-11-05T12:00' }).map((t) => t.title)).toContain('今天');
  });

  it('按标签筛选：tagIds 命中 task_tags 关联，空数组不过滤', () => {
    const tagged = tasks.create({ title: '有标签' });
    const other = tasks.create({ title: '另一个标签' });
    const untagged = tasks.create({ title: '没标签' });

    // 直接写 task_tags：标签仓储（Task 6）尚未实现，这里只验证筛选 SQL。
    // task_tags 对 tags(id) 有外键约束（PRAGMA foreign_keys = ON），所以先建标签行
    db.prepare('INSERT INTO tags (id, name) VALUES (?, ?)').run('tag-a', '标签A');
    db.prepare('INSERT INTO tags (id, name) VALUES (?, ?)').run('tag-b', '标签B');
    db.prepare('INSERT INTO task_tags (task_id, tag_id) VALUES (?, ?)').run(tagged.id, 'tag-a');
    db.prepare('INSERT INTO task_tags (task_id, tag_id) VALUES (?, ?)').run(other.id, 'tag-b');

    // 单个标签：只返回带该标签的任务，没标签关联的任务自然不入选
    expect(tasks.list({ tagIds: ['tag-a'] }).map((t) => t.id)).toEqual([tagged.id]);

    // 多个标签：命中任一即可
    expect(tasks.list({ tagIds: ['tag-a', 'tag-b'] }).map((t) => t.id)).toEqual([
      tagged.id,
      other.id
    ]);

    // 空数组 = 不过滤（与 status/priority 空数组的既有语义一致），返回全部
    expect(tasks.list({ tagIds: [] }).map((t) => t.id)).toEqual([
      tagged.id,
      other.id,
      untagged.id
    ]);
  });
});
