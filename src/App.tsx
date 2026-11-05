import { useEffect, type ReactElement } from 'react';
import { ProjectPage } from '@/features/projects/ProjectPage';
import { SettingsPage } from '@/features/settings/SettingsPage';
import { TaskPage } from '@/features/tasks/TaskPage';
import { call } from '@/lib/api';
import { isTheme, useUiStore, type Page } from '@/store/ui';

const NAV: Array<{ page: Page; label: string }> = [
  { page: 'projects', label: '项目' },
  { page: 'tasks', label: '任务' },
  { page: 'settings', label: '设置' }
];

export default function App(): ReactElement {
  const page = useUiStore((s) => s.page);
  const setPage = useUiStore((s) => s.setPage);
  const applyTheme = useUiStore((s) => s.applyTheme);
  const theme = useUiStore((s) => s.theme);
  const setTheme = useUiStore((s) => s.setTheme);
  const toast = useUiStore((s) => s.toast);
  const dismissToast = useUiStore((s) => s.dismissToast);

  // 启动时恢复保存的主题：设置页只在切到「设置」时挂载，不能把读取放在那里
  useEffect(() => {
    void call<unknown>(() => window.api.settings.get('theme'))
      .then((saved) => {
        // settings 表的值不受类型系统保护，必须运行时校验后再用
        if (isTheme(saved)) setTheme(saved);
      })
      .catch(() => {
        // 读不到设置就保持默认的跟随系统，不打扰用户
      });
  }, [setTheme]);

  useEffect(() => {
    applyTheme(theme);
  }, [applyTheme, theme]);

  // 「跟随系统」要跟着系统实时变化，而不只是启动时解析一次
  useEffect(() => {
    if (theme !== 'system') return;
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = (): void => applyTheme('system');
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, [applyTheme, theme]);

  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(dismissToast, 3000);
    return () => window.clearTimeout(timer);
  }, [toast, dismissToast]);

  return (
    <div className="app-shell">
      <nav className="app-nav">
        <div className="app-brand">工作台</div>
        {NAV.map((item) => (
          <button
            key={item.page}
            type="button"
            className={item.page === page ? 'nav-item nav-item-active' : 'nav-item'}
            onClick={() => setPage(item.page)}
          >
            {item.label}
          </button>
        ))}
      </nav>
      <main className="app-main">
        {page === 'projects' && <ProjectPage />}
        {page === 'tasks' && <TaskPage />}
        {page === 'settings' && <SettingsPage />}
      </main>
      {toast && (
        <div className={toast.tone === 'error' ? 'toast toast-error' : 'toast'}>
          <span>{toast.message}</span>
          <button type="button" onClick={dismissToast}>
            ×
          </button>
        </div>
      )}
    </div>
  );
}
