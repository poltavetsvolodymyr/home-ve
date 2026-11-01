import { FitAddon } from '@xterm/addon-fit'
import { Terminal } from '@xterm/xterm'
import '@xterm/xterm/css/xterm.css'
import { AArrowDown, AArrowUp, ClipboardPaste, Copy, Maximize, Monitor, MoveHorizontal, RotateCcw } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Badge, Card, confirmEdit, type Status as BadgeStatus } from '@/shared/ui'
import { terminalUrl } from './api'
import type { Modifier, PanelKey } from './keysyms'
import { keyBytes, typed } from './termKeys'
import { VmKeys } from './VmKeys'

type Status = 'connecting' | 'connected' | 'closed'

const statusBadge: Record<Status, BadgeStatus> = { connecting: 'warning', connected: 'good', closed: 'neutral' }

/** As for the screen: after a lost connection (a reboot), try again every 2 s for a minute. */
const retryMs = 2000
const maxRetries = 30
/** Nothing new from the VM this long after connecting: it may not use its serial port at all. */
const silentMs = 3000

const fontSizes = { min: 9, max: 22, default: 13 }
const fontSizeKey = 'home-ve.terminal.fontSize'

/** The font size picked last time, on this device. */
function savedFontSize(): number {
  try {
    const n = Number(localStorage.getItem(fontSizeKey))
    return n >= fontSizes.min && n <= fontSizes.max ? n : fontSizes.default
  } catch {
    return fontSizes.default
  }
}

const encoder = new TextEncoder()

/**
 * The VM's serial console as a terminal (xterm.js over /api/vms/{name}/terminal), next to its screen: text that can
 * be copied, scrolled back and resized, for when SSH can't get in. Opens with the end of the console's log. The
 * guest has to use its serial port for that (console=ttyS0, a getty on ttyS0; docs/deployment.md). A phone lacks
 * Esc, Ctrl and arrows: the same panel of keys as the screen has, here sending what a terminal sends.
 */
