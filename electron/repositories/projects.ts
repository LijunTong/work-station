import type { DatabaseSync } from 'node:sqlite';
import type {
  Priority,
  ProgressMode,
  Project,
  ProjectStatus
} from '../../src/types/entities';
import { newId } from '../util/id';
import { nowIso, toLocalDate } from '../util/time';
import { nullable } from '../lib/result';

// 用 type 而非 interface：node:sqlite 的 all() 返回 Record<string, SQLOutputValue>[]，
// 类型别名（对象类型字面量）才有隐式索引签名，interface 没有，直接断言会报 TS2352
type ProjectRow = {
  id: string;
  name: string;
  description: string;
  status: string;
  priority: string;
  color: string | null;
  progress_mode: string;
  progress_manual: number | null;
  start_date: string | null;
  due_date: string | null;
  sort_order: number;
  created_at: string;
  updated_at: string;
  archived_at: string | null;
};

function mapRow(row: ProjectRow): Project {
  return {
    id: row.id,
    name: row.name,
    description: row.description,
    status: row.status as ProjectStatus,
    priority: row.priority as Priority,
    color: row.color,
    progressMode: row.progress_mode as ProgressMode,
    progressManual: row.progress_manual,
    startDate: row.start_date,
    dueDate: row.due_date,
    sortOrder: row.sort_order,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    archivedAt: row.archived_at
  };
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

export function createProjectsRepo(db: DatabaseSync) {
  const selectAll = db.prepare('SELECT * FROM projects ORDER BY sort_order ASC, created_at ASC');
  const selectActive = db.prepare(
    'SELECT * FROM projects WHERE archived_at IS NULL ORDER BY sort_order ASC, created_at ASC'
  );
  const selectById = db.prepare('SELECT * FROM projects WHERE id = ?');
  const selectByName = db.prepare('SELECT * FROM projects WHERE name = ?');

  return {
    list(options?: { includeArchived?: boolean }): Project[] {
      const stmt = options?.includeArchived ? selectAll : selectActive;
      return (stmt.all() as ProjectRow[]).map(mapRow);
    },

    get(id: string): Project | null {
      const row = selectById.get(id) as ProjectRow | undefined;
      return row ? mapRow(row) : null;
    },

    getByName(name: string): Project | null {
      const row = selectByName.get(name) as ProjectRow | undefined;
      return row ? mapRow(row) : null;
    },

    create(input: CreateProjectInput): Project {
      const id = newId();
      const now = nowIso();
      db.prepare(
        `INSERT INTO projects (
           id, name, description, status, priority, color,
           progress_mode, progress_manual, start_date, due_date,
           sort_order, created_at, updated_at
         ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
      ).run(
        id,
        input.name,
        input.description ?? '',
        input.status ?? 'planning',
        input.priority ?? 'none',
        nullable(input.color),
        input.progressMode ?? 'auto',
        nullable(input.progressManual),
        nullable(input.startDate),
        nullable(input.dueDate),
        0,
        now,
        now
      );
      const created = this.get(id);
      if (!created) throw new Error('项目创建后读取失败');
      return created;
    },

    update(id: string, patch: UpdateProjectInput): Project | null {
      const existing = this.get(id);
      if (!existing) return null;

      // 合并后的记录每个字段都已确定，用 Required 去掉可选性：
      // 否则字段类型会带上 undefined，node:sqlite 不接受 undefined 作为绑定参数
      const merged: Required<UpdateProjectInput> = {
        name: patch.name ?? existing.name,
        description: patch.description ?? existing.description,
        status: patch.status ?? existing.status,
        priority: patch.priority ?? existing.priority,
        color: patch.color === undefined ? existing.color : patch.color,
        progressMode: patch.progressMode ?? existing.progressMode,
        progressManual:
          patch.progressManual === undefined ? existing.progressManual : patch.progressManual,
        startDate: patch.startDate === undefined ? existing.startDate : patch.startDate,
        dueDate: patch.dueDate === undefined ? existing.dueDate : patch.dueDate,
        sortOrder: patch.sortOrder ?? existing.sortOrder,
        archivedAt: patch.archivedAt === undefined ? existing.archivedAt : patch.archivedAt
      };

      db.prepare(
        `UPDATE projects SET
           name = ?, description = ?, status = ?, priority = ?, color = ?,
           progress_mode = ?, progress_manual = ?, start_date = ?, due_date = ?,
           sort_order = ?, updated_at = ?, archived_at = ?
         WHERE id = ?`
      ).run(
        merged.name,
        merged.description,
        merged.status,
        merged.priority,
        nullable(merged.color),
        merged.progressMode,
        nullable(merged.progressManual),
        nullable(merged.startDate),
        nullable(merged.dueDate),
        merged.sortOrder,
        nowIso(),
        nullable(merged.archivedAt),
        id
      );

      return this.get(id);
    },

    archive(id: string): boolean {
      const result = db
        .prepare('UPDATE projects SET archived_at = ?, updated_at = ? WHERE id = ?')
        .run(nowIso(), nowIso(), id);
      return Number(result.changes) > 0;
    },

    unarchive(id: string): boolean {
      const result = db
        .prepare('UPDATE projects SET archived_at = NULL, updated_at = ? WHERE id = ?')
        .run(nowIso(), id);
      return Number(result.changes) > 0;
    },

    remove(id: string): boolean {
      const result = db.prepare('DELETE FROM projects WHERE id = ?').run(id);
      return Number(result.changes) > 0;
    },

    countTasks(projectId: string): { total: number; done: number; overdue: number; doing: number } {
      const now = toLocalDate(new Date());
      const row = db
        .prepare(
          `SELECT
             COUNT(*) AS total,
             SUM(CASE WHEN status = 'done' THEN 1 ELSE 0 END) AS done,
             SUM(CASE WHEN status = 'doing' THEN 1 ELSE 0 END) AS doing,
             SUM(CASE WHEN status NOT IN ('done','canceled') AND due_at IS NOT NULL
                      AND due_at < ? THEN 1 ELSE 0 END) AS overdue
           FROM tasks WHERE project_id = ?`
        )
        .get(now, projectId) as {
        total: number;
        done: number;
        doing: number;
        overdue: number;
      };
      return {
        total: row.total ?? 0,
        done: row.done ?? 0,
        doing: row.doing ?? 0,
        overdue: row.overdue ?? 0
      };
    }
  };
}

export type ProjectsRepo = ReturnType<typeof createProjectsRepo>;
