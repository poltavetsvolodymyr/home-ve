import { useState, type FormEvent } from 'react'
import { Unauthorized } from '@/shared/api/http'
import { login } from './api'
import './login.css'

/** A login prompt in a frame, as a terminal would show one: the field, then the key to press. */
export function LoginPage({ onLogin }: { onLogin: () => void }) {
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      await login(password)
      onLogin()
    } catch (err) {
      setError(err instanceof Unauthorized ? 'wrong password' : err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login">
      <form className="card login-card" onSubmit={submit}>
        <header className="card-head">
          <h1>home · login</h1>
        </header>
        <p className="hint">The VM host: VMs, disks, backups.</p>
        <label className="login-field">
          <span className="login-prompt">password:</span>
          <input
            type="password"
            autoFocus
            autoComplete="current-password"
            value={password}
            onChange={e => setPassword(e.target.value)}
          />
        </label>
        {error && (
          <div className="error-note" role="alert">
            {error}
          </div>
        )}
        <button className="primary" disabled={busy || !password}>
          {busy ? 'signing in…' : 'sign in ⏎'}
        </button>
      </form>
    </div>
  )
}
