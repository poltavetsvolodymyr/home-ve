import { Download, RefreshCw } from 'lucide-react'
import { useState } from 'react'
import { levelClass } from '@/features/vms/logLevel'
import { dateTime } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { Badge, Card, confirm, ErrorNote, Segmented, Skeleton } from '@/shared/ui'
import { type Channel, fetchChannel, fetchUpdate, saveChannel, startUpdate } from './api'
import { confirmDev, confirmUpdate, updateBadge } from './update'
import '@/features/vms/logs.css'

/**
 * The Update button: /opt/home-ve/deploy/update.sh (the channel's latest version + install.sh) as root, through
 * home-update.service. Shows the channel and the version, the last run and what it printed, oldest line first
 * like a terminal.
 */
export function UpdateCard() {
  const { data, error, refresh } = usePoll(fetchUpdate, 2000)
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState<string>()
  // only a run started from this page can have changed the page itself
  const [startedHere, setStartedHere] = useState(false)

  const running = data?.state === 'running'
  const channel = usePoll(fetchChannel, 10000)
  const [channelError, setChannelError] = useState<string>()

  const changeChannel = async (next: Channel) => {
    if (next === channel.data?.channel) return
    if (next === 'dev' && !(await confirm(confirmDev))) return
    setChannelError(undefined)
    try {
      await saveChannel(next)
      await channel.refresh()
    } catch (e) {
      setChannelError(e instanceof Error ? e.message : String(e))
    }
  }
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
      className="fit-screen head-inline"
      actions={
        <button className="primary" disabled={!data || running || starting} onClick={start}>
          <Download size={15} aria-hidden /> {running || starting ? 'Updating…' : 'Update now'}
        </button>
      }
    >
      {/* the backend restarts during an update: a failed poll then is expected, not news */}
      {!running && <ErrorNote error={error} />}
      <ErrorNote error={startError} />
      <ErrorNote error={channelError} />

      <div className="update-channel">
        {channel.data ? (
          <Segmented
            label="Update channel"
            value={channel.data.channel}
            options={[
              { value: 'stable', label: 'Stable' },
              { value: 'dev', label: 'Dev' },
            ]}
            onChange={changeChannel}
          />
        ) : (
          <Skeleton width="9em" />
        )}
        <span className="muted mono">{channel.data?.version ?? ''}</span>
      </div>

      {/* state, started and finished one under another; Reload on the right */}
      <div className="update-state">
        <div className="update-times">
          {badge ? <Badge status={badge.status}>{badge.label}</Badge> : <Skeleton width="8em" />}
          {data?.startedAt && <span className="muted">started {dateTime(data.startedAt)}</span>}
          {data?.finishedAt && <span className="muted">finished {dateTime(data.finishedAt)}</span>}
        </div>
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
