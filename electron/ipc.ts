import { ipcMain, app } from 'electron';
import type { DatabaseSync } from 'node:sqlite';
import { fail, ok, type Result } from './lib/result';
import { createProjectsRepo } from './repositories/projects';
import { createTasksRepo } from './repositories/tasks';
import { createTagsRepo } from './repositories/tags';
import { createSettingsRepo } from './repositories/settings';
import type {
  CreateProjectInput,
  CreateTaskInput,
  TaskFilter,
  UpdateProjectInput,
  UpdateTaskInput
} from '../src/types/api';
import type { TaskStatus } from '../src/types/entities';

/**
 * 把任意抛出值转成可读文案。
 * 不能直接写 (error as Error).message：仓储若抛出 null / undefined / 字符串，
 * 读取 .message 本身就会抛 TypeError，把「handler 永远不向外 throw」的契约打破。
 */
function describeError(error: unknown): string {
  if (error instanceof Error) return error.message ?? '未知错误';
  return String(error ?? '未知错误');
}

function handle<TArgs extends unknown[], TResult>(
  channel: string,
  fn: (...args: TArgs) => TResult
): void {
  ipcMain.handle(channel, async (_event, ...args: TArgs): Promise<Result<TResult>> => {
    try {
      return ok(fn(...args));
    } catch (error) {
      const message = describeError(error);
      console.error(`[ipc] ${channel} 失败:`, error);
      return fail('HANDLER_FAILED', `${channel} 执行失败：${message}`);
    }
  });
}

export function registerIpc(db: DatabaseSync, dbPath: string): void {
  const projects = createProjectsRepo(db);
  const tasks = createTasksRepo(db);
  const tags = createTagsRepo(db);
  const settings = createSettingsRepo(db);

  // 入参必须透传给仓储：写死 { includeArchived: false } 会让渲染端永远取不到归档项目
  handle('projects:list', (options?: { includeArchived?: boolean }) => projects.list(options));
  handle('projects:get', (id: string) => projects.get(id));
  handle('projects:create', (input: CreateProjectInput) => projects.create(input));
  handle('projects:update', (id: string, patch: UpdateProjectInput) => projects.update(id, patch));
  handle('projects:archive', (id: string) => projects.archive(id));
  handle('projects:unarchive', (id: string) => projects.unarchive(id));
  handle('projects:remove', (id: string) => projects.remove(id));
  handle('projects:counts', (id: string) => projects.countTasks(id));

  handle('tasks:list', (filter?: TaskFilter) => tasks.list(filter));
  handle('tasks:get', (id: string) => tasks.get(id));
  handle('tasks:create', (input: CreateTaskInput) => tasks.create(input));
  handle('tasks:update', (id: string, patch: UpdateTaskInput) => tasks.update(id, patch));
  handle('tasks:setStatus', (id: string, status: TaskStatus) => tasks.setStatus(id, status));
  handle('tasks:remove', (id: string) => tasks.remove(id));
  handle('tasks:countsByStatus', () => tasks.countByStatus());
  handle('tasks:overdueCount', (nowLocalDateTime: string) => tasks.countOverdue(nowLocalDateTime));

  handle('tags:list', () => tags.list());
  handle('tags:create', (input: { name: string; color?: string | null }) => tags.create(input));
  handle('tags:update', (id: string, patch: { name?: string; color?: string | null }) =>
    tags.update(id, patch)
  );
  handle('tags:remove', (id: string) => tags.remove(id));
  handle('tags:setTaskTags', (taskId: string, tagIds: string[]) => {
    tags.setTaskTags(taskId, tagIds);
  });
  handle('tags:listTaskTags', (taskId: string) => tags.listTaskTags(taskId));

  handle('settings:get', (key: string) => settings.get(key));
  handle('settings:set', (key: string, value: unknown) => {
    settings.set(key, value);
  });
  handle('settings:getAll', () => settings.getAll());

  handle('app:info', () => ({
    version: app.getVersion(),
    dbPath,
    userDataPath: app.getPath('userData')
  }));
}
