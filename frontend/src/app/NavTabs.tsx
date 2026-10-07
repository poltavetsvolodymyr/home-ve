import { NavLink } from 'react-router'
import { pages } from './routes'

interface NavTabsProps {
  /** "top": a segmented pill in the header on wide screens; "bottom": a tab bar at the bottom on phones. */
  placement: 'top' | 'bottom'
}

/** The page tabs. The app renders both placements; CSS shows the one that fits the screen. */
export function NavTabs({ placement }: NavTabsProps) {
  return (
    <nav className={`tabs tabs-${placement}`} aria-label="Pages">
      {pages.map(({ path, label, icon: Icon }) => (
        // NavLink marks the current page with aria-current="page"
        <NavLink key={path} to={path} className={({ isActive }) => (isActive ? 'on' : '')}>
          <span className="tab-icon">
            <Icon size={placement === 'top' ? 16 : 22} strokeWidth={placement === 'top' ? 2 : 1.75} aria-hidden />
          </span>
          <span className="tab-label">{label}</span>
        </NavLink>
      ))}
    </nav>
  )
}
