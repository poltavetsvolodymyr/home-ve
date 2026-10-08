import { useState, type FormEvent } from 'react'
import { fetchHost } from '@/features/host/api'
import { usePoll } from '@/shared/hooks/usePoll'
import { Card } from '@/shared/ui'
import { fetchBridges, runVmAction, saveVmSettings, type Vm, type VmSettings } from './api'
import { CdromField } from './CdromField'
import { NetsEditor } from './NetsEditor'
import { needsRestart, settingsFrom, validateSettings } from './settings'

/**
 * CPUs, memory, CD drive, network cards, autostart and the nightly backup. Hardware changes apply at the VM's next start (like on a real
 * machine), so after saving a running VM the form offers to reboot it.
 */
export function VmSettingsForm({ vm, onSaved }: { vm: Vm; onSaved: () => void }) {
  const [form, setForm] = useState<VmSettings>(() => settingsFrom(vm.config))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string>()
  const [restartPending, setRestartPending] = useState(false)
  const host = usePoll(fetchHost, 0)
  const bridges = usePoll(fetchBridges, 0)

  const hostCpus = host.data?.system.cpuCount ?? 64
  const hostMemoryMb = Math.floor((host.data?.system.memoryTotal ?? 256 * 2 ** 30) / 2 ** 20)
  const problems = validateSettings(form, hostCpus, hostMemoryMb)
  const changed = JSON.stringify(form) !== JSON.stringify(settingsFrom(vm.config))

  const update = (patch: Partial<VmSettings>) => setForm(f => ({ ...f, ...patch }))

  const save = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError(undefined)
    try {
      await saveVmSettings(vm.name, { ...form, nets: form.nets.map(n => ({ ...n, mac: n.mac.toUpperCase() })) })
      setRestartPending(vm.state === 'running' && needsRestart(vm.config, form))
      onSaved()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setSaving(false)
    }
  }

  const reboot = async () => {
    if (!window.confirm(`Reboot ${vm.name} now to apply the new settings?`)) return
    await runVmAction(vm.name, 'reboot').catch(err => setError(err instanceof Error ? err.message : String(err)))
    setRestartPending(false)
    onSaved()
  }

  return (
    <Card title="Settings">
      {/* our own checks (settings.ts) say what's wrong; the browser's would block silently, e.g. on memory
          that isn't a multiple of the step */}
      <form className="vm-form" onSubmit={save} noValidate>
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
        <label className="check">
          <input type="checkbox" checked={form.autostart} onChange={e => update({ autostart: e.target.checked })} />
          Start at boot
        </label>
        <label className="check">
          <input type="checkbox" checked={form.backup} onChange={e => update({ backup: e.target.checked })} />
          Back up every night
        </label>

        <CdromField value={form.cdrom} onChange={cdrom => update({ cdrom })} />

        <NetsEditor nets={form.nets} bridges={bridges.data ?? []} onChange={nets => update({ nets })} />

        <div className="vm-form-foot">
          <div className="muted">
            Disk: <span className="mono">{vm.config.disk}</span>
            {vm.config.diskSizeGb && ` · ${vm.config.diskSizeGb} GiB`}
          </div>
          {problems.length > 0 && (
            <ul className="error-note vm-problems">
              {problems.map(p => (
                <li key={p}>{p}</li>
              ))}
            </ul>
          )}
          {error && <div className="error-note">{error}</div>}
          <div className="vm-form-buttons">
            <button type="button" disabled={!changed || saving} onClick={() => setForm(settingsFrom(vm.config))}>
              Undo
            </button>
            <button className="primary" disabled={!changed || saving || problems.length > 0}>
              {saving ? 'Saving…' : 'Save'}
            </button>
          </div>
          {restartPending && (
            <div className="vm-restart">
              Saved. The VM picks up the new hardware when it restarts.
              <button onClick={reboot}>Reboot now</button>
            </div>
          )}
        </div>
      </form>
    </Card>
  )
}
