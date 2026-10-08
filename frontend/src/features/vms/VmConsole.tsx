import RFB from '@novnc/novnc'
import { CornerDownLeft, Keyboard, Maximize, RotateCcw } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Card } from '@/shared/ui'
import { consoleUrl } from './api'
import { panelKeyEvents, panelKeys, textEvents, type KeyEvent, type Modifier, type PanelKey } from './keysyms'
import { VmKeys } from './VmKeys'

type Status = 'connecting' | 'connected' | 'closed'

/** After a lost connection (a reboot: QEMU goes and a new one comes), try again every 2 s for a minute. */
const retryMs = 2000
const maxRetries = 30

/** Whether Send presses Enter after the text: remembered in this browser, on unless switched off. */
const enterKey = 'home-ve.console.enter'
function readEnter(): boolean {
  try {
    return localStorage.getItem(enterKey) !== 'off'
  } catch {
    return true
  }
}
function saveEnter(on: boolean) {
  try {
    localStorage.setItem(enterKey, on ? 'on' : 'off')
  } catch {
    /* private mode: just not remembered */
  }
}

/**
 * The VM's screen (noVNC over /api/vms/{name}/console). Scaled to fit; click it to type with a real keyboard.
 * A phone has none that noVNC can capture, so there's a panel of keys (arrows, Esc, F-keys, Ctrl…) and a
 * text box that types into the VM key by key.
 */
export default function VmConsole({ name }: { name: string }) {
  const screen = useRef<HTMLDivElement>(null)
  const rfb = useRef<RFB | null>(null)
  const [status, setStatus] = useState<Status>('connecting')
  const [attempt, setAttempt] = useState(0)
  // connection attempts in a row that failed or dropped; a successful connect resets it
  const failures = useRef(0)
  const [text, setText] = useState('')
  const [mods, setMods] = useState<Modifier[]>([])
  const [withEnter, setWithEnter] = useState(readEnter)

  useEffect(() => {
    if (!screen.current) return
    setStatus('connecting')
    const r = new RFB(screen.current, consoleUrl(name), { wsProtocols: ['binary'] })
    r.scaleViewport = true
    r.resizeSession = false
    let retry: number | undefined
    let leaving = false // our own disconnect below (another tab, another VM) is no reason to retry
    r.addEventListener('connect', () => {
      failures.current = 0
      setStatus('connected')
    })
    r.addEventListener('disconnect', () => {
      if (leaving) return
      setStatus('closed')
      if (++failures.current <= maxRetries) retry = window.setTimeout(() => setAttempt(a => a + 1), retryMs)
    })
    rfb.current = r
    return () => {
      leaving = true
      window.clearTimeout(retry)
      r.disconnect()
      rfb.current = null
    }
  }, [name, attempt])

  const send = (events: KeyEvent[]) => {
    const r = rfb.current
    if (!r) return
    for (const [keysym, code, down] of events) r.sendKey(keysym, code, down)
    setMods([])
  }

  const toggle = (m: Modifier) => setMods(ms => (ms.includes(m) ? ms.filter(x => x !== m) : [...ms, m]))
  const pressKey = (k: PanelKey) => send(panelKeyEvents(k, mods))

  // Enter after the text only when the ⏎ toggle is on, and never after a shortcut (Ctrl+C) with Ctrl or Alt;
  // off, a password and its repeat can go into two fields, with Tab or Enter from the panel in between
  const type = (e: FormEvent) => {
    e.preventDefault()
    const shortcut = mods.includes('ctrl') || mods.includes('alt')
    send([...textEvents(text, mods), ...(withEnter && !shortcut ? panelKeyEvents(panelKeys.enter) : [])])
    setText('')
  }

  const toggleEnter = () =>
    setWithEnter(on => {
      saveEnter(!on)
      return !on
    })

  return (
    <Card
      title="Console"
      actions={
        <>
          <span className="muted vm-console-status">{status}</span>
          <button disabled={status !== 'connected'} onClick={() => rfb.current?.sendCtrlAltDel()}>
            Ctrl+Alt+Del
          </button>
          <button
            className="icon-button"
            title="Full screen"
            aria-label="Full screen"
            onClick={() => screen.current?.requestFullscreen?.()}
          >
            <Maximize size={16} aria-hidden />
          </button>
          {status === 'closed' && (
            <button
              onClick={() => {
                failures.current = 0
                setAttempt(a => a + 1)
              }}
            >
              <RotateCcw size={15} aria-hidden /> Reconnect
            </button>
          )}
        </>
      }
    >
      <div ref={screen} className="vm-screen" onClick={() => rfb.current?.focus()} />
      <VmKeys disabled={status !== 'connected'} mods={mods} onToggle={toggle} onKey={pressKey} />
      <form className="vm-type" onSubmit={type}>
        <Keyboard size={16} aria-hidden className="muted" />
        <input
          className="mono"
          placeholder={mods.length ? `${mods.join('+')} + …` : withEnter ? 'Text, then Enter' : 'Text only, no Enter'}
          value={text}
          onChange={e => setText(e.target.value)}
          autoCapitalize="off"
          autoCorrect="off"
          spellCheck={false}
          disabled={status !== 'connected'}
        />
        <button
          type="button"
          className={`icon-button vm-enter${withEnter ? ' on' : ''}`}
          aria-pressed={withEnter}
          title={withEnter ? 'Send presses Enter after the text' : 'Send types the text only'}
          aria-label="Press Enter after the text"
          onClick={toggleEnter}
        >
          <CornerDownLeft size={16} aria-hidden />
        </button>
        <button disabled={status !== 'connected'}>Send</button>
      </form>
    </Card>
  )
}
