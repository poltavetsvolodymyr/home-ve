import type { ReactNode } from 'react'

interface StatProps {
  label: string
  value: ReactNode
  sub?: ReactNode
  /** 0–100: draws a bar under the value, amber from 75, red from 90. */
  meter?: number
}

/** A small tile with one big number, like CPU or Memory on the Overview page. */
export function Stat({ label, value, sub, meter }: StatProps) {
  const level = meter === undefined ? '' : meter >= 90 ? 'critical' : meter >= 75 ? 'warning' : ''
  return (
    <div className="stat">
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
      {meter !== undefined && (
        <div
          className={`meter ${level}`}
          role="meter"
          aria-valuenow={Math.round(meter)}
          aria-valuemin={0}
          aria-valuemax={100}
        >
          <div style={{ width: `${Math.min(100, Math.max(0, meter))}%` }} />
        </div>
      )}
      {sub && <div className="stat-sub">{sub}</div>}
    </div>
  )
}
