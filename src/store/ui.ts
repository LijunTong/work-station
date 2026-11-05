import { create } from 'zustand';

export const THEMES = ['light', 'dark', 'system'] as const;
export type Theme = (typeof THEMES)[number];
export type Page = 'projects' | 'tasks' | 'settings';

/** 运行时校验：settings 表里的值不受类型系统保护（IPC 层不做校验） */
export function isTheme(value: unknown): value is Theme {
  return typeof value === 'string' && (THEMES as readonly string[]).includes(value);
}

/** 纯函数，便于测试：把三态主题解析成实际明暗 */
export function resolveTheme(theme: Theme, systemPrefersDark: boolean): 'light' | 'dark' {
  if (theme !== 'system') return theme;
  return systemPrefersDark ? 'dark' : 'light';
}

interface Toast {
  id: number;
  message: string;
  tone: 'info' | 'error';
}

interface UiState {
  theme: Theme;
  page: Page;
  toast: Toast | null;
  setTheme: (theme: Theme) => void;
  setPage: (page: Page) => void;
  showToast: (message: string, tone?: 'info' | 'error') => void;
  dismissToast: () => void;
  applyTheme: (theme: Theme) => void;
}

const systemPrefersDark = (): boolean =>
  window.matchMedia('(prefers-color-scheme: dark)').matches;

export const useUiStore = create<UiState>((set, get) => ({
  theme: 'system',
  page: 'projects',
  toast: null,
  setTheme: (theme) => {
    set({ theme });
    get().applyTheme(theme);
  },
  setPage: (page) => set({ page }),
  showToast: (message, tone = 'info') => set({ toast: { id: Date.now(), message, tone } }),
  dismissToast: () => set({ toast: null }),
  applyTheme: (theme) => {
    document.documentElement.dataset.theme = resolveTheme(theme, systemPrefersDark());
  }
}));
