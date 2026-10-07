import { bytes, duration, percent } from '@/shared/format'
import { Skeleton, Stat } from '@/shared/ui'
import type { Host } from './api'
import { temperatureTile } from './cpuTemperature'
import { LoadAverage } from './LoadAverage'

/** CPU, temperature, memory, disk and uptime of the host. Placeholders until the first answer. */
export function HostStats({ host }: { host?: Host }) {
  if (!host) return <PlaceholderStats />

  const { system: s, cpuPercent, cpuTemperature } = host
  const memUsed = s.memoryTotal - s.memoryAvailable
  const diskUsed = s.diskTotal - s.diskFree
  const temperature = temperatureTile(cpuTemperature)

  return (
    <div className="stats">
      <Stat
        label="CPU"
        value={percent(cpuPercent)}
        meter={cpuPercent}
        sub={<LoadAverage values={s.loadAverage} cores={s.cpuCount} />}
      />
      <Stat
        label="CPU temperature"
        value={temperature.value}
        meter={temperature.meter}
        sub={temperature.sub.map(line => (
          <div key={line}>{line}</div>
        ))}
      />
      <Stat
        label="Memory"
        value={bytes(memUsed)}
        meter={(memUsed / s.memoryTotal) * 100}
        sub={`of ${bytes(s.memoryTotal)}`}
      />
      <Stat
        label="System disk"
        value={bytes(diskUsed)}
        meter={(diskUsed / s.diskTotal) * 100}
        sub={`${bytes(s.diskFree)} free`}
      />
      <Stat label="Uptime" value={duration(s.uptimeSeconds)} sub={s.hostname} />
    </div>
  )
}

/** The same tiles before the first answer: labels, empty meters, and as many sub lines as the real ones. */
function PlaceholderStats() {
  const lines = (n: number) =>
    Array.from({ length: n }, (_, i) => (
      <div key={i}>
        <Skeleton width="8em" />
      </div>
    ))
  const tile = (label: string, meter: boolean, subLines: number) => (
    <Stat label={label} value={<Skeleton width="3em" />} meter={meter ? 0 : undefined} sub={lines(subLines)} />
  )

  return (
    <div className="stats">
      {tile('CPU', true, 3)}
      {tile('CPU temperature', true, 1)}
      {tile('Memory', true, 1)}
      {tile('System disk', true, 1)}
      {tile('Uptime', false, 1)}
    </div>
  )
}
