import { since } from '@/shared/format'
import { Card, Skeleton } from '@/shared/ui'
import type { Vm } from './api'
import { VmMeters } from './VmMeters'

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
        </dl>
      </Card>
      <Card title="Hardware">
        <dl className="kv">
          <dt>CPUs</dt>
          <dd>{c ? c.cpus : <Skeleton width="2em" />}</dd>
          <dt>Memory</dt>
          <dd>{c ? `${c.memoryMb} MiB` : <Skeleton width="5em" />}</dd>
          <dt>Disk</dt>
          <dd className="mono">{c ? c.disk : <Skeleton width="9em" />}</dd>
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
