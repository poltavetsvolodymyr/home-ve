import { Play, Power, RotateCcw, Zap } from 'lucide-react'
import { useState } from 'react'
import type { LucideIcon } from 'lucide-react'
import { runVmAction, type Vm, type VmAction } from './api'
import { confirm } from '@/shared/ui'
import { actionLabels, availableActions, confirmAction } from './vmState'

const icons: Record<VmAction, LucideIcon> = { start: Play, shutdown: Power, reboot: RotateCcw, poweroff: Zap }

/** The power buttons that make sense for the VM's state. Asks before interrupting a running VM. */
export function VmActions({ vm, onDone }: { vm: Vm; onDone: (vm: Vm) => void }) {
  const [busy, setBusy] = useState<VmAction | null>(null)
  const [error, setError] = useState<string>()

  const run = async (action: VmAction) => {
    const question = confirmAction(vm.name, action)
    if (question && !(await confirm(question))) return
    setBusy(action)
    setError(undefined)
    try {
      onDone(await runVmAction(vm.name, action))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="vm-actions">
      {availableActions(vm.state).map(action => {
        const Icon = icons[action]
        return (
          <button
            key={action}
            className={action === 'start' ? 'primary' : action === 'poweroff' ? 'danger' : ''}
            disabled={busy !== null}
            onClick={() => run(action)}
          >
            <Icon size={15} aria-hidden /> {busy === action ? '…' : actionLabels[action]}
          </button>
        )
      })}
      {error && <div className="error-note">{error}</div>}
    </div>
  )
}
