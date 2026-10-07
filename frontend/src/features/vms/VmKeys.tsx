import { useEffect, useRef, useState } from 'react'
import { functionKeys, modifierKeys, panelKeys, type Modifier, type PanelKey } from './keysyms'

/** Keys that repeat while held, like on a real keyboard: moving through a list, deleting. */
const repeating = new Set<PanelKey>([
  panelKeys.up,
  panelKeys.down,
  panelKeys.left,
  panelKeys.right,
  panelKeys.backspace,
  panelKeys.pgUp,
  panelKeys.pgDn,
])

interface VmKeysProps {
  disabled: boolean
  /** Modifiers switched on: they apply to the next key or line, then switch off. */
  mods: Modifier[]
  onToggle: (m: Modifier) => void
  onKey: (k: PanelKey) => void
}

/**
 * The keys a phone's keyboard lacks: Esc, Tab, arrows, Home/End, PgUp/PgDn, F1–F12, and Ctrl/Alt/Shift that
 * stay on for the next key. Arrows and Backspace repeat while held.
 */
export function VmKeys({ disabled, mods, onToggle, onKey }: VmKeysProps) {
  const [fn, setFn] = useState(false)
  const timer = useRef<number | undefined>(undefined)

  const stop = () => {
    window.clearTimeout(timer.current)
    window.clearInterval(timer.current)
    timer.current = undefined
  }
  useEffect(() => stop, [])

  // a press sends at once; held, it repeats after 400 ms every 80 ms
  const press = (k: PanelKey) => {
    onKey(k)
    if (!repeating.has(k)) return
    stop()
    timer.current = window.setTimeout(() => {
      timer.current = window.setInterval(() => onKey(k), 80)
    }, 400)
  }

  const keyButton = (k: PanelKey, className = '') => (
    <button
      key={k.code}
      type="button"
      className={className}
      disabled={disabled}
      aria-label={k.code}
      onPointerDown={e => {
        e.preventDefault() // keeps focus (and the phone's keyboard) where it was
        press(k)
      }}
      onPointerUp={stop}
      onPointerLeave={stop}
      onPointerCancel={stop}
      onKeyDown={e => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          onKey(k)
        }
      }}
    >
      {k.label}
    </button>
  )

  const modButton = (m: Modifier) => (
    <button
      key={m}
      type="button"
      className={mods.includes(m) ? 'on' : ''}
      aria-pressed={mods.includes(m)}
      disabled={disabled}
      onClick={() => onToggle(m)}
    >
      {modifierKeys[m].label}
    </button>
  )

  const k = panelKeys
  return (
    <div className="vm-keys">
      {keyButton(k.esc)}
      {keyButton(k.tab)}
      {modButton('ctrl')}
      {modButton('alt')}
      {modButton('shift')}
      <button type="button" className={fn ? 'on' : ''} aria-pressed={fn} onClick={() => setFn(f => !f)}>
        F1…
      </button>

      {fn && functionKeys.map(f => keyButton(f))}

      {keyButton(k.home)}
      {keyButton(k.end)}
      {keyButton(k.pgUp)}
      {keyButton(k.pgDn)}
      {keyButton(k.del)}
      {keyButton(k.backspace)}

      {keyButton(k.space, 'span3')}
      <span />
      {keyButton(k.up)}
      <span />
      {keyButton(k.enter, 'span3')}
      {keyButton(k.left)}
      {keyButton(k.down)}
      {keyButton(k.right)}
    </div>
  )
}
