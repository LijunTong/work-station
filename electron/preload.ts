import { contextBridge, ipcRenderer } from 'electron';
import type {
  CreateProjectInput,
  CreateTaskInput,
  Result,
  TaskFilter,
  UpdateProjectInput,
  UpdateTaskInput,
  WorkStationApi
} from '../src/types/api';
import { IPC_CHANNELS } from '../src/types/channels';
import type { TaskStatus } from '../src/types/entities';

const api: WorkStationApi = {
  projects: {
    list: (options) => ipcRenderer.invoke(IPC_CHANNELS.projectsList, options),
    get: (id) => ipcRenderer.invoke(IPC_CHANNELS.projectsGet, id),
    create: (input: CreateProjectInput) => ipcRenderer.invoke(IPC_CHANNELS.projectsCreate, input),
    update: (id: string, patch: UpdateProjectInput) =>
      ipcRenderer.invoke(IPC_CHANNELS.projectsUpdate, id, patch),
    archive: (id) => ipcRenderer.invoke(IPC_CHANNELS.projectsArchive, id),
    unarchive: (id) => ipcRenderer.invoke(IPC_CHANNELS.projectsUnarchive, id),
    remove: (id) => ipcRenderer.invoke(IPC_CHANNELS.projectsRemove, id),
    counts: (id) => ipcRenderer.invoke(IPC_CHANNELS.projectsCounts, id)
  },
  tasks: {
    list: (filter?: TaskFilter) => ipcRenderer.invoke(IPC_CHANNELS.tasksList, filter),
    get: (id) => ipcRenderer.invoke(IPC_CHANNELS.tasksGet, id),
    create: (input: CreateTaskInput) => ipcRenderer.invoke(IPC_CHANNELS.tasksCreate, input),
    update: (id: string, patch: UpdateTaskInput) =>
      ipcRenderer.invoke(IPC_CHANNELS.tasksUpdate, id, patch),
    setStatus: (id: string, status: TaskStatus) =>
      ipcRenderer.invoke(IPC_CHANNELS.tasksSetStatus, id, status),
    remove: (id) => ipcRenderer.invoke(IPC_CHANNELS.tasksRemove, id),
    countsByStatus: () => ipcRenderer.invoke(IPC_CHANNELS.tasksCountsByStatus),
    overdueCount: (nowLocalDateTime: string) =>
      ipcRenderer.invoke(IPC_CHANNELS.tasksOverdueCount, nowLocalDateTime)
  },
  tags: {
    list: () => ipcRenderer.invoke(IPC_CHANNELS.tagsList),
    create: (input) => ipcRenderer.invoke(IPC_CHANNELS.tagsCreate, input),
    update: (id, patch) => ipcRenderer.invoke(IPC_CHANNELS.tagsUpdate, id, patch),
    remove: (id) => ipcRenderer.invoke(IPC_CHANNELS.tagsRemove, id),
    setTaskTags: (taskId: string, tagIds: string[]) =>
      ipcRenderer.invoke(IPC_CHANNELS.tagsSetTaskTags, taskId, tagIds),
    listTaskTags: (taskId: string) => ipcRenderer.invoke(IPC_CHANNELS.tagsListTaskTags, taskId)
  },
  settings: {
    get: <T>(key: string) =>
      ipcRenderer.invoke(IPC_CHANNELS.settingsGet, key) as Promise<Result<T | null>>,
    set: (key: string, value: unknown) => ipcRenderer.invoke(IPC_CHANNELS.settingsSet, key, value),
    getAll: () => ipcRenderer.invoke(IPC_CHANNELS.settingsGetAll)
  },
  app: {
    info: () => ipcRenderer.invoke(IPC_CHANNELS.appInfo)
  }
};

contextBridge.exposeInMainWorld('api', api);
