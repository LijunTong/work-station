export type ProjectStatus = 'planning' | 'active' | 'paused' | 'done';
export type TaskStatus = 'todo' | 'doing' | 'blocked' | 'done' | 'canceled';
export type Priority = 'high' | 'medium' | 'low' | 'none';
export type ProgressMode = 'auto' | 'manual';
export type ReminderKind = 'task_due' | 'custom';
export type ReminderStatus = 'pending' | 'fired' | 'dismissed' | 'failed' | 'done';
export type RepeatRule = 'none' | 'daily' | 'weekly';
export type ReportKind = 'weekly' | 'monthly';

export interface Project {
  id: string;
  name: string;
  description: string;
  status: ProjectStatus;
  priority: Priority;
  color: string | null;
  progressMode: ProgressMode;
  progressManual: number | null;
  startDate: string | null;
  dueDate: string | null;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
  archivedAt: string | null;
}

export interface Task {
  id: string;
  projectId: string | null;
  title: string;
  notes: string;
  status: TaskStatus;
  priority: Priority;
  dueAt: string | null;
  remindBefore: number;
  estimate: number | null;
  spent: number | null;
  blockedReason: string | null;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
  startedAt: string | null;
  completedAt: string | null;
}

export interface Tag {
  id: string;
  name: string;
  color: string | null;
}

export interface Reminder {
  id: string;
  kind: ReminderKind;
  taskId: string | null;
  title: string;
  remindAt: string;
  repeatRule: RepeatRule;
  channel: 'system' | 'inapp';
  status: ReminderStatus;
  firedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface Report {
  id: string;
  kind: ReportKind;
  periodStart: string;
  periodEnd: string;
  autoMarkdown: string;
  manualText: string;
  finalMarkdown: string;
  createdAt: string;
  updatedAt: string;
}
