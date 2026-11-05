import { useCallback, useMemo, useState } from 'react';
import { Badge } from '@/components/Badge';
import { Drawer } from '@/components/Drawer';
import { EmptyState } from '@/components/EmptyState';
import { Modal } from '@/components/Modal';
import { call } from '@/lib/api';
import { formatLocalDateTime, isOverdue, toLocalDate, toLocalDateTime } from '@/lib/format';
import { useAsync } from '@/hooks/useAsync';
import { useUiStore } from '@/store/ui';
import type { Project, Task, TaskStatus } from '@/types/entities';
import { QuickAdd } from './QuickAdd';
import { TaskForm } from './TaskForm';

const STATUS_LABEL: Record<TaskStatus, string> = {
  todo: '待办',
  doing: '进行中',
  blocked: '已阻塞',
  done: '已完成',
  canceled: '已取消'
};

const PRIORITY_TONE: Record<string, 'danger' | 'warning' | 'accent' | 'default'> = {
  high: 'danger',
  medium: 'warning',
  low: 'accent',
  none: 'default'
};

const PRIORITY_LABEL: Record<string, string> = {
  high: '高',
  medium: '中',
  low: '低',
  none: '无'
};

type GroupKey = 'overdue' | 'today' | 'week' | 'later' | 'someday';

function groupOf(task: Task, nowDate: string, weekEndDate: string): GroupKey {
  if (!task.dueAt) return 'someday';
  const dueDate = task.dueAt.slice(0, 10);
  if (dueDate < nowDate) return 'overdue';
  if (dueDate === nowDate) return 'today';
  if (dueDate <= weekEndDate) return 'week';
  return 'later';
}

const GROUP_LABEL: Record<GroupKey, string> = {
  overdue: '逾期',
  today: '今日',
  week: '本周',
  later: '以后',
  someday: '无期限'
};

// 列表渲染顺序遵循设计文档：今日 / 逾期 / 本周 / 以后 / 无期限
const GROUP_ORDER: GroupKey[] = ['today', 'overdue', 'week', 'later', 'someday'];

type CollapseKey = GroupKey | 'completed';

