import { useState, type FormEvent } from 'react'
import { setUp } from './api'
import { setupProblem } from './setup'
import './login.css'

/**
 * The first-run setup: the host has no password yet. Whoever sets it owns the host, so it takes the setup
 * code, which is only on the host itself (install.sh prints it).
 */
export function SetupPage({ onDone }: { onDone: () => void }) {
  const [code, setCode] = useState('')
  const [password, setPassword] = useState('')
  const [repeat, setRepeat] = useState('')
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    const problem = setupProblem(code, password, repeat)
    if (problem) return setError(problem)
    setBusy(true)
    setError(undefined)
    try {
      await setUp(code, password)
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
        <img src="/favicon.svg" alt="" width={40} height={40} />
        <h1>Welcome</h1>
        <p className="muted login-hint">
          This host has no password yet. Choose one; you'll sign in with it from now on. The setup code is on the host:{' '}
          <span className="mono">install.sh</span> printed it, or{' '}
          <span className="mono">cat /var/lib/home-backend/setup-code</span>
        </p>
        <label>
          Setup code
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
        <label>
          Password
          <input
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={e => setPassword(e.target.value)}
          />
        </label>
        <label>
          Repeat the password
          <input type="password" autoComplete="new-password" value={repeat} onChange={e => setRepeat(e.target.value)} />
        </label>
        {error && (
          <div className="error-note" role="alert">
            {error}
          </div>
        )}
        <button className="primary" disabled={busy || !code || !password || !repeat}>
          {busy ? 'Saving…' : 'Set password and sign in'}
        </button>
      </form>
    </div>
  )
}
