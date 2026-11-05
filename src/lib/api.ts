import type { Result } from '@/types/api';

export class ApiError extends Error {
  constructor(
    readonly code: string,
    message: string
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

export async function call<T>(fn: () => Promise<Result<T>>): Promise<T> {
  const result = await fn();
  if (!result.ok) {
    throw new ApiError(result.error.code, result.error.message);
  }
  return result.data;
}
