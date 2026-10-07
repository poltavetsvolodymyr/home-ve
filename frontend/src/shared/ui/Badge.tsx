import type { ReactNode } from 'react'

export type Status = 'good' | 'warning' | 'critical' | 'neutral'

const icons: Record<Status, string> = { good: '●', warning: '▲', critical: '✕', neutral: '○' }

/** Status is never carried by colour alone: icon + label always ride along. */
export function Badge({ status, children }: { status: Status; children: ReactNode }) {
  return (
    <span className={`badge ${status}`}>
      <span className="badge-icon" aria-hidden>
        {icons[status]}
      </span>
      {children}
    </span>
  )
}
