import type { ReactElement } from 'react';
import { call } from '@/lib/api';
import { THEMES, useUiStore, type Theme } from '@/store/ui';

const THEME_LABELS: Record<Theme, string> = {
  light: '浅色',
  dark: '深色',
  system: '跟随系统'
};

const THEME_OPTIONS = THEMES.map((value) => ({ value, label: THEME_LABELS[value] }));

export function SettingsPage(): ReactElement {
  const theme = useUiStore((s) => s.theme);
  const setTheme = useUiStore((s) => s.setTheme);
  const showToast = useUiStore((s) => s.showToast);

  const handleThemeChange = async (next: Theme): Promise<void> => {
    setTheme(next);
    try {
      await call<void>(() => window.api.settings.set('theme', next));
    } catch (error) {
      showToast(error instanceof Error ? error.message : '保存主题失败', 'error');
    }
  };

  return (
    <section className="settings-page">
      <h2>设置</h2>
      <div className="settings-row">
        <label htmlFor="theme-select">主题</label>
        <select
          id="theme-select"
          value={theme}
          onChange={(event) => void handleThemeChange(event.target.value as Theme)}
        >
          {THEME_OPTIONS.map((item) => (
            <option key={item.value} value={item.value}>
              {item.label}
            </option>
          ))}
        </select>
      </div>
    </section>
  );
}
