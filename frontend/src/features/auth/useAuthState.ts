import { useCallback, useEffect, useState } from 'react'
import { Unauthorized } from '@/shared/api/http'
import { clearPollCache } from '@/shared/hooks/usePoll'
import { fetchMe, fetchSetup, logout as logoutRequest } from './api'

/** `setup`: the host has no password yet, the first-run setup sets one. */
export type AuthStatus = 'checking' | 'signed-in' | 'signed-out' | 'setup'

/**
 * Whether to show the login page (or the first-run setup). Asks the server once at start. Only a 401 means
 * signed out: on any other error the app opens and its pages show what's wrong with the server.
 */
export function useAuthState() {
  const [status, setStatus] = useState<AuthStatus>('checking')

  useEffect(() => {
    fetchMe().then(
      () => setStatus('signed-in'),
      e => {
        if (!(e instanceof Unauthorized)) return setStatus('signed-in')
        fetchSetup().then(
          s => setStatus(s.needed ? 'setup' : 'signed-out'),
          () => setStatus('signed-out'),
        )
      },
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
