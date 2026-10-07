import { Link } from 'react-router'
import { since } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { Badge, Card, ErrorNote, Skeleton } from '@/shared/ui'
import { fetchVms, type Vm } from './api'
import { VmMeters } from './VmMeters'
import { stateBadge } from './vmState'
import './vms.css'

/** Every VM as a card: state, load, size. A card opens the VM's page. */
export function VmsPage() {
  const { data, error } = usePoll(fetchVms, 2000)

  return (
    <div className="page">
      <ErrorNote error={error} />
      <div className="grid-cards">
        {!data && [0, 1].map(i => <VmCard key={i} />)}
        {data?.map(vm => (
          <VmCard key={vm.name} vm={vm} />
        ))}
      </div>
      {data?.length === 0 && <p className="hint">No VMs yet: each one is a file in /etc/vm on the host.</p>}
    </div>
  )
}

function VmCard({ vm }: { vm?: Vm }) {
  const [status, label] = vm ? stateBadge(vm.state) : ['neutral' as const, '']
  const body = (
    <Card
      className="vm-card"
      title={
        vm ? (
          <>
            {vm.name}
            {vm.config.autostart && <span className="tag">AUTOSTART</span>}
          </>
        ) : (
          <Skeleton width="5em" />
        )
      }
      actions={vm ? <Badge status={status}>{label}</Badge> : <Skeleton width="4em" />}
    >
      <VmMeters vm={vm} />
      <div className="vm-card-foot muted">
        {vm ? (
          <>
            <span>{vm.config.nets.map(n => n.bridge).join(', ') || 'no network'}</span>
            <span>{vm.state === 'running' ? `up ${since(vm.since)}` : vm.config.disk}</span>
          </>
        ) : (
          <Skeleton width="10em" />
        )}
      </div>
    </Card>
  )
  return vm ? (
    <Link className="vm-card-link" to={`/vms/${encodeURIComponent(vm.name)}`}>
      {body}
    </Link>
  ) : (
    body
  )
}
