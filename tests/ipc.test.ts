import { DatabaseSync } from 'node:sqlite';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { runMigrations } from '../electron/db';
import { registerIpc } from '../electron/ipc';
import { ApiError, call } from '../src/lib/api';
import type { Result } from '../src/types/api';
import type { Project, Tag, Task } from '../src/types/entities';

type Handler = (event: unknown, ...args: unknown[]) => Promise<unknown>;

// electron 模块在 vitest（node 环境）里拿不到真实实现：
// ipcMain.handle 用桩接住注册，app 提供 getVersion / getPath 的替身
const mocks = vi.hoisted(() => ({
  handlers: new Map<string, Handler>(),
  appStub: {
    getVersion: vi.fn(() => '0.1.0'),
    getPath: vi.fn((name: string) => `/tmp/userData/${name}`)
  }
}));

vi.mock('electron', () => ({
  ipcMain: {
    handle: (channel: string, listener: Handler) => {
      mocks.handlers.set(channel, listener);
    }
  },
  app: mocks.appStub
}));

const DB_PATH = '/tmp/work-station/data.db';

/** 契约里 window.api 暴露的全部 channel，缺一即契约破裂 */
const EXPECTED_CHANNELS = [
  'projects:list',
  'projects:get',
  'projects:create',
  'projects:update',
  'projects:archive',
  'projects:unarchive',
  'projects:remove',
  'projects:counts',
  'tasks:list',
  'tasks:get',
  'tasks:create',
  'tasks:update',
  'tasks:setStatus',
  'tasks:remove',
  'tasks:countsByStatus',
  'tasks:overdueCount',
  'tags:list',
  'tags:create',
  'tags:update',
  'tags:remove',
  'tags:setTaskTags',
  'tags:listTaskTags',
  'settings:get',
  'settings:set',
  'settings:getAll',
  'app:info'
];

let db: DatabaseSync;

beforeEach(() => {
  mocks.handlers.clear();
  mocks.appStub.getVersion.mockReset();
  mocks.appStub.getVersion.mockReturnValue('0.1.0');
  mocks.appStub.getPath.mockReset();
  mocks.appStub.getPath.mockImplementation((name: string) => `/tmp/userData/${name}`);
  // handler 失败时会把原始异常打到 console.error，测试里静音以免淹没断言
  vi.spyOn(console, 'error').mockImplementation(() => {});

  db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  runMigrations(db);
  registerIpc(db, DB_PATH);
});

afterEach(() => {
  db.close();
  vi.restoreAllMocks();
});

/** 调用某个 channel；未注册直接失败，避免「测试通过但其实没调到」 */
async function invoke<T>(channel: string, ...args: unknown[]): Promise<Result<T>> {
  const handler = mocks.handlers.get(channel);
  if (!handler) throw new Error(`channel 未注册: ${channel}`);
  return (await handler({}, ...args)) as Result<T>;
}

