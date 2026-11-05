import { useCallback, useState } from 'react';
import { Badge } from '@/components/Badge';
import { Drawer } from '@/components/Drawer';
import { EmptyState } from '@/components/EmptyState';
import { Modal } from '@/components/Modal';
import { ProgressRing } from '@/components/ProgressRing';
import { call } from '@/lib/api';
import { isOverdue, toLocalDateTime } from '@/lib/format';
import { computeProgress } from '@/lib/progress';
import { useAsync } from '@/hooks/useAsync';
import { useUiStore } from '@/store/ui';
import type { CreateProjectInput } from '@/types/api';
import type { Project, Task } from '@/types/entities';
import { ProjectForm } from './ProjectForm';

const STATUS_LABEL: Record<string, string> = {
  planning: '规划中',
  active: '进行中',
  paused: '暂停',
  done: '已完成'
};

const PRIORITY_TONE: Record<string, 'danger' | 'warning' | 'accent' | 'default'> = {
  high: 'danger',
  medium: 'warning',
  low: 'accent',
  none: 'default'
};

const PRIORITY_LABEL: Record<string, string> = {
  high: '高优先级',
  medium: '中优先级',
  low: '低优先级',
  none: '无优先级'
};

export function ProjectPage(): React.ReactElement {
  const showToast = useUiStore((s) => s.showToast);

  const projectsAsync = useAsync<Project[]>(() => call(() => window.api.projects.list()), []);
  const tasksAsync = useAsync<Task[]>(() => call(() => window.api.tasks.list()), []);

  const [editing, setEditing] = useState<Project | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<Project | null>(null);

  const projects = projectsAsync.data ?? [];
  const tasks = tasksAsync.data ?? [];

  // 墙钟口径统一走 Task 10 的 toLocalDateTime（与主进程 electron/util/time.ts 同实现），
  // 不要在页面里手拼日期串 —— 手拼副本会与主进程口径漂移。
  const nowLocalDateTime = toLocalDateTime(new Date());

  const reloadAll = useCallback(() => {
    projectsAsync.reload();
    tasksAsync.reload();
  }, [projectsAsync, tasksAsync]);

  const handleSubmit = async (input: CreateProjectInput): Promise<void> => {
    setSubmitting(true);
    try {
      if (editing) {
        await call(() => window.api.projects.update(editing.id, input));
        showToast('项目已更新');
      } else {
        await call(() => window.api.projects.create(input));
        showToast('项目已创建');
      }
      setDrawerOpen(false);
      setEditing(null);
      reloadAll();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '保存失败', 'error');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (): Promise<void> => {
    if (!pendingDelete) return;
    try {
      await call(() => window.api.projects.remove(pendingDelete.id));
      showToast('项目已删除');
      setPendingDelete(null);
      reloadAll();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '删除失败', 'error');
    }
  };

  const handleArchive = async (project: Project): Promise<void> => {
    try {
      await call(() => window.api.projects.archive(project.id));
      showToast('项目已归档');
      reloadAll();
    } catch (error) {
      showToast(error instanceof Error ? error.message : '归档失败', 'error');
    }
  };

  return (
    <section className="project-page">
      <header className="page-header">
        <h2>项目</h2>
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => {
            setEditing(null);
            setDrawerOpen(true);
          }}
        >
          新建项目
        </button>
      </header>

      {projectsAsync.loading && <p className="placeholder">加载中…</p>}
      {projectsAsync.error && <p className="error-text">{projectsAsync.error}</p>}

      {!projectsAsync.loading && projects.length === 0 && (
        <EmptyState
          title="还没有项目"
          hint="建一个项目，把散落的事归到一起"
          action={
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => {
                setEditing(null);
                setDrawerOpen(true);
              }}
            >
              新建项目
            </button>
          }
        />
      )}

      <div className="project-grid">
        {projects.map((project) => {
          const projectTasks = tasks.filter((task) => task.projectId === project.id);
          const progress =
            project.progressMode === 'manual'
              ? (project.progressManual ?? 0)
              : computeProgress(projectTasks);
          const overdue = projectTasks.filter(
            (task) =>
              task.status !== 'done' &&
              task.status !== 'canceled' &&
              isOverdue(task.dueAt, nowLocalDateTime)
          ).length;

          return (
            <article key={project.id} className="project-card">
              <div className="project-card-top">
                <ProgressRing value={progress} label={`${project.name} 进度`} />
                <div className="project-card-title">
                  <h3>{project.name}</h3>
                  <div className="project-card-badges">
                    <Badge>{STATUS_LABEL[project.status] ?? project.status}</Badge>
                    {project.priority !== 'none' && (
                      <Badge tone={PRIORITY_TONE[project.priority]}>
                        {PRIORITY_LABEL[project.priority]}
                      </Badge>
                    )}
                    {overdue > 0 && <Badge tone="danger">逾期 {overdue}</Badge>}
                  </div>
                </div>
              </div>
              {project.description && <p className="project-card-desc">{project.description}</p>}
              <div className="project-card-meta">
                <span>{projectTasks.length} 个任务</span>
                {project.dueDate && <span>目标 {project.dueDate}</span>}
              </div>
              <div className="project-card-actions">
                <button
                  type="button"
                  className="btn btn-sm"
                  onClick={() => {
                    setEditing(project);
                    setDrawerOpen(true);
                  }}
                >
                  编辑
                </button>
                <button type="button" className="btn btn-sm" onClick={() => void handleArchive(project)}>
                  归档
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-danger"
                  onClick={() => setPendingDelete(project)}
                >
                  删除
                </button>
              </div>
            </article>
          );
        })}
      </div>

      <Drawer open={drawerOpen} title={editing ? '编辑项目' : '新建项目'} onClose={() => setDrawerOpen(false)}>
        <ProjectForm
          initial={editing}
          submitting={submitting}
          onCancel={() => setDrawerOpen(false)}
          onSubmit={(input) => handleSubmit(input)}
        />
      </Drawer>

      <Modal
        open={pendingDelete !== null}
        title="删除项目"
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
        <p>
          确定删除「{pendingDelete?.name}」吗？该项目下的所有任务会一并删除，且无法撤销。
        </p>
      </Modal>
    </section>
  );
}
