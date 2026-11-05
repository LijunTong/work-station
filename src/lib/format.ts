export function formatPercent(value: number): string {
  const clamped = Math.max(0, Math.min(100, Math.round(value)));
  return `${clamped}%`;
}

export function formatHours(value: number | null): string {
  if (value === null) return '—';
  const rounded = Math.round(value * 10) / 10;
  return `${rounded}h`;
}

export function formatLocalDateTime(value: string | null): string {
  if (!value) return '—';
  if (value.length === 16) return value.slice(5).replace('T', ' ');
  if (value.length === 10) return value.slice(5);
  return value;
}

export function isOverdue(dueAt: string | null, nowLocalDateTime: string): boolean {
  if (!dueAt) return false;
  return dueAt < nowLocalDateTime;
}

const pad = (n: number): string => String(n).padStart(2, '0');

export function toLocalDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function toLocalDateTime(date: Date): string {
  return `${toLocalDate(date)}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
