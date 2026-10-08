import { Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { Card, confirm } from '@/shared/ui'
import { deleteVm, type Vm } from './api'

/**
 * Deleting a stopped VM: its .conf always, its disk only when asked. Deleting the disk can't be undone,
 * so that takes typing the VM's name; the VM alone takes an OK.
 */
export function VmDelete({ vm }: { vm: Vm }) {
  const navigate = useNavigate()
  const [withDisk, setWithDisk] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const stopped = vm.state === 'stopped' || vm.state === 'failed'
  // only a disk named after the VM is ever deleted (the server checks the same)
  const ownDisk = vm.config.disk.endsWith(`/${vm.name}`)

  const run = async () => {
    const ok = await confirm(
      withDisk
        ? {
            title: `Delete ${vm.name} and its disk?`,
            message: `The disk ${vm.config.disk} goes too: everything on it is lost.`,
            confirmLabel: 'Delete both',
            danger: true,
            typeToConfirm: vm.name,
          }
        : {
            title: `Delete ${vm.name}?`,
            message: `Its disk ${vm.config.disk} stays and can be used by a new VM of the same name.`,
            confirmLabel: 'Delete VM',
            danger: true,
          },
    )
    if (!ok) return
    setBusy(true)
    setError(undefined)
    try {
      await deleteVm(vm.name, withDisk)
      navigate('/vms')
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setBusy(false)
    }
  }

  return (
    <Card title="Delete VM" className="vm-delete">
      {!stopped && <p className="muted">Shut the VM down first.</p>}
      {ownDisk ? (
        <label className="check">
          <input type="checkbox" checked={withDisk} disabled={!stopped} onChange={e => setWithDisk(e.target.checked)} />
          Also delete the disk <span className="mono">{vm.config.disk}</span>
        </label>
      ) : (
        <p className="muted">
          The disk <span className="mono">{vm.config.disk}</span> isn't named after the VM and stays.
        </p>
      )}
      {error && <div className="error-note">{error}</div>}
      <div className="vm-form-buttons">
        <button className="danger" disabled={!stopped || busy} onClick={run}>
          <Trash2 size={15} aria-hidden /> {busy ? 'Deleting…' : withDisk ? 'Delete VM and disk' : 'Delete VM'}
        </button>
      </div>
    </Card>
  )
}
