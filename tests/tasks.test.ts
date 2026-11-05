import { DatabaseSync } from 'node:sqlite';
import { beforeEach, describe, expect, it } from 'vitest';
import { runMigrations } from '../electron/db';
import { createProjectsRepo } from '../electron/repositories/projects';
import { createTasksRepo } from '../electron/repositories/tasks';

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
});
