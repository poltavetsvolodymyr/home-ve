import { useState } from 'react'
import { dateTime } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { Card, ErrorNote, Skeleton } from '@/shared/ui'
import { fetchVmLogs } from './api'
import { levelClass, levelLabel } from './logLevel'
import './logs.css'

const lineCounts = [100, 200, 500, 1000]
const placeholderWidths = ['70%', '55%', '82%', '64%', '48%', '76%']

/** The journal of vm@{name}.service: QEMU's own messages and systemd's start/stop lines. Newest first. */
export function VmLogs({ name }: { name: string }) {
  const [lines, setLines] = useState(200)
  const { data, error } = usePoll(() => fetchVmLogs(name, lines), 5000, `${name}|${lines}`)

  return (
    <Card
      title="Log"
      className="fit-screen"
      actions={
        <select value={lines} onChange={e => setLines(Number(e.target.value))} aria-label="Lines">
          {lineCounts.map(n => (
            <option key={n} value={n}>
              {n} lines
            </option>
          ))}
        </select>
      }
    >
      <ErrorNote error={error} />
      <div className="log">
        {!data &&
          !error &&
          placeholderWidths.map((w, i) => (
            <div key={i} className="log-line">
              <Skeleton width={w} />
            </div>
          ))}
        {data?.map((l, i) => (
          <div key={i} className={`log-line ${levelClass(l.priority)}`}>
            <span className="log-time">{dateTime(l.time)}</span>
            <span className="log-level">{levelLabel(l.priority)}</span>
            <span className="log-src">{l.source}</span>
            <span className="log-msg">{l.message}</span>
          </div>
        ))}
        {data && data.length === 0 && <div className="muted center">Empty</div>}
      </div>
    </Card>
  )
}
