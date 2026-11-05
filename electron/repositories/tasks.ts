import type { DatabaseSync } from 'node:sqlite';
import type { Priority, Task, TaskStatus } from '../../src/types/entities';
import { newId } from '../util/id';
import { nowIso } from '../util/time';
import { nullable } from '../lib/result';

// 用 type 而非 interface：node:sqlite 的 all() 返回 Record<string, SQLOutputValue>[]，
// 类型别名（对象类型字面量）才有隐式索引签名，interface 没有，直接断言会报 TS2352
type TaskRow = {
  id: string;
  project_id: string | null;
  title: string;
  notes: string;
  status: string;
  priority: string;
  due_at: string | null;
  remind_before: number;
  estimate: number | null;
  spent: number | null;
  blocked_reason: string | null;
  sort_order: number;
  created_at: string;
  updated_at: string;
  started_at: string | null;
  completed_at: string | null;
};

function mapRow(row: TaskRow): Task {
  return {
    id: row.id,
    projectId: row.project_id,
    title: row.title,
    notes: row.notes,
    status: row.status as TaskStatus,
    priority: row.priority as Priority,
    dueAt: row.due_at,
    remindBefore: row.remind_before,
    estimate: row.estimate,
    spent: row.spent,
    blockedReason: row.blocked_reason,
    sortOrder: row.sort_order,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    startedAt: row.started_at,
    completedAt: row.completed_at
  };
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
  dueBefore?: string;
  dueAfter?: string;
  includeDone?: boolean;
  search?: string;
}

const ALL_STATUSES: TaskStatus[] = ['todo', 'doing', 'blocked', 'done', 'canceled'];

