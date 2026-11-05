// 用相对路径而不是 @ 别名：tsconfig.node.json（主进程程序）没有 paths 配置，
// 该文件一旦被 electron/** 引入，别名就会报 TS2307
import type { TaskStatus } from '../types/entities';

export function computeProgress(tasks: Array<{ status: TaskStatus }>): number {
  const counted = tasks.filter((t) => t.status !== 'canceled');
  if (counted.length === 0) return 0;
  const done = counted.filter((t) => t.status === 'done').length;
  return Math.round((done / counted.length) * 100);
}
