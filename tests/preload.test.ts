import { describe, expect, it, vi } from 'vitest';
import type { WorkStationApi } from '../src/types/api';
import { IPC_CHANNELS } from '../src/types/channels';

type InvokeCall = { channel: string; args: unknown[] };

const mocks = vi.hoisted(() => ({
  exposedKey: '',
  exposed: undefined as unknown,
  calls: [] as InvokeCall[]
}));

vi.mock('electron', () => ({
  contextBridge: {
    exposeInMainWorld: (key: string, api: unknown) => {
      mocks.exposedKey = key;
      mocks.exposed = api;
    }
  },
  ipcRenderer: {
    invoke: (channel: string, ...args: unknown[]) => {
      mocks.calls.push({ channel, args });
      return Promise.resolve({ ok: true, data: null });
    }
  }
}));

// preload 在模块顶层调用 exposeInMainWorld，导入即完成捕获
import '../electron/preload';

const api = mocks.exposed as WorkStationApi;

/** 每个 window.api 方法 → 它必须使用的 channel */
const METHOD_CASES: Array<[string, () => unknown, string]> = [
  ['projects.list', () => api.projects.list(), IPC_CHANNELS.projectsList],
  ['projects.get', () => api.projects.get('p1'), IPC_CHANNELS.projectsGet],
  ['projects.create', () => api.projects.create({ name: 'p' }), IPC_CHANNELS.projectsCreate],
  ['projects.update', () => api.projects.update('p1', { name: 'p2' }), IPC_CHANNELS.projectsUpdate],
  ['projects.archive', () => api.projects.archive('p1'), IPC_CHANNELS.projectsArchive],
  ['projects.unarchive', () => api.projects.unarchive('p1'), IPC_CHANNELS.projectsUnarchive],
  ['projects.remove', () => api.projects.remove('p1'), IPC_CHANNELS.projectsRemove],
  ['projects.counts', () => api.projects.counts('p1'), IPC_CHANNELS.projectsCounts],
  ['tasks.list', () => api.tasks.list({ includeDone: true }), IPC_CHANNELS.tasksList],
  ['tasks.get', () => api.tasks.get('t1'), IPC_CHANNELS.tasksGet],
  ['tasks.create', () => api.tasks.create({ title: 't' }), IPC_CHANNELS.tasksCreate],
  ['tasks.update', () => api.tasks.update('t1', { title: 't2' }), IPC_CHANNELS.tasksUpdate],
  ['tasks.setStatus', () => api.tasks.setStatus('t1', 'doing'), IPC_CHANNELS.tasksSetStatus],
  ['tasks.remove', () => api.tasks.remove('t1'), IPC_CHANNELS.tasksRemove],
  ['tasks.countsByStatus', () => api.tasks.countsByStatus(), IPC_CHANNELS.tasksCountsByStatus],
  ['tasks.overdueCount', () => api.tasks.overdueCount('2026-01-01T00:00'), IPC_CHANNELS.tasksOverdueCount],
  ['tags.list', () => api.tags.list(), IPC_CHANNELS.tagsList],
  ['tags.create', () => api.tags.create({ name: 'tag' }), IPC_CHANNELS.tagsCreate],
  ['tags.update', () => api.tags.update('g1', { color: '#fff' }), IPC_CHANNELS.tagsUpdate],
  ['tags.remove', () => api.tags.remove('g1'), IPC_CHANNELS.tagsRemove],
  ['tags.setTaskTags', () => api.tags.setTaskTags('t1', ['g1']), IPC_CHANNELS.tagsSetTaskTags],
  ['tags.listTaskTags', () => api.tags.listTaskTags('t1'), IPC_CHANNELS.tagsListTaskTags],
  ['settings.get', () => api.settings.get('theme'), IPC_CHANNELS.settingsGet],
  ['settings.set', () => api.settings.set('theme', 'dark'), IPC_CHANNELS.settingsSet],
  ['settings.getAll', () => api.settings.getAll(), IPC_CHANNELS.settingsGetAll],
  ['app.info', () => api.app.info(), IPC_CHANNELS.appInfo]
];

describe('preload 桥接', () => {
  it("通过 contextBridge 暴露在 'api' 下，且形状与契约一致", () => {
    expect(mocks.exposedKey).toBe('api');
    expect(Object.keys(api).sort()).toEqual(['app', 'projects', 'settings', 'tags', 'tasks']);
  });

  it('26 个方法各自使用正确的 channel，且覆盖 IPC_CHANNELS 全集', async () => {
    for (const [method, call, expectedChannel] of METHOD_CASES) {
      mocks.calls.length = 0;
      await call();

      expect(mocks.calls, method).toHaveLength(1);
      expect(mocks.calls[0].channel, method).toBe(expectedChannel);
    }

    const used = new Set(METHOD_CASES.map(([, , channel]) => channel));
    expect([...used].sort()).toEqual([...Object.values(IPC_CHANNELS)].sort());
  });

  it('入参按契约顺序原样透传给 ipcRenderer.invoke', async () => {
    mocks.calls.length = 0;
    await api.projects.update('p1', { name: '改过' });
    await api.tasks.setStatus('t1', 'done');
    await api.tags.setTaskTags('t1', ['g1', 'g2']);
    await api.settings.set('theme', 'dark');

    expect(mocks.calls).toEqual([
      { channel: IPC_CHANNELS.projectsUpdate, args: ['p1', { name: '改过' }] },
      { channel: IPC_CHANNELS.tasksSetStatus, args: ['t1', 'done'] },
      { channel: IPC_CHANNELS.tagsSetTaskTags, args: ['t1', ['g1', 'g2']] },
      { channel: IPC_CHANNELS.settingsSet, args: ['theme', 'dark'] }
    ]);
  });
});
