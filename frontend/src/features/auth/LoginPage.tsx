import { useState, type FormEvent } from 'react'
import { Unauthorized } from '@/shared/api/http'
import { login } from './api'
import './login.css'

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
      setError(err instanceof Unauthorized ? 'Wrong password' : err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login">
      <form className="card login-card" onSubmit={submit}>
        <img src="/favicon.svg" alt="" width={40} height={40} />
        <h1>Home</h1>
        <label>
          Password
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
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </div>
  )
}
