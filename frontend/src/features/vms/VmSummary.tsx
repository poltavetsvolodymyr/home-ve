import { since } from '@/shared/format'
import { Card, Skeleton } from '@/shared/ui'
import type { Vm } from './api'
import { VmMeters } from './VmMeters'

/** The guest's addresses, one per line with its interface; they come from the guest agent. */
function VmAddresses({ vm }: { vm: Vm }) {
  if (vm.state !== 'running') return '—'
  if (!vm.addresses) return <span className="muted">unknown: needs qemu-guest-agent in the VM</span>
  if (vm.addresses.length === 0) return 'none'
  return (
    <ul className="vm-addresses">
      {vm.addresses.map(a => (
        <li key={`${a.interface} ${a.address}`}>
          <span className="mono">{a.address}</span>
          <span className="muted">
            /{a.prefix} · {a.interface}
          </span>
        </li>
      ))}
    </ul>
  )
}

/** Load and the hardware the VM was given. */
export function VmSummary({ vm }: { vm?: Vm }) {
  const c = vm?.config
  return (
    <div className="grid-cards">
      <Card title="Load">
        <VmMeters vm={vm} />
        <dl className="kv vm-uptime">
          <dt>Up</dt>
          <dd>{vm ? vm.state === 'running' ? since(vm.since) : '—' : <Skeleton width="4em" />}</dd>
          <dt>Addresses</dt>
          <dd>{vm ? <VmAddresses vm={vm} /> : <Skeleton width="8em" />}</dd>
        </dl>
      </Card>
      <Card title="Hardware">
        <dl className="kv">
          <dt>CPUs</dt>
          <dd>{c ? c.cpus : <Skeleton width="2em" />}</dd>
          <dt>Memory</dt>
          <dd>{c ? `${c.memoryMb} MiB` : <Skeleton width="5em" />}</dd>
          <dt>Disk</dt>
          <dd>
            {c ? (
              <>
                <span className="mono">{c.disk}</span>
                {c.diskSizeGb && <span className="muted"> · {c.diskSizeGb} GiB</span>}
              </>
            ) : (
              <Skeleton width="9em" />
            )}
          </dd>
          <dt>CD drive</dt>
          <dd>{c ? c.cdrom ? <span className="mono">{c.cdrom}</span> : 'empty' : <Skeleton width="6em" />}</dd>
          {c ? (
            c.nets.map((n, i) => (
              <div key={n.mac} className="kv-row">
                <dt>Network {i + 1}</dt>
                <dd>
                  {n.bridge} <span className="mono muted">{n.mac}</span>
                </dd>
              </div>
            ))
          ) : (
            <div className="kv-row">
              <dt>Network</dt>
              <dd>
                <Skeleton width="12em" />
              </dd>
            </div>
          )}
          <dt>Autostart</dt>
          <dd>{c ? c.autostart ? 'yes, at boot' : 'no' : <Skeleton width="3em" />}</dd>
        </dl>
      </Card>
    </div>
  )
}
