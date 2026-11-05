import { describe, expect, it } from 'vitest';
import { formatHours, formatLocalDateTime, formatPercent, isOverdue, toLocalDate, toLocalDateTime } from '../src/lib/format';
import { toLocalDate as mainToLocalDate, toLocalDateTime as mainToLocalDateTime } from '../electron/util/time';

describe('格式化工具', () => {
  it('formatPercent 四舍五入并夹在 0-100', () => {
    expect(formatPercent(33.4)).toBe('33%');
    expect(formatPercent(33.5)).toBe('34%');
    expect(formatPercent(-10)).toBe('0%');
    expect(formatPercent(150)).toBe('100%');
  });

  it('formatHours 对 null 返回破折号', () => {
    expect(formatHours(null)).toBe('—');
  });

  it('formatHours 保留一位小数', () => {
    expect(formatHours(2)).toBe('2h');
    expect(formatHours(2.55)).toBe('2.6h');
  });

  it('formatLocalDateTime 截取月日与时分', () => {
    expect(formatLocalDateTime('2026-11-05T14:30')).toBe('11-05 14:30');
    expect(formatLocalDateTime('2026-11-05')).toBe('11-05');
    expect(formatLocalDateTime(null)).toBe('—');
  });

  it('isOverdue 按本地墙钟字符串比较', () => {
    expect(isOverdue('2026-11-05T09:00', '2026-11-05T10:00')).toBe(true);
    expect(isOverdue('2026-11-05T11:00', '2026-11-05T10:00')).toBe(false);
    expect(isOverdue(null, '2026-11-05T10:00')).toBe(false);
  });

  it('toLocalDateTime 与主进程同口径', () => {
    // 钉死「渲染端补齐」这件事：同一批时刻，两边输出必须逐字相同
    const samples = [
      new Date(2026, 10, 5, 14, 30),
      new Date(2026, 0, 1, 0, 0),
      new Date(2026, 11, 31, 23, 59),
      new Date(2026, 2, 9, 7, 5)
    ];
    for (const d of samples) {
      expect(toLocalDateTime(d)).toBe(mainToLocalDateTime(d));
      expect(toLocalDateTime(d)).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/);
      expect(toLocalDate(d)).toBe(mainToLocalDate(d));
    }
    expect(toLocalDateTime(new Date(2026, 10, 5, 9, 7))).toBe('2026-11-05T09:07');
  });
});
