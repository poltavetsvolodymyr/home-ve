import { useMemo } from 'react'
import { RouterProvider } from 'react-router'
import { LoginPage } from '@/features/auth/LoginPage'
import { useAuthState } from '@/features/auth/useAuthState'
import { SessionContext } from '@/shared/session'
import { ConfirmHost } from '@/shared/ui'
import { router } from './router'
import './app.css'

/**
 * The pages, or the login page once the server says there is no session. While that is being
 * checked the pages already render: a saved session is the usual case, and a 401 from any of their
 * requests signs out as well. The URL stays as it was, so after logging in the same page opens.
 */
export function App() {
  const { status, signedIn, logout } = useAuthState()
  const session = useMemo(() => ({ logout }), [logout])

  if (status === 'signed-out') return <LoginPage onLogin={signedIn} />

  return (
    <SessionContext.Provider value={session}>
      <RouterProvider router={router} />
      <ConfirmHost />
    </SessionContext.Provider>
  )
}
