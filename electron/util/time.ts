const pad = (n: number): string => String(n).padStart(2, '0');

export function nowIso(): string {
  return new Date().toISOString();
}

export function toLocalDate(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function toLocalDateTime(d: Date): string {
  return `${toLocalDate(d)}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function isLocalDateTime(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value);
}
