import { useCallback, useEffect, useState } from 'react'
import { Unauthorized } from '@/shared/api/http'
import { clearPollCache } from '@/shared/hooks/usePoll'
import { fetchMe, logout as logoutRequest } from './api'

export type AuthStatus = 'checking' | 'signed-in' | 'signed-out'

/**
 * Whether to show the login page. Asks the server once at start. Only a 401 means signed out:
 * on any other error the app opens and its pages show what's wrong with the server.
 */
export function useAuthState() {
  const [status, setStatus] = useState<AuthStatus>('checking')

  useEffect(() => {
    fetchMe().then(
      () => setStatus('signed-in'),
      e => setStatus(e instanceof Unauthorized ? 'signed-out' : 'signed-in'),
    )
  }, [])

  const signedIn = useCallback(() => setStatus('signed-in'), [])

  /** `callServer = false` when the server already told us the session is gone. */
  const logout = useCallback((callServer = true) => {
    if (callServer) logoutRequest().catch(() => {})
    clearPollCache()
    setStatus('signed-out')
  }, [])

  return { status, signedIn, logout }
}
