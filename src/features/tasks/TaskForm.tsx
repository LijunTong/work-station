import { useState } from 'react';
import { Field } from '@/components/Field';
import type { Priority, Project, Task, TaskStatus } from '@/types/entities';

interface TaskFormProps {
  initial?: Task | null;
  projects: Project[];
  onSubmit: (input: {
    title: string;
    projectId: string | null;
    notes: string;
    status: TaskStatus;
    priority: Priority;
    dueAt: string | null;
    estimate: number | null;
    spent: number | null;
    blockedReason: string | null;
  }) => Promise<void>;
  onCancel: () => void;
  submitting: boolean;
}

const STATUS_OPTIONS: Array<{ value: TaskStatus; label: string }> = [
  { value: 'todo', label: '待办' },
  { value: 'doing', label: '进行中' },
  { value: 'blocked', label: '已阻塞' },
  { value: 'done', label: '已完成' },
  { value: 'canceled', label: '已取消' }
];

const PRIORITY_OPTIONS: Array<{ value: Priority; label: string }> = [
  { value: 'high', label: '高' },
  { value: 'medium', label: '中' },
  { value: 'low', label: '低' },
  { value: 'none', label: '无' }
];

export function TaskForm({
  initial,
  projects,
  onSubmit,
  onCancel,
  submitting
}: TaskFormProps): React.ReactElement {
  const [title, setTitle] = useState(initial?.title ?? '');
  const [projectId, setProjectId] = useState(initial?.projectId ?? '');
  const [notes, setNotes] = useState(initial?.notes ?? '');
  const [status, setStatus] = useState<TaskStatus>(initial?.status ?? 'todo');
  const [priority, setPriority] = useState<Priority>(initial?.priority ?? 'none');
  const [dueAt, setDueAt] = useState(initial?.dueAt ?? '');
  const [estimate, setEstimate] = useState(initial?.estimate ?? 0);
  const [spent, setSpent] = useState(initial?.spent ?? 0);
  const [blockedReason, setBlockedReason] = useState(initial?.blockedReason ?? '');

  const submit = async (): Promise<void> => {
    if (!title.trim()) return;
    await onSubmit({
      title: title.trim(),
      projectId: projectId || null,
      notes: notes.trim(),
      status,
      priority,
      dueAt: dueAt || null,
      estimate: estimate > 0 ? estimate : null,
      spent: spent > 0 ? spent : null,
      blockedReason: status === 'blocked' ? blockedReason.trim() : null
    });
  };

  const handleSubmit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    await submit();
  };

  return (
    <form
      onSubmit={(event) => void handleSubmit(event)}
      onKeyDown={(event) => {
        // 设计文档要求「Ctrl+Enter 确认」，这里用 requestSubmit 走正常提交路径
        if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
          event.preventDefault();
          event.currentTarget.requestSubmit();
        }
      }}
    >
      <Field label="任务标题">
        <input
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          placeholder="要做什么？"
          autoFocus
        />
      </Field>
      <Field label="所属项目">
        <select value={projectId} onChange={(event) => setProjectId(event.target.value)}>
          <option value="">独立任务</option>
          {projects.map((project) => (
            <option key={project.id} value={project.id}>
              {project.name}
            </option>
          ))}
        </select>
      </Field>
      <Field label="状态">
        <select value={status} onChange={(event) => setStatus(event.target.value as TaskStatus)}>
          {STATUS_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </Field>
      <Field label="优先级">
        <select value={priority} onChange={(event) => setPriority(event.target.value as Priority)}>
          {PRIORITY_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </Field>
      <Field label="截止时间">
        <input
          type="datetime-local"
          value={dueAt}
          onChange={(event) => setDueAt(event.target.value)}
        />
      </Field>
      <Field label="预估工时（小时）">
        <input
          type="number"
          min={0}
          step={0.5}
          value={estimate}
          onChange={(event) => setEstimate(Number(event.target.value))}
        />
      </Field>
      <Field label="实际工时（小时）">
        <input
          type="number"
          min={0}
          step={0.5}
          value={spent}
          onChange={(event) => setSpent(Number(event.target.value))}
        />
      </Field>
      {status === 'blocked' && (
        <Field label="阻塞原因" hint="写清楚卡在哪里，周报会自动收录">
          <input
            value={blockedReason}
            onChange={(event) => setBlockedReason(event.target.value)}
            placeholder="等后端接口"
          />
        </Field>
      )}
      <Field label="备注">
        <textarea value={notes} onChange={(event) => setNotes(event.target.value)} rows={3} />
      </Field>
      <div className="form-actions">
        <button type="button" className="btn" onClick={onCancel}>
          取消
        </button>
        <button type="submit" className="btn btn-primary" disabled={submitting || !title.trim()}>
          {submitting ? '保存中…' : '保存'}
        </button>
      </div>
    </form>
  );
}
