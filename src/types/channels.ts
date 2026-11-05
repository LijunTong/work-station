/**
 * IPC 通道名的唯一真源。
 *
 * 必须是**运行时值**而不是 type-only 声明：electron 的 invoke 签名是
 * `invoke(channel: string, ...args: any[]): Promise<any>`（electron.d.ts），
 * 编译器对 channel 字符串零校验。把它收敛成一份被主进程、preload 与测试
 * 共同 import 的常量，channel 名写错才会立刻变成测试失败，而不是
 * 渲染端运行时的 "No handler registered"。
 *
 * 主进程与 preload 侧用相对路径 import（tsconfig.node.json 没有 paths 配置）；
 * 渲染端用 @/types/channels。
 */
export const IPC_CHANNELS = {
  projectsList: 'projects:list',
  projectsGet: 'projects:get',
  projectsCreate: 'projects:create',
  projectsUpdate: 'projects:update',
  projectsArchive: 'projects:archive',
  projectsUnarchive: 'projects:unarchive',
  projectsRemove: 'projects:remove',
  projectsCounts: 'projects:counts',

  tasksList: 'tasks:list',
  tasksGet: 'tasks:get',
  tasksCreate: 'tasks:create',
  tasksUpdate: 'tasks:update',
  tasksSetStatus: 'tasks:setStatus',
  tasksRemove: 'tasks:remove',
  tasksCountsByStatus: 'tasks:countsByStatus',
  tasksOverdueCount: 'tasks:overdueCount',

  tagsList: 'tags:list',
  tagsCreate: 'tags:create',
  tagsUpdate: 'tags:update',
  tagsRemove: 'tags:remove',
  tagsSetTaskTags: 'tags:setTaskTags',
  tagsListTaskTags: 'tags:listTaskTags',

  settingsGet: 'settings:get',
  settingsSet: 'settings:set',
  settingsGetAll: 'settings:getAll',

  appInfo: 'app:info'
} as const;

export type IpcChannel = (typeof IPC_CHANNELS)[keyof typeof IPC_CHANNELS];
