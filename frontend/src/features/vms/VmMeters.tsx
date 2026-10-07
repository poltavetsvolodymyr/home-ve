import { bytes, percent } from '@/shared/format'
import { Skeleton } from '@/shared/ui'
import type { Vm } from './api'

/** CPU and memory of a running VM as two thin bars; dashes when it's off. */
export function VmMeters({ vm }: { vm?: Vm }) {
  const running = vm?.state === 'running'
  const memoryShare = vm && vm.memoryBytes !== null ? (vm.memoryBytes / (vm.config.memoryMb * 1024 * 1024)) * 100 : 0
  return (
    <div className="vm-meters">
      <Meter
        label="CPU"
        value={!vm ? undefined : running && vm.cpuPercent !== null ? percent(vm.cpuPercent) : '—'}
        share={running ? (vm?.cpuPercent ?? 0) : 0}
        sub={vm ? `${vm.config.cpus} vCPU` : undefined}
      />
      <Meter
        label="Memory"
        value={!vm ? undefined : running && vm.memoryBytes !== null ? bytes(vm.memoryBytes) : '—'}
        share={running ? memoryShare : 0}
        sub={vm ? `of ${bytes(vm.config.memoryMb * 1024 * 1024)}` : undefined}
      />
    </div>
  )
}

function Meter({ label, value, share, sub }: { label: string; value?: string; share: number; sub?: string }) {
  const level = share >= 90 ? 'critical' : share >= 75 ? 'warning' : ''
  return (
    <div className="vm-meter">
      <div className="vm-meter-head">
        <span className="muted">{label}</span>
        <b>{value ?? <Skeleton width="3em" />}</b>
      </div>
      <div
        className={`meter ${level}`}
        role="meter"
        aria-label={label}
        aria-valuenow={Math.round(share)}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <div style={{ width: `${Math.min(100, Math.max(0, share))}%` }} />
      </div>
      <div className="vm-meter-sub muted">{sub ?? <Skeleton width="5em" />}</div>
    </div>
  )
}