describe('IPC 路由', () => {
  it('成功路径：handler 返回 ok(data)', async () => {
    const result = await invoke<Project>('projects:create', { name: '项目一' });

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.data.name).toBe('项目一');
      expect(result.data.id).toBeTruthy();
      expect(result.data.status).toBe('planning');
    }
  });

  it('失败路径：仓储抛错时 handler 返回 fail 而不是向外 throw', async () => {
    // dueAt 不是本地墙钟格式，仓储会在 create 内部抛错
    const result = await invoke<Task>('tasks:create', { title: '坏任务', dueAt: '2026-13-45' });

    // 若 handler 向外抛错，这里会 reject 而非 resolve —— 能走到断言即证明没有外抛
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.code).toBe('HANDLER_FAILED');
      expect(result.error.message).toContain('tasks:create');
      expect(result.error.message).toContain('dueAt 必须是本地墙钟格式');
    }
  });

  it('失败路径：依赖抛出非 Error 值时 catch 自身不炸，仍返回 fail', async () => {
    mocks.appStub.getVersion.mockImplementationOnce(() => {
      throw null;
    });

    const result = await invoke<{ version: string }>('app:info');

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.code).toBe('HANDLER_FAILED');
      expect(result.error.message).toContain('未知错误');
    }
  });

  it('projects:list 把 includeArchived 透传给仓储', async () => {
    const created = await invoke<Project>('projects:create', { name: '将被归档的项目' });
    if (!created.ok) throw new Error('前置数据创建失败');
    const id = created.data.id;

    const archived = await invoke<boolean>('projects:archive', id);
    expect(archived.ok).toBe(true);

    // 默认不含归档项目
    const active = await invoke<Project[]>('projects:list');
    expect(active.ok).toBe(true);
    if (active.ok) expect(active.data.map((p) => p.id)).not.toContain(id);

    // 透传 includeArchived 才能取回归档项目
    const all = await invoke<Project[]>('projects:list', { includeArchived: true });
    expect(all.ok).toBe(true);
    if (all.ok) expect(all.data.map((p) => p.id)).toContain(id);
  });

  it('裁决三：setTaskTags 遇未知 id 时报错并被包成 fail，不被静默吞掉', async () => {
    const result = await invoke('tags:setTaskTags', '不存在的任务', ['不存在的标签']);

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error.code).toBe('HANDLER_FAILED');
      expect(result.error.message).toContain('FOREIGN KEY constraint failed');
    }
  });

  it('裁决一：handler 不开外层事务，setTaskTags 可连续调用', async () => {
    const project = await invoke<Project>('projects:create', { name: '项目' });
    if (!project.ok) throw new Error('前置数据创建失败');
    const task = await invoke<Task>('tasks:create', { title: '任务', projectId: project.data.id });
    if (!task.ok) throw new Error('前置数据创建失败');
    const tag = await invoke<Tag>('tags:create', { name: '标签' });
    if (!tag.ok) throw new Error('前置数据创建失败');

    // 若 handler 外层套了 withTransaction，第二次调用会因事务不可重入而失败
    const first = await invoke('tags:setTaskTags', task.data.id, [tag.data.id]);
    expect(first.ok).toBe(true);
    const second = await invoke('tags:setTaskTags', task.data.id, [tag.data.id]);
    expect(second.ok).toBe(true);

    const listed = await invoke<Tag[]>('tags:listTaskTags', task.data.id);
    expect(listed.ok).toBe(true);
    if (listed.ok) expect(listed.data.map((t) => t.id)).toEqual([tag.data.id]);
  });

  it('裁决二：迁移未执行时 registerIpc 直接抛错', () => {
    const bare = new DatabaseSync(':memory:');
    try {
      // 仓储工厂内部即时 db.prepare(...)，表未建好必然抛错
      expect(() => registerIpc(bare, DB_PATH)).toThrow(/no such table/i);
    } finally {
      bare.close();
    }
  });

  it('channel 齐全性：契约里的 channel 全部注册且没有多余注册', () => {
    expect([...mocks.handlers.keys()].sort()).toEqual([...EXPECTED_CHANNELS].sort());
  });

  it('channel 齐全性：每个 channel 都能被调用并返回 ok', async () => {
    const project = await invoke<Project>('projects:create', { name: '项目' });
    if (!project.ok) throw new Error('前置数据创建失败');
    const task = await invoke<Task>('tasks:create', { title: '任务', projectId: project.data.id });
    if (!task.ok) throw new Error('前置数据创建失败');
    const tag = await invoke<Tag>('tags:create', { name: '标签' });
    if (!tag.ok) throw new Error('前置数据创建失败');
    const throwawayProject = await invoke<Project>('projects:create', { name: '待删除项目' });
    if (!throwawayProject.ok) throw new Error('前置数据创建失败');
    const throwawayTask = await invoke<Task>('tasks:create', { title: '待删除任务' });
    if (!throwawayTask.ok) throw new Error('前置数据创建失败');
    const throwawayTag = await invoke<Tag>('tags:create', { name: '待删除标签' });
    if (!throwawayTag.ok) throw new Error('前置数据创建失败');

    const cases: Array<[string, unknown[]]> = [
      ['projects:list', []],
      ['projects:get', [project.data.id]],
      ['projects:create', [{ name: '另一个项目' }]],
      ['projects:update', [project.data.id, { name: '项目（改名）' }]],
      ['projects:archive', [project.data.id]],
      ['projects:unarchive', [project.data.id]],
      ['projects:counts', [project.data.id]],
      ['projects:remove', [throwawayProject.data.id]],
      ['tasks:list', []],
      ['tasks:get', [task.data.id]],
      ['tasks:create', [{ title: '新任务' }]],
      ['tasks:update', [task.data.id, { title: '任务（改名）' }]],
      ['tasks:setStatus', [task.data.id, 'doing']],
      ['tasks:remove', [throwawayTask.data.id]],
      ['tasks:countsByStatus', []],
      ['tasks:overdueCount', ['2026-01-01T00:00']],
      ['tags:list', []],
      ['tags:create', [{ name: '另一个标签' }]],
      ['tags:update', [tag.data.id, { color: '#ff0000' }]],
      ['tags:remove', [throwawayTag.data.id]],
      ['tags:setTaskTags', [task.data.id, [tag.data.id]]],
      ['tags:listTaskTags', [task.data.id]],
      ['settings:get', ['key']],
      ['settings:set', ['key', { a: 1 }]],
      ['settings:getAll', []],
      ['app:info', []]
    ];

    for (const [channel, args] of cases) {
      const result = await invoke(channel, ...args);
      expect(result.ok, channel).toBe(true);
    }
  });

  it('tasks:list 把 tagIds 过滤器透传给仓储', async () => {
    const task = await invoke<Task>('tasks:create', { title: '带标签的任务' });
    if (!task.ok) throw new Error('前置数据创建失败');
    const tag = await invoke<Tag>('tags:create', { name: '筛选标签' });
    if (!tag.ok) throw new Error('前置数据创建失败');
    const linked = await invoke('tags:setTaskTags', task.data.id, [tag.data.id]);
    expect(linked.ok).toBe(true);

    const filtered = await invoke<Task[]>('tasks:list', { tagIds: [tag.data.id] });
    expect(filtered.ok).toBe(true);
    if (filtered.ok) expect(filtered.data.map((t) => t.id)).toEqual([task.data.id]);
  });

  it('tasks:overdueCount 按本地墙钟口径统计逾期任务', async () => {
    await invoke('tasks:create', { title: '已逾期', dueAt: '2026-01-01T00:00' });
    await invoke('tasks:create', { title: '未到期', dueAt: '2027-01-01T00:00' });

    const result = await invoke<number>('tasks:overdueCount', '2026-06-01T00:00');
    expect(result).toEqual({ ok: true, data: 1 });
  });
});

describe('渲染端 API 客户端', () => {
  it('call 在成功时返回 data', async () => {
    const value = await call<number>(async (): Promise<Result<number>> => ({ ok: true, data: 42 }));
    expect(value).toBe(42);
  });

  it('call 在失败时抛出带 code 的 ApiError', async () => {
    const failed = call<number>(
      async (): Promise<Result<number>> => ({
        ok: false,
        error: { code: 'HANDLER_FAILED', message: 'boom' }
      })
    );

    await expect(failed).rejects.toBeInstanceOf(ApiError);
    await expect(failed).rejects.toMatchObject({ code: 'HANDLER_FAILED', message: 'boom' });
  });
});
