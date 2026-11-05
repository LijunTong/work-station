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
import { IPC_CHANNELS } from '../src/types/channels';
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
  handle(IPC_CHANNELS.projectsList, (options?: { includeArchived?: boolean }) =>
    projects.list(options)
  );
  handle(IPC_CHANNELS.projectsGet, (id: string) => projects.get(id));
  handle(IPC_CHANNELS.projectsCreate, (input: CreateProjectInput) => projects.create(input));
  handle(IPC_CHANNELS.projectsUpdate, (id: string, patch: UpdateProjectInput) =>
    projects.update(id, patch)
  );
  handle(IPC_CHANNELS.projectsArchive, (id: string) => projects.archive(id));
  handle(IPC_CHANNELS.projectsUnarchive, (id: string) => projects.unarchive(id));
  handle(IPC_CHANNELS.projectsRemove, (id: string) => projects.remove(id));
  handle(IPC_CHANNELS.projectsCounts, (id: string) => projects.countTasks(id));

  handle(IPC_CHANNELS.tasksList, (filter?: TaskFilter) => tasks.list(filter));
  handle(IPC_CHANNELS.tasksGet, (id: string) => tasks.get(id));
  handle(IPC_CHANNELS.tasksCreate, (input: CreateTaskInput) => tasks.create(input));
  handle(IPC_CHANNELS.tasksUpdate, (id: string, patch: UpdateTaskInput) =>
    tasks.update(id, patch)
  );
  handle(IPC_CHANNELS.tasksSetStatus, (id: string, status: TaskStatus) => tasks.setStatus(id, status));
  handle(IPC_CHANNELS.tasksRemove, (id: string) => tasks.remove(id));
  handle(IPC_CHANNELS.tasksCountsByStatus, () => tasks.countByStatus());
  handle(IPC_CHANNELS.tasksOverdueCount, (nowLocalDateTime: string) =>
    tasks.countOverdue(nowLocalDateTime)
  );

  handle(IPC_CHANNELS.tagsList, () => tags.list());
  handle(IPC_CHANNELS.tagsCreate, (input: { name: string; color?: string | null }) =>
    tags.create(input)
  );
  handle(IPC_CHANNELS.tagsUpdate, (id: string, patch: { name?: string; color?: string | null }) =>
    tags.update(id, patch)
  );
  handle(IPC_CHANNELS.tagsRemove, (id: string) => tags.remove(id));
  handle(IPC_CHANNELS.tagsSetTaskTags, (taskId: string, tagIds: string[]) => {
    tags.setTaskTags(taskId, tagIds);
  });
  handle(IPC_CHANNELS.tagsListTaskTags, (taskId: string) => tags.listTaskTags(taskId));

  handle(IPC_CHANNELS.settingsGet, (key: string) => settings.get(key));
  handle(IPC_CHANNELS.settingsSet, (key: string, value: unknown) => {
    settings.set(key, value);
  });
  handle(IPC_CHANNELS.settingsGetAll, () => settings.getAll());

  handle(IPC_CHANNELS.appInfo, () => ({
    version: app.getVersion(),
    dbPath,
    userDataPath: app.getPath('userData')
  }));
}
