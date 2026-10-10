import { Disc3, Plus } from 'lucide-react'
import { Link } from 'react-router'
import { since } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { Badge, Card, ErrorNote, Skeleton } from '@/shared/ui'
import { fetchVms, type Vm } from './api'
import { VmMeters } from './VmMeters'
import { stateBadge } from './vmState'
import './vms.css'

/** Every VM as a card: state, load, size. A card opens the VM's page. Above them: new VM, ISO images. */
export function VmsPage() {
  const { data, error } = usePoll(fetchVms, 2000)

  return (
    <div className="page">
      <div className="vms-toolbar">
        <Link className="button primary" to="/vms/new">
          <Plus size={16} aria-hidden /> New VM
        </Link>
        <Link className="button" to="/isos">
          <Disc3 size={16} aria-hidden /> ISO images
        </Link>
      </div>
      <ErrorNote error={error} />
      <div className="grid-cards">
        {!data && [0, 1].map(i => <VmCard key={i} />)}
        {data?.map(vm => (
          <VmCard key={vm.name} vm={vm} />
        ))}
      </div>
      {data?.length === 0 && (
        <p className="hint">No VMs yet. New VM makes one; each is a file in /etc/vm on the host.</p>
      )}
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
            {/* where to reach it, once its guest agent tells; until then the bridges it is plugged into */}
            {vm.state === 'running' && vm.addresses?.length ? (
              <span className="mono">
                {vm.addresses[0].address}
                {vm.addresses.length > 1 && <span className="muted"> +{vm.addresses.length - 1}</span>}
              </span>
            ) : (
              <span>{vm.config.nets.map(n => n.bridge).join(', ') || 'no network'}</span>
            )}
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
