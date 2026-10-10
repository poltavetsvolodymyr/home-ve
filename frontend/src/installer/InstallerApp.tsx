import { useEffect, useState, type FormEvent } from 'react'
import { Unauthorized } from '@/shared/api/http'
import { BrandMark } from '@/shared/ui'
import { createSession, fetchSession } from './api'
import { MachinePage } from './MachinePage'
import '@/features/auth/login.css'
import './installer.css'

/**
 * The installer image's page: first the code from the machine's screen (whoever has it is in front of the
 * machine), then the machine.
 */
export function InstallerApp() {
  const [status, setStatus] = useState<'checking' | 'code' | 'in'>('checking')

  useEffect(() => {
    fetchSession().then(
      () => setStatus('in'),
      e => setStatus(e instanceof Unauthorized ? 'code' : 'in'),
    )
  }, [])

  if (status === 'checking') return null
  if (status === 'code') return <CodePage onDone={() => setStatus('in')} />
  return <MachinePage />
}

function CodePage({ onDone }: { onDone: () => void }) {
  const [code, setCode] = useState('')
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      await createSession(code)
      onDone()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login">
      <form className="card login-card" onSubmit={submit}>
        <BrandMark large />
        <h1>Install home-ve</h1>
        <p className="muted login-hint">Enter the code shown on the machine&apos;s screen, under its address.</p>
        <label>
          Code
          <input
            className="mono"
            autoFocus
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            placeholder="ABCD-EFGH"
            value={code}
            onChange={e => setCode(e.target.value)}
          />
        </label>
        {error && (
          <div className="error-note" role="alert">
            {error}
          </div>
        )}
        <button className="primary" disabled={busy || !code}>
          {busy ? 'Checking…' : 'Continue'}
        </button>
      </form>
    </div>
  )
}
