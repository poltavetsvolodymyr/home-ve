import { Download, RefreshCw } from 'lucide-react'
import { useState } from 'react'
import { levelClass } from '@/features/vms/logLevel'
import { dateTime } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { Badge, Card, confirm, ErrorNote, Skeleton } from '@/shared/ui'
import { fetchUpdate, startUpdate } from './api'
import { confirmUpdate, updateBadge } from './update'
import '@/features/vms/logs.css'

/**
 * The Update button: /opt/home-ve/deploy/update.sh (git pull + install.sh) as root, through
 * home-update.service. Shows the last run and what it printed, oldest line first like a terminal.
 */
export function UpdateCard() {
  const { data, error, refresh } = usePoll(fetchUpdate, 2000)
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState<string>()
  // only a run started from this page can have changed the page itself
  const [startedHere, setStartedHere] = useState(false)

  const running = data?.state === 'running'
  const badge = data && updateBadge[data.state]

  const start = async () => {
    if (!(await confirm(confirmUpdate))) return
    setStarting(true)
    setStartError(undefined)
    try {
      await startUpdate()
      setStartedHere(true)
      await refresh()
    } catch (e) {
      setStartError(e instanceof Error ? e.message : String(e))
    } finally {
      setStarting(false)
    }
  }

  return (
    <Card
      title="Update"
      className="fit-screen"
      actions={
        <button className="primary" disabled={!data || running || starting} onClick={start}>
          <Download size={15} aria-hidden /> {running || starting ? 'Updating…' : 'Update now'}
        </button>
      }
    >
      <p className="muted update-hint">
        Runs <span className="mono">/opt/home-ve/deploy/update.sh</span>: pulls the latest version from GitHub and
        installs the backend, this web UI, vm-run and the units. The VMs keep running; a VM picks up a new vm-run at its
        next restart.
      </p>
      {/* the backend restarts during an update: a failed poll then is expected, not news */}
      {!running && <ErrorNote error={error} />}
      <ErrorNote error={startError} />

      <div className="update-state">
        {badge ? <Badge status={badge.status}>{badge.label}</Badge> : <Skeleton width="8em" />}
        {data?.startedAt && <span className="muted">started {dateTime(data.startedAt)}</span>}
        {data?.finishedAt && <span className="muted">finished {dateTime(data.finishedAt)}</span>}
        {startedHere && data?.state === 'succeeded' && (
          <button onClick={() => location.reload()}>
            <RefreshCw size={15} aria-hidden /> Reload page
          </button>
        )}
      </div>

      {data && data.log.length > 0 && (
        <div className="log update-log">
          {[...data.log].reverse().map((l, i) => (
            <div key={i} className={`log-line ${levelClass(l.priority)}`}>
              <span className="log-msg">{l.message}</span>
            </div>
          ))}
        </div>
      )}
    </Card>
  )
}
