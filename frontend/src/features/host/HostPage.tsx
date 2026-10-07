import { usePoll } from '@/shared/hooks/usePoll'
import { ErrorNote } from '@/shared/ui'
import { fetchHost } from './api'
import { HostCard } from './HostCard'
import { HostStats } from './HostStats'
import './host.css'

/** The machine itself. Repeats every 2 s, like the samples on the server. */
export function HostPage() {
  const { data, error } = usePoll(fetchHost, 2000)
  return (
    <div className="page">
      <ErrorNote error={error} />
      <HostStats host={data} />
      <HostCard system={data?.system} />
    </div>
  )
}
