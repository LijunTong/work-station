import { contextBridge, ipcRenderer } from 'electron';
import type {
  CreateProjectInput,
  CreateTaskInput,
  TaskFilter,
  UpdateProjectInput,
  UpdateTaskInput,
  WorkStationApi
} from '../src/types/api';
import type { TaskStatus } from '../src/types/entities';

const api: WorkStationApi = {
  projects: {
    list: (options) => ipcRenderer.invoke('projects:list', options),
    get: (id) => ipcRenderer.invoke('projects:get', id),
    create: (input: CreateProjectInput) => ipcRenderer.invoke('projects:create', input),
    update: (id: string, patch: UpdateProjectInput) =>
      ipcRenderer.invoke('projects:update', id, patch),
    archive: (id) => ipcRenderer.invoke('projects:archive', id),
    unarchive: (id) => ipcRenderer.invoke('projects:unarchive', id),
    remove: (id) => ipcRenderer.invoke('projects:remove', id),
    counts: (id) => ipcRenderer.invoke('projects:counts', id)
  },
  tasks: {
    list: (filter?: TaskFilter) => ipcRenderer.invoke('tasks:list', filter),
    get: (id) => ipcRenderer.invoke('tasks:get', id),
    create: (input: CreateTaskInput) => ipcRenderer.invoke('tasks:create', input),
    update: (id: string, patch: UpdateTaskInput) =>
      ipcRenderer.invoke('tasks:update', id, patch),
    setStatus: (id: string, status: TaskStatus) =>
      ipcRenderer.invoke('tasks:setStatus', id, status),
    remove: (id) => ipcRenderer.invoke('tasks:remove', id),
    countsByStatus: () => ipcRenderer.invoke('tasks:countsByStatus'),
    overdueCount: (nowLocalDateTime: string) =>
      ipcRenderer.invoke('tasks:overdueCount', nowLocalDateTime)
  },
  tags: {
    list: () => ipcRenderer.invoke('tags:list'),
    create: (input) => ipcRenderer.invoke('tags:create', input),
    update: (id, patch) => ipcRenderer.invoke('tags:update', id, patch),
    remove: (id) => ipcRenderer.invoke('tags:remove', id),
    setTaskTags: (taskId: string, tagIds: string[]) =>
      ipcRenderer.invoke('tags:setTaskTags', taskId, tagIds),
    listTaskTags: (taskId: string) => ipcRenderer.invoke('tags:listTaskTags', taskId)
  },
  settings: {
    get: <T>(key: string) => ipcRenderer.invoke('settings:get', key),
    set: (key: string, value: unknown) => ipcRenderer.invoke('settings:set', key, value),
    getAll: () => ipcRenderer.invoke('settings:getAll')
  },
  app: {
    info: () => ipcRenderer.invoke('app:info')
  }
};

contextBridge.exposeInMainWorld('api', api);