export function createTasksRepo(db: DatabaseSync) {
  const selectById = db.prepare('SELECT * FROM tasks WHERE id = ?');

  // 局部查询函数：内部调用不依赖返回对象的 this 绑定，
  // 解构（const { create } = repo）或把方法当回调传（list().map(repo.get)）都不会炸
  const getById = (id: string): Task | null => {
    const row = selectById.get(id) as TaskRow | undefined;
    return row ? mapRow(row) : null;
  };

  // 局部查询函数：同理不依赖 this，list / listByProject 都委托给它
  const listTasks = (filter?: TaskFilter): Task[] => {
    const where: string[] = [];
    const params: Array<string | number> = [];

    if (filter?.projectId !== undefined) {
      if (filter.projectId === null) {
        where.push('project_id IS NULL');
      } else {
        where.push('project_id = ?');
        params.push(filter.projectId);
      }
    }

    if (filter?.status && filter.status.length > 0) {
      where.push(`status IN (${filter.status.map(() => '?').join(', ')})`);
      params.push(...filter.status);
    } else if (filter?.includeDone === false) {
      where.push(`status IN (${ALL_STATUSES.filter((s) => s !== 'done').map(() => '?').join(', ')})`);
      params.push(...ALL_STATUSES.filter((s) => s !== 'done'));
    }

    if (filter?.priority && filter.priority.length > 0) {
      where.push(`priority IN (${filter.priority.map(() => '?').join(', ')})`);
      params.push(...filter.priority);
    }

    if (filter?.dueBefore) {
      where.push('due_at IS NOT NULL AND due_at < ?');
      params.push(filter.dueBefore);
    }

    if (filter?.dueAfter) {
      where.push('due_at IS NOT NULL AND due_at >= ?');
      params.push(filter.dueAfter);
    }

    if (filter?.search) {
      where.push('(title LIKE ? OR notes LIKE ?)');
      const like = `%${filter.search}%`;
      params.push(like, like);
    }

    const sql = `SELECT * FROM tasks ${
      where.length > 0 ? `WHERE ${where.join(' AND ')}` : ''
    } ORDER BY sort_order ASC, created_at ASC`;

    return (db.prepare(sql).all(...params) as TaskRow[]).map(mapRow);
  };

  // 局部更新函数：setStatus 与 update 共享同一套状态迁移与时间戳语义，
  // 且都不依赖 this 绑定
  const updateTask = (id: string, patch: UpdateTaskInput): Task | null => {
    const existing = getById(id);
    if (!existing) return null;

    const nextStatus = patch.status ?? existing.status;
    const now = nowIso();
    // 首次进入 doing 时写 started_at，已有则保留原值
    const startedAt = nextStatus === 'doing' ? (existing.startedAt ?? now) : existing.startedAt;
    // 进入 done 时写 completed_at；从 done 退回其他状态时清空
    const completedAt = nextStatus === 'done' ? (existing.completedAt ?? now) : null;

    db.prepare(
      `UPDATE tasks SET
         title = ?, project_id = ?, notes = ?, status = ?, priority = ?, due_at = ?,
         remind_before = ?, estimate = ?, spent = ?, blocked_reason = ?, sort_order = ?,
         updated_at = ?, started_at = ?, completed_at = ?
       WHERE id = ?`
    ).run(
      patch.title ?? existing.title,
      patch.projectId === undefined ? existing.projectId : patch.projectId,
      patch.notes ?? existing.notes,
      nextStatus,
      patch.priority ?? existing.priority,
      patch.dueAt === undefined ? existing.dueAt : patch.dueAt,
      patch.remindBefore ?? existing.remindBefore,
      patch.estimate === undefined ? existing.estimate : patch.estimate,
      patch.spent === undefined ? existing.spent : patch.spent,
      patch.blockedReason === undefined ? existing.blockedReason : patch.blockedReason,
      patch.sortOrder ?? existing.sortOrder,
      now,
      startedAt,
      completedAt,
      id
    );

    return getById(id);
  };

  return {
    list(filter?: TaskFilter): Task[] {
      return listTasks(filter);
    },

    get(id: string): Task | null {
      return getById(id);
    },

    create(input: CreateTaskInput): Task {
      const id = newId();
      const now = nowIso();
      const status = input.status ?? 'todo';
      db.prepare(
        `INSERT INTO tasks (
           id, project_id, title, notes, status, priority, due_at, remind_before,
           estimate, spent, blocked_reason, sort_order, created_at, updated_at, started_at, completed_at
         ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
      ).run(
        id,
        nullable(input.projectId),
        input.title,
        input.notes ?? '',
        status,
        input.priority ?? 'none',
        nullable(input.dueAt),
        input.remindBefore ?? 0,
        nullable(input.estimate),
        nullable(input.spent),
        nullable(input.blockedReason),
        0,
        now,
        now,
        status === 'doing' ? now : null,
        status === 'done' ? now : null
      );
      const created = getById(id);
      if (!created) throw new Error('任务创建后读取失败');
      return created;
    },

    update(id: string, patch: UpdateTaskInput): Task | null {
      return updateTask(id, patch);
    },

    setStatus(id: string, status: TaskStatus): Task | null {
      return updateTask(id, { status });
    },

    remove(id: string): boolean {
      const result = db.prepare('DELETE FROM tasks WHERE id = ?').run(id);
      return Number(result.changes) > 0;
    },

    countByStatus(): Record<TaskStatus, number> {
      const rows = db
        .prepare('SELECT status, COUNT(*) AS c FROM tasks GROUP BY status')
        .all() as Array<{ status: TaskStatus; c: number }>;
      const result = { todo: 0, doing: 0, blocked: 0, done: 0, canceled: 0 } as Record<
        TaskStatus,
        number
      >;
      for (const row of rows) result[row.status] = row.c;
      return result;
    },

    countOverdue(nowLocalDateTime: string): number {
      // 本地墙钟口径：due_at 与入参同为 YYYY-MM-DDTHH:mm 本地墙钟字符串，
      // 字典序即时间序；已完成/已取消不计入逾期
      const row = db
        .prepare(
          `SELECT COUNT(*) AS c FROM tasks
           WHERE status NOT IN ('done','canceled')
             AND due_at IS NOT NULL AND due_at < ?`
        )
        .get(nowLocalDateTime) as { c: number };
      return row.c ?? 0;
    },

    listByProject(projectId: string): Task[] {
      return listTasks({ projectId });
    }
  };
}

export type TasksRepo = ReturnType<typeof createTasksRepo>;
