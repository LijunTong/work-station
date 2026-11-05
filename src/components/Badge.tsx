import type { ReactNode } from 'react';

interface BadgeProps {
  tone?: 'default' | 'accent' | 'danger' | 'warning' | 'success';
  children: ReactNode;
}

export function Badge({ tone = 'default', children }: BadgeProps): React.ReactElement {
  return <span className={`badge badge-${tone}`}>{children}</span>;
}
