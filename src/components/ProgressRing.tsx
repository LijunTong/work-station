interface ProgressRingProps {
  value: number;
  size?: number;
  stroke?: number;
  label?: string;
}

export function ProgressRing({
  value,
  size = 44,
  stroke = 5,
  label
}: ProgressRingProps): React.ReactElement {
  const clamped = Math.max(0, Math.min(100, value));
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference * (1 - clamped / 100);

  return (
    <div className="progress-ring" style={{ width: size, height: size }}>
      <svg width={size} height={size} role="img" aria-label={label ?? `进度 ${clamped}%`}>
        <circle
          className="progress-ring-track"
          cx={size / 2}
          cy={size / 2}
          r={radius}
          strokeWidth={stroke}
          fill="none"
        />
        <circle
          className="progress-ring-value"
          cx={size / 2}
          cy={size / 2}
          r={radius}
          strokeWidth={stroke}
          fill="none"
          strokeDasharray={circumference}
          strokeDashoffset={offset}
          strokeLinecap="round"
          transform={`rotate(-90 ${size / 2} ${size / 2})`}
        />
      </svg>
      <span className="progress-ring-text">{Math.round(clamped)}%</span>
    </div>
  );
}
