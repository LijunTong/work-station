export type Result<T> =
  | { ok: true; data: T }
  | { ok: false; error: { code: string; message: string } };

export function ok<T>(data: T): Result<T> {
  return { ok: true, data };
}

export function fail(code: string, message: string): Result<never> {
  return { ok: false, error: { code, message } };
}

export function toBool(value: boolean): number {
  return value ? 1 : 0;
}

export function nullable<T>(value: T | null | undefined): T | null {
  return value === undefined ? null : value;
}
