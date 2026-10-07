import { Card, Skeleton } from '@/shared/ui'
import type { SystemInfo } from './api'

/** Host name, OS and kernel. */
export function HostCard({ system }: { system?: SystemInfo }) {
  const value = (v: string | undefined, width: string) => v ?? <Skeleton width={width} />
  return (
    <Card title="Host">
      <dl className="kv">
        <dt>Name</dt>
        <dd>{value(system?.hostname, '5em')}</dd>
        <dt>OS</dt>
        <dd>{value(system?.os, '14em')}</dd>
        <dt>Kernel</dt>
        <dd>{value(system?.kernel, '10em')}</dd>
        <dt>CPUs</dt>
        <dd>{system ? system.cpuCount : <Skeleton width="2em" />}</dd>
      </dl>
    </Card>
  )
}
