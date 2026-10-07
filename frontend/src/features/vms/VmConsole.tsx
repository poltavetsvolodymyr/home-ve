import RFB from '@novnc/novnc'
import { Keyboard, Maximize, RotateCcw } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Card } from '@/shared/ui'
import { consoleUrl } from './api'
import { KEY_ENTER, KEY_SHIFT, keysymOf, needsShift } from './keysyms'

type Status = 'connecting' | 'connected' | 'closed'

/**
 * The VM's screen (noVNC over /api/vms/{name}/console). Scaled to fit; click it to type with a real keyboard.
 * A phone has none that noVNC can capture, so there's a text box that types into the VM key by key.
 */
export default function VmConsole({ name }: { name: string }) {
  const screen = useRef<HTMLDivElement>(null)
  const rfb = useRef<RFB | null>(null)
  const [status, setStatus] = useState<Status>('connecting')
  const [attempt, setAttempt] = useState(0)
  const [text, setText] = useState('')

  useEffect(() => {
    if (!screen.current) return
    setStatus('connecting')
    const r = new RFB(screen.current, consoleUrl(name), { wsProtocols: ['binary'] })
    r.scaleViewport = true
    r.resizeSession = false
    r.addEventListener('connect', () => setStatus('connected'))
    r.addEventListener('disconnect', () => setStatus('closed'))
    rfb.current = r
    return () => {
      r.disconnect()
      rfb.current = null
    }
  }, [name, attempt])

  const type = (e: FormEvent) => {
    e.preventDefault()
    const r = rfb.current
    if (!r) return
    for (const ch of text) {
      const shift = needsShift(ch)
      if (shift) r.sendKey(KEY_SHIFT, 'ShiftLeft', true)
      r.sendKey(keysymOf(ch), null)
      if (shift) r.sendKey(KEY_SHIFT, 'ShiftLeft', false)
    }
    r.sendKey(KEY_ENTER, 'Enter')
    setText('')
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
            <button onClick={() => setAttempt(a => a + 1)}>
              <RotateCcw size={15} aria-hidden /> Reconnect
            </button>
          )}
        </>
      }
    >
      <div ref={screen} className="vm-screen" onClick={() => rfb.current?.focus()} />
      <form className="vm-type" onSubmit={type}>
        <Keyboard size={16} aria-hidden className="muted" />
        <input
          className="mono"
          placeholder="Type a line, Enter sends it"
          value={text}
          onChange={e => setText(e.target.value)}
          autoCapitalize="off"
          autoCorrect="off"
          spellCheck={false}
          disabled={status !== 'connected'}
        />
        <button disabled={status !== 'connected'}>Send</button>
      </form>
    </Card>
  )
}
