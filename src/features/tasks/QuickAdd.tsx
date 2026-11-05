import { useState } from 'react';
import { call } from '@/lib/api';
import { useUiStore } from '@/store/ui';

interface QuickAddProps {
  onCreated: () => void;
}

export function QuickAdd({ onCreated }: QuickAddProps): React.ReactElement {
  const showToast = useUiStore((s) => s.showToast);
  const [title, setTitle] = useState('');
  const [busy, setBusy] = useState(false);

  const handleSubmit = async (event: React.FormEvent): Promise<void> => {
    event.preventDefault();
    const trimmed = title.trim();
    if (!trimmed || busy) return;
    setBusy(true);
    try {
      await call(() => window.api.tasks.create({ title: trimmed }));
      setTitle('');
      onCreated();
    } catch (error) {
      // 失败时保留输入内容，让用户可以重试；但必须给出可见反馈，否则用户分不清「没成功」和「没响应」
      showToast(error instanceof Error ? error.message : '添加失败', 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="quick-add" onSubmit={(event) => void handleSubmit(event)}>
      <input
        value={title}
        onChange={(event) => setTitle(event.target.value)}
        placeholder="记一件事…"
        aria-label="快速添加任务"
      />
      <button type="submit" className="btn btn-primary" disabled={busy || !title.trim()}>
        添加
      </button>
    </form>
  );
}
