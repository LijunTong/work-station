import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { isTheme, resolveTheme, THEMES, useUiStore } from '../src/store/ui';

describe('主题辅助函数', () => {
  it('isTheme 接受三态主题，拒绝其它值', () => {
    for (const theme of THEMES) expect(isTheme(theme)).toBe(true);
    expect(isTheme('sepia')).toBe(false);
    expect(isTheme(null)).toBe(false);
    expect(isTheme(undefined)).toBe(false);
    expect(isTheme(1)).toBe(false);
    expect(isTheme({ theme: 'dark' })).toBe(false);
  });

  it('resolveTheme 原样返回显式明暗，system 按系统偏好解析', () => {
    expect(resolveTheme('light', true)).toBe('light');
    expect(resolveTheme('dark', false)).toBe('dark');
    expect(resolveTheme('system', true)).toBe('dark');
    expect(resolveTheme('system', false)).toBe('light');
  });
});

describe('UI store', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T00:00:00Z'));
    vi.stubGlobal('document', { documentElement: { dataset: {} } });
    vi.stubGlobal('window', { matchMedia: vi.fn(() => ({ matches: true })) });
    useUiStore.setState({ theme: 'system', page: 'projects', toast: null });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('setPage 切换当前页', () => {
    useUiStore.getState().setPage('settings');
    expect(useUiStore.getState().page).toBe('settings');
  });

  it('showToast / dismissToast 管理提示', () => {
    useUiStore.getState().showToast('保存失败', 'error');

    const toast = useUiStore.getState().toast;
    expect(toast).toEqual({ id: Date.now(), message: '保存失败', tone: 'error' });

    useUiStore.getState().dismissToast();
    expect(useUiStore.getState().toast).toBeNull();
  });

  it('setTheme 立即把解析后的主题写到 documentElement', () => {
    useUiStore.getState().setTheme('dark');
    expect(useUiStore.getState().theme).toBe('dark');
    expect(document.documentElement.dataset.theme).toBe('dark');

    useUiStore.getState().setTheme('light');
    expect(document.documentElement.dataset.theme).toBe('light');
  });

  it('setTheme(system) 跟随 matchMedia 的当前偏好', () => {
    useUiStore.getState().setTheme('system');
    expect(document.documentElement.dataset.theme).toBe('dark');
  });
});
