import { useState } from 'react';
import { Field } from '@/components/Field';
import type { Priority, Project, ProjectStatus, ProgressMode } from '@/types/entities';

interface ProjectFormProps {
  initial?: Project | null;
  onSubmit: (input: {
    name: string;
    description: string;
    status: ProjectStatus;
    priority: Priority;
    color: string | null;
    progressMode: ProgressMode;
    progressManual: number | null;
    startDate: string | null;
    dueDate: string | null;
  }) => Promise<void>;
  onCancel: () => void;
  submitting: boolean;
}

const STATUS_OPTIONS: Array<{ value: ProjectStatus; label: string }> = [
  { value: 'planning', label: '规划中' },
  { value: 'active', label: '进行中' },
  { value: 'paused', label: '暂停' },
  { value: 'done', label: '已完成' }
];

const PRIORITY_OPTIONS: Array<{ value: Priority; label: string }> = [
  { value: 'high', label: '高' },
  { value: 'medium', label: '中' },
  { value: 'low', label: '低' },
  { value: 'none', label: '无' }
];

export function ProjectForm({
  initial,
  onSubmit,
  onCancel,
  submitting
}: ProjectFormProps): React.ReactElement {
  const [name, setName] = useState(initial?.name ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [status, setStatus] = useState<ProjectStatus>(initial?.status ?? 'planning');
  const [priority, setPriority] = useState<Priority>(initial?.priority ?? 'none');
  const [progressMode, setProgressMode] = useState<ProgressMode>(initial?.progressMode ?? 'auto');
  const [progressManual, setProgressManual] = useState(initial?.progressManual ?? 0);
  const [startDate, setStartDate] = useState(initial?.startDate ?? '');
  const [dueDate, setDueDate] = useState(initial?.dueDate ?? '');

  const handleSubmit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    if (!name.trim()) return;
    await onSubmit({
      name: name.trim(),
      description: description.trim(),
      status,
      priority,
      color: initial?.color ?? null,
      progressMode,
      progressManual: progressMode === 'manual' ? progressManual : null,
      startDate: startDate || null,
      dueDate: dueDate || null
    });
  };

  return (
    <form onSubmit={(event) => void handleSubmit(event)}>
      <Field label="项目名称">
        <input
          value={name}
          onChange={(event) => setName(event.target.value)}
          placeholder="例如：工作台上线"
          autoFocus
        />
      </Field>
      <Field label="描述">
        <textarea
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          rows={3}
          placeholder="这个项目要达成什么？"
        />
      </Field>
      <Field label="状态">
        <select value={status} onChange={(event) => setStatus(event.target.value as ProjectStatus)}>
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
      <Field label="进度模式" hint="自动模式按任务完成比例计算；手动模式由你直接填写">
        <select
          value={progressMode}
          onChange={(event) => setProgressMode(event.target.value as ProgressMode)}
        >
          <option value="auto">自动</option>
          <option value="manual">手动</option>
        </select>
      </Field>
      {progressMode === 'manual' && (
        <Field label="进度百分比">
          <input
            type="number"
            min={0}
            max={100}
            value={progressManual}
            onChange={(event) => setProgressManual(Number(event.target.value))}
          />
        </Field>
      )}
      <Field label="开始日期">
        <input
          type="date"
          value={startDate}
          onChange={(event) => setStartDate(event.target.value)}
        />
      </Field>
      <Field label="目标日期">
        <input type="date" value={dueDate} onChange={(event) => setDueDate(event.target.value)} />
      </Field>
      <div className="form-actions">
        <button type="button" className="btn" onClick={onCancel}>
          取消
        </button>
        <button type="submit" className="btn btn-primary" disabled={submitting || !name.trim()}>
          {submitting ? '保存中…' : '保存'}
        </button>
      </div>
    </form>
  );
}
