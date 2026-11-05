import type {
  Project,
  ProjectStatus,
  Priority,
  ProgressMode,
  Tag,
  Task,
  TaskStatus
} from './entities';

export type Result<T> =
  | { ok: true; data: T }
  | { ok: false; error: { code: string; message: string } };

export interface TaskCounts {
  total: number;
  done: number;
  doing: number;
  overdue: number;
}

export interface CreateProjectInput {
  name: string;
  description?: string;
  status?: ProjectStatus;
  priority?: Priority;
  color?: string | null;
  progressMode?: ProgressMode;
  progressManual?: number | null;
  startDate?: string | null;
  dueDate?: string | null;
}

export interface UpdateProjectInput {
  name?: string;
  description?: string;
  status?: ProjectStatus;
  priority?: Priority;
  color?: string | null;
  progressMode?: ProgressMode;
  progressManual?: number | null;
  startDate?: string | null;
  dueDate?: string | null;
  sortOrder?: number;
  archivedAt?: string | null;
}

export interface CreateTaskInput {
  title: string;
  projectId?: string | null;
  notes?: string;
  status?: TaskStatus;
  priority?: Priority;
  dueAt?: string | null;
  remindBefore?: number;
  estimate?: number | null;
  spent?: number | null;
  blockedReason?: string | null;
}

export interface UpdateTaskInput {
  title?: string;
  projectId?: string | null;
  notes?: string;
  status?: TaskStatus;
  priority?: Priority;
  dueAt?: string | null;
  remindBefore?: number;
  estimate?: number | null;
  spent?: number | null;
  blockedReason?: string | null;
  sortOrder?: number;
}

export interface TaskFilter {
  projectId?: string | null;
  status?: TaskStatus[];
  priority?: Priority[];
  tagIds?: string[];
  dueBefore?: string;
  dueAfter?: string;
  includeDone?: boolean;
  search?: string;
}

export interface WorkStationApi {
  projects: {
    list(options?: { includeArchived?: boolean }): Promise<Result<Project[]>>;
    get(id: string): Promise<Result<Project | null>>;
    create(input: CreateProjectInput): Promise<Result<Project>>;
    update(id: string, patch: UpdateProjectInput): Promise<Result<Project | null>>;
    archive(id: string): Promise<Result<boolean>>;
    unarchive(id: string): Promise<Result<boolean>>;
    remove(id: string): Promise<Result<boolean>>;
    counts(projectId: string): Promise<Result<TaskCounts>>;
  };
  tasks: {
    list(filter?: TaskFilter): Promise<Result<Task[]>>;
    get(id: string): Promise<Result<Task | null>>;
    create(input: CreateTaskInput): Promise<Result<Task>>;
    update(id: string, patch: UpdateTaskInput): Promise<Result<Task | null>>;
    setStatus(id: string, status: TaskStatus): Promise<Result<Task | null>>;
    remove(id: string): Promise<Result<boolean>>;
    countsByStatus(): Promise<Result<Record<TaskStatus, number>>>;
    overdueCount(nowLocalDateTime: string): Promise<Result<number>>;
  };
  tags: {
    list(): Promise<Result<Tag[]>>;
    create(input: { name: string; color?: string | null }): Promise<Result<Tag>>;
    update(id: string, patch: { name?: string; color?: string | null }): Promise<Result<Tag | null>>;
    remove(id: string): Promise<Result<boolean>>;
    setTaskTags(taskId: string, tagIds: string[]): Promise<Result<void>>;
    listTaskTags(taskId: string): Promise<Result<Tag[]>>;
  };
  settings: {
    get<T>(key: string): Promise<Result<T | null>>;
    set(key: string, value: unknown): Promise<Result<void>>;
    getAll(): Promise<Result<Record<string, unknown>>>;
  };
  app: {
    info(): Promise<Result<{ version: string; dbPath: string; userDataPath: string }>>;
  };
}
