import { useEffect, useRef } from 'react'

interface SegmentedProps<T extends string | number> {
  value: T
  options: { value: T; label: string }[]
  onChange: (value: T) => void
  /** Accessible name of the group, e.g. "Period". */
  label: string
}

/**
 * A row of mutually exclusive buttons (a radio group), like 5 min / 15 min. When the row is wider than the
 * screen it scrolls sideways, and the chosen button is always scrolled into sight (only the row moves).
 */
export function Segmented<T extends string | number>({ value, options, onChange, label }: SegmentedProps<T>) {
  const row = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const r = row.current
    const on = r?.querySelector<HTMLElement>('button.on')
    if (!r || !on) return
    // the row's 2px padding and the chosen button's 1px outline come into sight too
    const edge = 3
    const left = on.offsetLeft - edge
    const right = on.offsetLeft + on.offsetWidth + edge
    if (left < r.scrollLeft) r.scrollLeft = left
    else if (right > r.scrollLeft + r.clientWidth) r.scrollLeft = right - r.clientWidth
  }, [value])

  return (
    <div ref={row} className="segmented" role="radiogroup" aria-label={label}>
      {options.map(o => (
        <button
          key={o.value}
          role="radio"
          aria-checked={o.value === value}
          className={o.value === value ? 'on' : ''}
          onClick={() => onChange(o.value)}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}
