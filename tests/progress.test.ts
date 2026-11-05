import { describe, expect, it } from 'vitest';
import { computeProgress } from '../src/lib/progress';

describe('进度计算', () => {
  it('空集合返回 0', () => {
    expect(computeProgress([])).toBe(0);
  });

  it('全部取消时返回 0 而不是 NaN', () => {
    expect(computeProgress([{ status: 'canceled' }, { status: 'canceled' }])).toBe(0);
  });

  it('全部完成返回 100', () => {
    expect(computeProgress([{ status: 'done' }, { status: 'done' }])).toBe(100);
  });

  it('一半完成返回 50', () => {
    expect(computeProgress([{ status: 'done' }, { status: 'todo' }])).toBe(50);
  });

  it('已取消的任务不计入分母', () => {
    expect(computeProgress([{ status: 'done' }, { status: 'todo' }, { status: 'canceled' }])).toBe(50);
  });

  it('进行中与阻塞都算未完成', () => {
    expect(computeProgress([{ status: 'done' }, { status: 'doing' }, { status: 'blocked' }])).toBe(33);
  });
});
