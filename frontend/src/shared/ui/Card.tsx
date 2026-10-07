import type { ReactNode } from 'react'

interface CardProps {
  title?: ReactNode
  /** Controls on the right of the title: filters, period switches. */
  actions?: ReactNode
  children: ReactNode
  className?: string
}

export function Card({ title, actions, children, className = '' }: CardProps) {
  return (
    <section className={`card ${className}`}>
      {(title || actions) && (
        <header className="card-head">
          {title && <h2>{title}</h2>}
          {actions && <div className="card-actions">{actions}</div>}
        </header>
      )}
      {children}
    </section>
  )
}
