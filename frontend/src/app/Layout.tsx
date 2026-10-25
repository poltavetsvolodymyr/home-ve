import { useEffect } from 'react'
import { Outlet, useNavigate } from 'react-router'
import { useSession } from '@/shared/session'
import { AppHeader } from './AppHeader'
import { NavTabs } from './NavTabs'
import { pages } from './routes'

/**
 * The status line, the page of the current path and the tab bar. Only the page changes between tabs.
 * The number keys open the tabs, as the function keys do in a terminal UI; not while typing in a field.
 */
export function Layout() {
  const { logout } = useSession()
  const navigate = useNavigate()

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.altKey || e.ctrlKey || e.metaKey) return
      if (e.target instanceof Element && e.target.closest('input, textarea, select, [contenteditable]')) return
      const i = Number(e.key) - 1
      if (Number.isInteger(i) && i >= 0 && i < pages.length) navigate(pages[i].path)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [navigate])

  return (
    <>
      <AppHeader onLogout={() => logout()} />
      <main>
        <Outlet />
      </main>
      <NavTabs />
    </>
  )
}
