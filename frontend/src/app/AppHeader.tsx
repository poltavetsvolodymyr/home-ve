import { ExternalLink, LogOut, Server, Settings } from 'lucide-react'
import { Link } from 'react-router'
import { NavTabs } from './NavTabs'
import { ThemeToggle } from './ThemeToggle'

interface AppHeaderProps {
  onLogout: () => void
}

/** home.example.com → https://router.example.com: the router's own page, next door. */
const routerUrl = location.hostname.startsWith('home.') ? `https://router.${location.hostname.slice(5)}` : null

/** Sticky top bar: brand, page tabs (wide screens only), link to the router, host settings, theme switch and log out. */
export function AppHeader({ onLogout }: AppHeaderProps) {
  return (
    <header className="topbar">
      <div className="topbar-inner">
        <Link className="brand" to="/">
          <span className="brand-mark" aria-hidden>
            <Server size={18} strokeWidth={2} />
          </span>
          <span className="brand-text">
            Home
            <small>VM host</small>
          </span>
        </Link>
        <NavTabs placement="top" />
        <div className="topbar-actions">
          {routerUrl && (
            <a className="icon-button" href={routerUrl} title="Router" aria-label="Open the router's page">
              <ExternalLink size={18} aria-hidden />
            </a>
          )}
          <Link className="icon-button" to="/settings" title="Host settings" aria-label="Host settings">
            <Settings size={18} aria-hidden />
          </Link>
          <ThemeToggle />
          <button className="icon-button" onClick={onLogout} title="Log out" aria-label="Log out">
            <LogOut size={18} aria-hidden />
          </button>
        </div>
      </div>
    </header>
  )
}