export function TaskPage(): React.ReactElement {
  const showToast = useUiStore((s) => s.showToast);

  const tasksAsync = useAsync<Task[]>(() => call(() => window.api.tasks.list()), []);
  const projectsAsync = useAsync<Project[]>(() => call(() => window.api.projects.list()), []);

  const [editing, setEditing] = useState<Task | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<Task | null>(null);
  const [statusFilter, setStatusFilter] = useState<TaskStatus[]>([]);
  // 记录被折叠的分组；默认全部展开
  const [collapsed, setCollapsed] = useState<Set<CollapseKey>>(() => new Set<CollapseKey>());

  const tasks = tasksAsync.data ?? [];
  const projects = projectsAsync.data ?? [];

  const now = new Date();
  // 墙钟串统一走 Task 10 的 toLocalDate / toLocalDateTime（与主进程同实现），不要手拼
  const nowDate = toLocalDate(now);
  const nowDateTime = toLocalDateTime(now);
  // 本周以周一为首日、结束于周日
  const daysUntilSunday = now.getDay() === 0 ? 0 : 7 - now.getDay();
  const weekEnd = new Date(now);
  weekEnd.setDate(weekEnd.getDate() + daysUntilSunday);
  const weekEndDate = toLocalDate(weekEnd);

  const visible = useMemo(
    () =>
      statusFilter.length === 0
        ? tasks
        : tasks.filter((task) => statusFilter.includes(task.status)),
    [tasks, statusFilter]
  );

  const groups = useMemo(() => {
    const map = new Map<GroupKey, Task[]>();
    for (const task of visible) {
      if (task.status === 'done' || task.status === 'canceled') continue;
      const key = groupOf(task, nowDate, weekEndDate);
      const bucket = map.get(key) ?? [];
      bucket.push(task);
      map.set(key, bucket);
    }
    return map;
  }, [visible, nowDate, weekEndDate]);

  const completed = useMemo(
    () => visible.filter((task) => task.status === 'done' || task.status === 'canceled'),
    [visible]
  );

  const toggleGroup = (key: CollapseKey): void => {
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  };

  const reload = useCallback(() => {
    tasksAsync.reload();
    projectsAsync.reload();
  }, [tasksAsync, projectsAsync]);

  const handleCreate = async (input: {
    title: string;
    projectId: string | null;
    notes: string;
    status: TaskStatus;
    priority: Task['priority'];
    dueAt: string | null;
    estimate: number | null;
    spent: number | null;
    blockedReason: string | null;
  }): Promise<void> => {
    setSubmitting(true);
    try {
      if (editing) {
        await call(() => window.api.tasks.update(editing.id, input));
        showToast('任务已更新');
      } else {
        await call(() => window.api.tasks.create(input));
        showToast('任务已添加');
      }
      setDrawerOpen(false);
      setEditing(null);
      reload();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '保存失败', 'error');
    } finally {
      setSubmitting(false);
    }
  };

  const handleToggleDone = async (task: Task): Promise<void> => {
    try {
      await call(() => window.api.tasks.setStatus(task.id, 'done'));
      reload();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '操作失败', 'error');
    }
  };

  const handleDelete = async (): Promise<void> => {
    if (!pendingDelete) return;
    try {
      await call(() => window.api.tasks.remove(pendingDelete.id));
      showToast('任务已删除');
      setPendingDelete(null);
      reload();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '删除失败', 'error');
    }
  };

  const projectName = (projectId: string | null): string => {
    if (!projectId) return '独立任务';
    return projects.find((p) => p.id === projectId)?.name ?? '未知项目';
  };

  const renderTask = (task: Task): React.ReactElement => {
    const finished = task.status === 'done' || task.status === 'canceled';
    return (
      <li key={task.id} className={finished ? 'task-item task-item-done' : 'task-item'}>
        {finished ? (
          <span className="task-check task-check-done" aria-hidden="true" />
        ) : (
          <button
            type="button"
            className="task-check"
            aria-label="标记为已完成"
            onClick={() => void handleToggleDone(task)}
          />
        )}
        <div
          className="task-body"
          onClick={() => {
            setEditing(task);
            setDrawerOpen(true);
          }}
        >
          <div className="task-title">{task.title}</div>
          <div className="task-meta">
            <span>{projectName(task.projectId)}</span>
            {task.dueAt && (
              <span
                className={
                  !finished && isOverdue(task.dueAt, nowDateTime) ? 'task-due-overdue' : ''
                }
              >
                截止 {formatLocalDateTime(task.dueAt)}
              </span>
            )}
            {task.status === 'blocked' && <Badge tone="warning">已阻塞</Badge>}
            {task.status === 'done' && <Badge tone="success">已完成</Badge>}
            {task.priority !== 'none' && (
              <Badge tone={PRIORITY_TONE[task.priority]}>
                {PRIORITY_LABEL[task.priority] ?? task.priority}
              </Badge>
            )}
          </div>
        </div>
        <button
          type="button"
          className="btn btn-sm btn-danger"
          onClick={() => setPendingDelete(task)}
        >
          删除
        </button>
      </li>
    );
  };

  const renderGroup = (
    key: CollapseKey,
    label: string,
    bucket: Task[]
  ): React.ReactElement => {
    const isCollapsed = collapsed.has(key);
    return (
      <div key={key} className="task-group">
        <button
          type="button"
          className="task-group-title task-group-toggle"
          aria-expanded={!isCollapsed}
          onClick={() => toggleGroup(key)}
        >
          <span className="task-group-arrow" aria-hidden="true">
            {isCollapsed ? '▸' : '▾'}
          </span>
          {label}
          <span className="task-group-count">{bucket.length}</span>
        </button>
        {!isCollapsed && <ul className="task-list">{bucket.map(renderTask)}</ul>}
      </div>
    );
  };

  return (
    <section className="task-page">
      <header className="page-header">
        <h2>任务</h2>
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => {
            setEditing(null);
            setDrawerOpen(true);
          }}
        >
          新建任务
        </button>
      </header>

      <QuickAdd onCreated={reload} />

      <div className="filter-bar">
        {(['todo', 'doing', 'blocked', 'done'] as TaskStatus[]).map((status) => (
          <button
            key={status}
            type="button"
            className={statusFilter.includes(status) ? 'chip chip-active' : 'chip'}
            onClick={() =>
              setStatusFilter((current) =>
                current.includes(status)
                  ? current.filter((s) => s !== status)
                  : [...current, status]
              )
            }
          >
            {STATUS_LABEL[status]}
          </button>
        ))}
      </div>

      {tasksAsync.loading && <p className="placeholder">加载中…</p>}
      {tasksAsync.error && <p className="error-text">{tasksAsync.error}</p>}

      {!tasksAsync.loading && !tasksAsync.error && tasks.length === 0 && (
        <EmptyState title="还没有任务" hint="用上面的输入框记第一条" />
      )}

      {!tasksAsync.loading &&
        !tasksAsync.error &&
        tasks.length > 0 &&
        visible.length === 0 && (
          <EmptyState
            title="没有符合条件的任务"
            hint="换个筛选条件试试"
            action={
              <button
                type="button"
                className="btn btn-sm"
                onClick={() => setStatusFilter([])}
              >
                清除筛选
              </button>
            }
          />
        )}

      {GROUP_ORDER.map((key) => {
        const bucket = groups.get(key);
        if (!bucket || bucket.length === 0) return null;
        return renderGroup(key, GROUP_LABEL[key], bucket);
      })}

      {completed.length > 0 && renderGroup('completed', '已完结', completed)}

      <Drawer
        open={drawerOpen}
        title={editing ? '编辑任务' : '新建任务'}
        onClose={() => setDrawerOpen(false)}
      >
        <TaskForm
          initial={editing}
          projects={projects}
          submitting={submitting}
          onCancel={() => setDrawerOpen(false)}
          onSubmit={(input) => handleCreate(input)}
        />
      </Drawer>

      <Modal
        open={pendingDelete !== null}
        title="删除任务"
        onClose={() => setPendingDelete(null)}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setPendingDelete(null)}>
              取消
            </button>
            <button type="button" className="btn btn-danger" onClick={() => void handleDelete()}>
              确认删除
            </button>
          </>
        }
      >
        <p>确定删除「{pendingDelete?.title}」吗？此操作无法撤销。</p>
      </Modal>
    </section>
  );
}
