import { DatabaseSync } from 'node:sqlite';
import { beforeEach, describe, expect, it } from 'vitest';
import { runMigrations } from '../electron/db';
import { createSettingsRepo } from '../electron/repositories/settings';

let db: DatabaseSync;
let settings: ReturnType<typeof createSettingsRepo>;

beforeEach(() => {
  db = new DatabaseSync(':memory:');
  db.exec('PRAGMA foreign_keys = ON');
  runMigrations(db);
  settings = createSettingsRepo(db);
});

describe('设置仓储', () => {
  it('未设置的键返回 null', () => {
    expect(settings.get('missing')).toBeNull();
  });

  it('可以写入并读回对象', () => {
    settings.set('daily_review', { enabled: true, time: '09:00' });
    expect(settings.get('daily_review')).toEqual({ enabled: true, time: '09:00' });
  });

  it('可以写入并读回布尔值', () => {
    settings.set('close_to_tray', false);
    expect(settings.get<boolean>('close_to_tray')).toBe(false);
  });

  it('重复写入覆盖旧值', () => {
    settings.set('theme', 'light');
    settings.set('theme', 'dark');
    expect(settings.get('theme')).toBe('dark');
  });

  it('getAll 返回全部设置', () => {
    settings.set('a', 1);
    settings.set('b', 'two');
    expect(settings.getAll()).toEqual({ a: 1, b: 'two' });
  });

  it('损坏的 JSON 值被当作不存在', () => {
    db.prepare('INSERT INTO settings (key, value) VALUES (?, ?)').run('broken', '{oops');
    expect(settings.get('broken')).toBeNull();
  });

  it('getString 在类型不符时返回兜底值', () => {
    settings.set('theme', 123);
    expect(settings.getString('theme', 'system')).toBe('system');
  });

  it('getBoolean 在类型不符时返回兜底值', () => {
    settings.set('close_to_tray', 'yes');
    expect(settings.getBoolean('close_to_tray', true)).toBe(true);
  });

  it('remove 能删除设置', () => {
    settings.set('x', 1);
    expect(settings.remove('x')).toBe(true);
    expect(settings.get('x')).toBeNull();
  });

  it('remove 对不存在的键返回 false', () => {
    expect(settings.remove('missing')).toBe(false);
  });

  it('getAll 跳过损坏的 JSON 值', () => {
    settings.set('ok', 'value');
    db.prepare('INSERT INTO settings (key, value) VALUES (?, ?)').run('broken', '{oops');
    // 损坏的值直接跳过，不影响其他设置
    expect(settings.getAll()).toEqual({ ok: 'value' });
  });

  it('getString 在类型相符时返回存储值', () => {
    settings.set('theme', 'dark');
    expect(settings.getString('theme', 'system')).toBe('dark');
  });

  it('getBoolean 在类型相符时返回存储值', () => {
    settings.set('close_to_tray', false);
    expect(settings.getBoolean('close_to_tray', true)).toBe(false);
  });

  it('写入 undefined 时按 null 存取', () => {
    // undefined 不能直接绑定到 node:sqlite，归一成 JSON null
    settings.set('unset', undefined);
    expect(settings.get('unset')).toBeNull();
    expect(settings.getAll()).toEqual({ unset: null });
  });

  it('仓储方法被解构后仍能调用（不依赖 this 绑定）', () => {
    const { get, set, remove, getAll, getString, getBoolean } = settings;
    set('theme', 'dark');
    expect(get('theme')).toBe('dark');
    expect(getString('theme', 'system')).toBe('dark');
    expect(getBoolean('theme', true)).toBe(true);
    expect(getAll()).toEqual({ theme: 'dark' });
    expect(remove('theme')).toBe(true);
    expect(get('theme')).toBeNull();
  });
});
