import { NavLink } from 'react-router'
import { pages } from './routes'

/**
 * The page tabs, as a terminal UI's function keys along the bottom: each with its key (1, 2, 3…: Layout
 * listens for them) and its name. The open one is lit.
 */
export function NavTabs() {
  return (
    <nav className="tabs" aria-label="Pages">
      {pages.map(({ path, label }, i) => (
        // NavLink marks the current page with aria-current="page"
        <NavLink key={path} to={path} className={({ isActive }) => (isActive ? 'on' : '')}>
          <span className="tab-key" aria-hidden>
            {i + 1}
          </span>
          <span className="tab-label">{label}</span>
        </NavLink>
      ))}
      <span className="tabs-hint">press 1–{pages.length} to switch</span>
    </nav>
  )
}