export default function VmTerminal({ name, onSetup }: { name: string; onSetup: () => void }) {
  const frame = useRef<HTMLDivElement>(null)
  const box = useRef<HTMLDivElement>(null)
  const term = useRef<Terminal | null>(null)
  const fit = useRef<FitAddon | null>(null)
  const socket = useRef<WebSocket | null>(null)
  const [status, setStatus] = useState<Status>('connecting')
  const [attempt, setAttempt] = useState(0)
  const failures = useRef(0)
  const [mods, setMods] = useState<Modifier[]>([])
  // the terminal's own keyboard input reads the modifiers from here: its handler is set up once
  const modsNow = useRef<Modifier[]>([])
  const [fontSize, setFontSize] = useState(savedFontSize)
  const [copied, setCopied] = useState(false)
  // connected, but the VM has said nothing since (only its log came)
  const [silent, setSilent] = useState(false)

  const send = (text: string) => {
    const s = socket.current
    if (s?.readyState === WebSocket.OPEN && text) s.send(encoder.encode(text))
  }

  const setModifiers = (next: Modifier[]) => {
    modsNow.current = next
    setMods(next)
  }

  // the terminal itself, once
  useEffect(() => {
    if (!box.current) return
    const t = new Terminal({
      fontFamily: getComputedStyle(box.current).fontFamily,
      fontSize,
      scrollback: 5000,
      cursorBlink: true,
      theme: { background: '#000000' },
    })
    const f = new FitAddon()
    t.loadAddon(f)
    t.open(box.current)
    f.fit()
    // typed on a keyboard (a phone's too): with Ctrl or Alt switched on in the panel, the next character is a
    // shortcut (Ctrl+C); escape sequences (a real keyboard's arrows) go out as they are
    const typing = t.onData(data => {
      const m = modsNow.current
      if (m.length && !data.startsWith('\x1b')) {
        modsNow.current = []
        setMods([])
        send(typed(data, m))
      } else {
        send(data)
      }
    })
    // as many columns and rows as fit the box: the window, a phone turned, full screen, the font size
    const resize = new ResizeObserver(() => f.fit())
    resize.observe(box.current)
    term.current = t
    fit.current = f
    return () => {
      resize.disconnect()
      typing.dispose()
      t.dispose()
      term.current = null
      fit.current = null
    }
    // once: the font size is applied below, not by making a new terminal; send only reads refs
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const t = term.current
    if (!t || t.options.fontSize === fontSize) return
    t.options.fontSize = fontSize
    fit.current?.fit()
    try {
      localStorage.setItem(fontSizeKey, String(fontSize))
    } catch {
      // not remembered, still applied
    }
  }, [fontSize])

  // another attempt: the connection effect below runs again
  const reconnect = () => {
    setStatus('connecting')
    setAttempt(a => a + 1)
  }

  // the connection: again on Reconnect or after a drop
  useEffect(() => {
    const s = new WebSocket(terminalUrl(name))
    s.binaryType = 'arraybuffer'
    let retry: number | undefined
    let silence: number | undefined
    let leaving = false
    // the backend sends the end of the log, then an empty message; after that, what the VM says now
    let live = false
    s.onopen = () => {
      failures.current = 0
      setStatus('connected')
      // the backend starts with the end of the log: from a clean screen, not after what the last connection left
      term.current?.reset()
    }
    s.onmessage = e => {
      const bytes = new Uint8Array(e.data as ArrayBuffer)
      if (!live && bytes.length === 0) {
        live = true
        silence = window.setTimeout(() => setSilent(true), silentMs)
        return
      }
      if (live) {
        window.clearTimeout(silence)
        setSilent(false)
      }
      term.current?.write(bytes)
    }
    s.onclose = () => {
      window.clearTimeout(silence)
      setSilent(false)
      if (leaving) return
      setStatus('closed')
      if (++failures.current <= maxRetries) retry = window.setTimeout(reconnect, retryMs)
    }
    socket.current = s
    return () => {
      leaving = true
      window.clearTimeout(retry)
      window.clearTimeout(silence)
      s.close()
      socket.current = null
    }
  }, [name, attempt])

  const toggle = (m: Modifier) => setModifiers(mods.includes(m) ? mods.filter(x => x !== m) : [...mods, m])

  const pressKey = (k: PanelKey) => {
    send(keyBytes(k, mods))
    setModifiers([])
  }

  // what's selected, or else what's on the screen
  const copy = async () => {
    const t = term.current
    if (!t) return
    let text = t.getSelection()
    if (!text) {
      const b = t.buffer.active
      const lines = Array.from({ length: t.rows }, (_, i) => b.getLine(b.viewportY + i)?.translateToString(true) ?? '')
      text = lines.join('\n').replace(/\n+$/, '')
    }
    await navigator.clipboard.writeText(text)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1500)
  }

  // several lines run as commands in a shell: looked over first, as in the screen's paste
  const paste = async () => {
    let text = await navigator.clipboard.readText()
    if (!text) return
    if (/[\r\n]/.test(text.replace(/[\r\n]+$/, ''))) {
      const edited = await confirmEdit({
        title: 'Paste into the VM?',
        message: 'Several lines: in a shell each of them runs as a command. You can change them here first.',
        editText: text.replace(/\r\n?/g, '\n'),
        confirmLabel: 'Paste',
      })
      if (edited === null) return
      text = edited
    }
    term.current?.paste(text)
    term.current?.focus()
  }

  // a serial line has no window size: the shell is told this one (it types the command into the VM)
  const tellSize = () => {
    const t = term.current
    if (t) send(`stty rows ${t.rows} cols ${t.cols}\r`)
  }

  const connected = status === 'connected'
  return (
    <Card
      className="vm-console head-inline"
      title={
        <>
          Terminal <Badge status={statusBadge[status]}>{status}</Badge>
        </>
      }
      actions={
        <>
          {status === 'closed' && (
            <button
              onClick={() => {
                failures.current = 0
                reconnect()
              }}
            >
              <RotateCcw size={15} aria-hidden /> Reconnect
            </button>
          )}
          <button
            className="icon-button"
            title="Full screen"
            aria-label="Full screen"
            onClick={() => frame.current?.requestFullscreen?.()}
          >
            <Maximize size={16} aria-hidden />
          </button>
        </>
      }
    >
      <div ref={frame} className="vm-term">
        <div ref={box} className="vm-term-screen" onClick={() => term.current?.focus()} />
        {silent && connected && (
          <div className="vm-term-silent">
            <p>
              Nothing from the VM yet. Press Enter to wake it. If it stays silent, its system doesn't use the serial
              port: that can be switched on from its screen, in a root shell.
            </p>
            <button onClick={onSetup}>
              <Monitor size={15} aria-hidden /> Set up from the screen
            </button>
          </div>
        )}
        <div className="vm-term-tools">
          <button
            className="icon-button"
            title="Smaller text"
            aria-label="Smaller text"
            disabled={fontSize <= fontSizes.min}
            onClick={() => setFontSize(s => s - 1)}
          >
            <AArrowDown size={16} aria-hidden />
          </button>
          <button
            className="icon-button"
            title="Larger text"
            aria-label="Larger text"
            disabled={fontSize >= fontSizes.max}
            onClick={() => setFontSize(s => s + 1)}
          >
            <AArrowUp size={16} aria-hidden />
          </button>
          <button
            disabled={!connected}
            title="Tell the VM this size: types stty rows … cols … into it"
            onClick={tellSize}
          >
            <MoveHorizontal size={15} aria-hidden /> Size
          </button>
          <button title="Copy the selection, or else the screen" onClick={() => void copy()}>
            <Copy size={15} aria-hidden /> {copied ? 'Copied' : 'Copy'}
          </button>
          <button disabled={!connected} onClick={() => void paste()}>
            <ClipboardPaste size={15} aria-hidden /> Paste
          </button>
        </div>
        <VmKeys disabled={!connected} mods={mods} onToggle={toggle} onKey={pressKey} />
      </div>
    </Card>
  )
}
