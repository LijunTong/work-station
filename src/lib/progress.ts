import type { TaskStatus } from '@/types/entities';

export function computeProgress(tasks: Array<{ status: TaskStatus }>): number {
  const counted = tasks.filter((t) => t.status !== 'canceled');
  if (counted.length === 0) return 0;
  const done = counted.filter((t) => t.status === 'done').length;
  return Math.round((done / counted.length) * 100);
}
