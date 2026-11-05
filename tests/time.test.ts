import { describe, expect, it } from 'vitest';
import { isLocalDateTime, toLocalDate, toLocalDateTime } from '../electron/util/time';

describe('time 工具', () => {
  it('toLocalDate 输出 YYYY-MM-DD', () => {
    expect(toLocalDate(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('toLocalDateTime 输出 YYYY-MM-DDTHH:mm 且月份日期补零', () => {
    expect(toLocalDateTime(new Date(2026, 0, 5, 9, 7))).toBe('2026-01-05T09:07');
  });

  it('toLocalDateTime 输出下午时间不做 12 小时制转换', () => {
    expect(toLocalDateTime(new Date(2026, 10, 5, 14, 30))).toBe('2026-11-05T14:30');
  });

  it('toLocalDateTime 输出 23:59 边界', () => {
    expect(toLocalDateTime(new Date(2026, 10, 5, 23, 59))).toBe('2026-11-05T23:59');
  });

  it('isLocalDateTime 接受合法值', () => {
    expect(isLocalDateTime('2026-11-05T14:30')).toBe(true);
  });

  it('isLocalDateTime 拒绝带秒的值', () => {
    expect(isLocalDateTime('2026-11-05T14:30:00')).toBe(false);
  });

  it('isLocalDateTime 拒绝纯日期', () => {
    expect(isLocalDateTime('2026-11-05')).toBe(false);
  });
});
