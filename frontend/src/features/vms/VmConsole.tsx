import RFB from '@novnc/novnc'
import { CornerDownLeft, Keyboard, Maximize, RotateCcw, X } from 'lucide-react'
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent, type KeyboardEvent } from 'react'
import { Card } from '@/shared/ui'
import { consoleUrl } from './api'
import { lineEdit, panelKeyEvents, panelKeys, textEvents, type KeyEvent, type Modifier, type PanelKey } from './keysyms'
import { VmKeys } from './VmKeys'

type Status = 'connecting' | 'connected' | 'closed'

/** After a lost connection (a reboot: QEMU goes and a new one comes), try again every 2 s for a minute. */
const retryMs = 2000
const maxRetries = 30

/**
 * The VM's screen (noVNC over /api/vms/{name}/console). Scaled to fit; click it to type with a real keyboard.
 * A phone has none that noVNC can capture, so there's a panel of keys (arrows, Esc, F-keys, Ctrl…) and a
 * text box that types into the VM as you type in it: every change to the box goes out as keys at once.
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

  // The box mirrors the line typed in the VM since the last Enter, so the panel's Enter and ⌫ keep it in step
  const pressKey = (k: PanelKey) => {
    send(panelKeyEvents(k, mods))
    if (k === panelKeys.enter) setText('')
    if (k === panelKeys.backspace) setText(t => [...t].slice(0, -1).join(''))
  }

  // Every change goes to the VM right away: Backspaces for what was removed, then what was added
  // (lineEdit), so autocorrect or a picked suggestion swapping a word comes out right too. With Ctrl or Alt
  // on, the characters are a shortcut (Ctrl+C), sent but not kept in the box.
  const edit = (e: ChangeEvent<HTMLInputElement>) => {
    const next = e.target.value
    const { backspaces, typed } = lineEdit(text, next)
    if (!backspaces && !typed) return
    const shortcut = mods.includes('ctrl') || mods.includes('alt')
    send([
      ...Array.from({ length: backspaces }, () => panelKeyEvents(panelKeys.backspace)).flat(),
      ...textEvents(typed, mods),
    ])
    if (!shortcut) setText(next)
  }

  // ⌫ in an empty box changes nothing in it, so no change event: it goes to the VM from here
  const keyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Backspace' && text === '') {
      e.preventDefault()
      send(panelKeyEvents(panelKeys.backspace, mods))
    }
  }

  // the phone keyboard's Return and the Enter button: Enter in the VM, a fresh line in the box
  const enter = (e?: FormEvent) => {
    e?.preventDefault()
    pressKey(panelKeys.enter)
  }

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
      <form className="vm-type" onSubmit={enter}>
        <Keyboard size={16} aria-hidden className="muted" />
        <input
          className="mono"
          placeholder={mods.length ? `${mods.join('+')} + …` : 'Type into the VM'}
          value={text}
          onChange={edit}
          onKeyDown={keyDown}
          autoCapitalize="off"
          autoCorrect="off"
          autoComplete="off"
          spellCheck={false}
          enterKeyHint="enter"
          disabled={status !== 'connected'}
        />
        {text && (
          <button
            type="button"
            className="icon-button"
            title="Clear the box (the VM keeps what was typed)"
            aria-label="Clear the box"
            onClick={() => setText('')}
          >
            <X size={16} aria-hidden />
          </button>
        )}
        <button disabled={status !== 'connected'}>
          <CornerDownLeft size={15} aria-hidden /> Enter
        </button>
      </form>
    </Card>
  )
}
