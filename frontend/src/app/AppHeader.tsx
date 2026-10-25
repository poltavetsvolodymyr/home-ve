import { ExternalLink, LogOut, Settings } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { fetchHost } from '@/features/host/api'
import { fetchVms } from '@/features/vms/api'
import { clock, percent } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { ThemeToggle } from './ThemeToggle'

interface AppHeaderProps {
  onLogout: () => void
}

/** home.example.com → https://router.example.com: the router's own page, next door. */
const routerUrl = location.hostname.startsWith('home.') ? `https://router.${location.hostname.slice(5)}` : null

/**
 * The status line, as a terminal UI has one: the app's name, then the host at a glance (its name, how many VMs
 * run, load, the time), then host settings, theme and log out. On a phone the less important fields drop out.
 */
export function AppHeader({ onLogout }: AppHeaderProps) {
  const host = usePoll(fetchHost, 5000).data
  const vms = usePoll(fetchVms, 5000).data
  const now = useClock()
  const running = vms?.filter(v => v.state === 'running').length ?? 0
  const failed = vms?.some(v => v.state === 'failed')
  const memory = host && ((host.system.memoryTotal - host.system.memoryAvailable) / host.system.memoryTotal) * 100

  return (
    <header className="statusline">
      <div className="statusline-inner">
        <Link className="brand" to="/">
          HOME
        </Link>
        <span className="status-items">
          {host && <span className="status-wide">{host.system.hostname}</span>}
          {vms && (
            <span className={failed ? 'bad' : 'ok'}>
              ● vms {running}/{vms.length}
            </span>
          )}
          {host && <span className="status-wide">cpu {percent(host.cpuPercent)}</span>}
          {memory !== undefined && <span className="status-wide">mem {percent(memory)}</span>}
          <span>{now}</span>
        </span>
        <span className="topbar-actions">
          {routerUrl && (
            <a className="icon-button" href={routerUrl} title="Router" aria-label="Open the router's page">
              <ExternalLink size={15} aria-hidden />
            </a>
          )}
          <Link className="icon-button" to="/settings" title="Host settings" aria-label="Host settings">
            <Settings size={15} aria-hidden />
          </Link>
          <ThemeToggle />
          <button className="icon-button" onClick={onLogout} title="Log out" aria-label="Log out">
            <LogOut size={15} aria-hidden />
          </button>
        </span>
      </div>
    </header>
  )
}

/** The time, ticking every second, for the status line. */
function useClock() {
  const [now, setNow] = useState(() => clock(Date.now()))
  useEffect(() => {
    const id = setInterval(() => setNow(clock(Date.now())), 1000)
    return () => clearInterval(id)
  }, [])
  return now
}
