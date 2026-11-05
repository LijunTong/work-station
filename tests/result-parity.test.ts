import { describe, expect, it } from 'vitest';
import { fail, ok, type Result as MainResult } from '../electron/lib/result';
import type { Result as RendererResult } from '../src/types/api';

type Equal<A, B> = (<T>() => T extends A ? 1 : 2) extends <T>() => T extends B ? 1 : 2
  ? true
  : false;
type Assert<T extends true> = T;

/**
 * 编译期护栏：主进程的 Result 与渲染端的 Result 必须完全相同。
 * 两份声明故意不合并（主进程不依赖渲染端类型），但任何一侧漂移都必须
 * 让 `npm run typecheck` 失败，而不是在跨 IPC 边界时静默错位。
 */
export type MainResultMatchesRenderer = Assert<
  Equal<MainResult<unknown>, RendererResult<unknown>>
>;

describe('Result 形状一致性', () => {
  it('主进程 ok() 的产出可被渲染端 Result 接收', () => {
    const result: RendererResult<number> = ok(1);
    expect(result).toEqual({ ok: true, data: 1 });
  });

  it('主进程 fail() 的产出可被渲染端 Result 接收', () => {
    const result: RendererResult<never> = fail('HANDLER_FAILED', 'boom');
    expect(result).toEqual({ ok: false, error: { code: 'HANDLER_FAILED', message: 'boom' } });
  });
});
