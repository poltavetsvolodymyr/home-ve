import { ArrowLeft } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { fetchHost } from '@/features/host/api'
import { usePoll } from '@/shared/hooks/usePoll'
import { Card } from '@/shared/ui'
import { createVm, fetchBridges, fetchVms, type VmCreate } from './api'
import { CdromField } from './CdromField'
import { NetsEditor } from './NetsEditor'
import { maxDiskSizeGb, newVm, validateNewVm } from './settings'
import './vms.css'

/**
 * A new VM: a new /etc/vm/<name>.conf. Its disk (a thin volume named after it) is created by vm-run at the
 * first start. With an ISO in the CD drive and "start now", the console opens on the installer.
 */
export function NewVmPage() {
  const navigate = useNavigate()
  const host = usePoll(fetchHost, 0)
  const bridges = usePoll(fetchBridges, 0)
  const vms = usePoll(fetchVms, 0)
  const [form, setForm] = useState<VmCreate>(() => newVm(''))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string>()

  // the first card goes on the first bridge once the list arrives
  const nets = form.nets.map(n => (n.bridge ? n : { ...n, bridge: bridges.data?.[0] ?? '' }))
  const vm = { ...form, nets }

  const hostCpus = host.data?.system.cpuCount ?? 64
  const hostMemoryMb = Math.floor((host.data?.system.memoryTotal ?? 256 * 2 ** 30) / 2 ** 20)
  const problems = validateNewVm(
    vm,
    (vms.data ?? []).map(v => v.name),
    hostCpus,
    hostMemoryMb,
  )
  const update = (patch: Partial<VmCreate>) => setForm(f => ({ ...f, nets, ...patch }))

  const create = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError(undefined)
    try {
      await createVm({ ...vm, nets: vm.nets.map(n => ({ ...n, mac: n.mac.toUpperCase() })) })
      navigate(`/vms/${encodeURIComponent(vm.name)}${vm.start ? '?tab=console' : ''}`)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setSaving(false)
    }
  }

  return (
    <div className="page">
      <Link className="back-link" to="/vms">
        <ArrowLeft size={16} aria-hidden /> All VMs
      </Link>
      <Card title="New VM">
        {/* our own checks (settings.ts) say what's wrong; the browser's would block silently */}
        <form className="vm-form" onSubmit={create} noValidate>
          <label>
            <span>Name</span>
            <input
              value={form.name}
              onChange={e => update({ name: e.target.value.toLowerCase() })}
              autoCapitalize="off"
              autoCorrect="off"
              spellCheck={false}
              maxLength={12}
              placeholder="e.g. web"
            />
            <small className="muted">a–z, 0–9, dashes</small>
          </label>
          <label>
            <span>CPUs</span>
            <input
              type="number"
              inputMode="numeric"
              min={1}
              max={hostCpus}
              value={form.cpus}
              onChange={e => update({ cpus: e.target.valueAsNumber })}
            />
            <small className="muted">the host has {hostCpus}</small>
          </label>
          <label>
            <span>Memory, MiB</span>
            <input
              type="number"
              inputMode="numeric"
              min={128}
              step={256}
              value={form.memoryMb}
              onChange={e => update({ memoryMb: e.target.valueAsNumber })}
            />
            <small className="muted">
              {(form.memoryMb / 1024).toFixed(1)} GiB of the host's {(hostMemoryMb / 1024).toFixed(0)} GiB
            </small>
          </label>
          <label>
            <span>Disk, GiB</span>
            <input
              type="number"
              inputMode="numeric"
              min={1}
              max={maxDiskSizeGb}
              value={form.diskSizeGb}
              onChange={e => update({ diskSizeGb: e.target.valueAsNumber })}
            />
            <small className="muted">thin: takes space as the guest writes</small>
          </label>
          <CdromField value={form.cdrom} onChange={cdrom => update({ cdrom })} />
          <label className="check">
            <input type="checkbox" checked={form.autostart} onChange={e => update({ autostart: e.target.checked })} />
            Start at boot
          </label>
          <label className="check">
            <input type="checkbox" checked={form.backup} onChange={e => update({ backup: e.target.checked })} />
            Back up every night
          </label>

          <NetsEditor nets={nets} bridges={bridges.data ?? []} onChange={nets => update({ nets })} />

          <div className="vm-form-foot">
            <div className="muted">
              Disk: <span className="mono">/dev/home/{form.name || '<name>'}</span>, created at the first start. A disk
              left over from a deleted VM of the same name is used as it is.
            </div>
            {form.name && problems.length > 0 && (
              <ul className="error-note vm-problems">
                {problems.map(p => (
                  <li key={p}>{p}</li>
                ))}
              </ul>
            )}
            {error && <div className="error-note">{error}</div>}
            <label className="check">
              <input type="checkbox" checked={form.start} onChange={e => update({ start: e.target.checked })} />
              Start now and open the console
            </label>
            <div className="vm-form-buttons">
              <button className="primary" disabled={saving || problems.length > 0}>
                {saving ? 'Creating…' : 'Create'}
              </button>
            </div>
          </div>
        </form>
      </Card>
    </div>
  )
}
