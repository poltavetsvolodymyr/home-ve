import { Outlet } from 'react-router'
import { useSession } from '@/shared/session'
import { AppHeader } from './AppHeader'
import { NavTabs } from './NavTabs'

/** Header, the page of the current path and (on phones) the tab bar. Only the page changes between tabs. */
export function Layout() {
  const { logout } = useSession()
  return (
    <>
      <AppHeader onLogout={() => logout()} />
      <main>
        <Outlet />
      </main>
      {/* outside the header: its backdrop-filter would pin a fixed bar to the header instead of the screen */}
      <NavTabs placement="bottom" />
    </>
  )
}
