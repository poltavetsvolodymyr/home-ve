import { CloudUpload } from 'lucide-react'
import { useState } from 'react'
import { levelClass } from '@/features/vms/logLevel'
import { dateTime } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { Badge, Card, confirm, ErrorNote, Skeleton } from '@/shared/ui'
import { fetchOffsite, startOffsite } from './api'
import { confirmOffsite, updateBadge } from './update'
import '@/features/vms/logs.css'

/**
 * The offsite copy: vm-offsite.service (deploy/offsite/vm-offsite) uploads the backups and the host's settings,
 * encrypted, every night after the backups and on "Upload now". Shows the last run and what it printed.
 */
export function OffsiteCard() {
  const { data, error, refresh } = usePoll(fetchOffsite, 3000)
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState<string>()

  const running = data?.state === 'running'
  const badge = data && updateBadge[data.state]

  const start = async () => {
    if (!(await confirm(confirmOffsite))) return
    setStarting(true)
    setStartError(undefined)
    try {
      await startOffsite()
      await refresh()
    } catch (e) {
      setStartError(e instanceof Error ? e.message : String(e))
    } finally {
      setStarting(false)
    }
  }

  return (
    <Card
      title="Offsite backup"
      className="fit-screen head-inline"
      actions={
        <button className="primary" disabled={!data?.configured || running || starting} onClick={start}>
          <CloudUpload size={15} aria-hidden /> {running || starting ? 'Uploading…' : 'Upload now'}
        </button>
      }
    >
      <ErrorNote error={error} />
      <ErrorNote error={startError} />
      {data && !data.configured && (
        <p className="muted">
          Not set up yet: the offsite store is configured by hand on the host, in{' '}
          <span className="mono">/etc/vm-offsite/rclone.conf</span> (docs/deployment.md, «Выгрузка наружу»).
        </p>
      )}

      {data?.configured !== false && (
        <div className="update-state">
          <div className="update-times">
            {badge ? <Badge status={badge.status}>{badge.label}</Badge> : <Skeleton width="8em" />}
            {data?.startedAt && <span className="muted">started {dateTime(data.startedAt)}</span>}
            {data?.finishedAt && <span className="muted">finished {dateTime(data.finishedAt)}</span>}
          </div>
        </div>
      )}

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
