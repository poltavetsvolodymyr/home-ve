import { Plus, RefreshCw, Trash2 } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { fetchHost } from '@/features/host/api'
import { usePoll } from '@/shared/hooks/usePoll'
import { Card } from '@/shared/ui'
import { fetchBridges, runVmAction, saveVmSettings, type Vm, type VmSettings } from './api'
import { maxNets, needsRestart, newNet, randomMac, settingsFrom, validateSettings } from './settings'

/**
 * CPUs, memory, network cards and autostart. Hardware changes apply at the VM's next start (like on a real
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
  const updateNet = (i: number, patch: Partial<VmSettings['nets'][number]>) =>
    update({ nets: form.nets.map((n, j) => (j === i ? { ...n, ...patch } : n)) })

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
      <form className="vm-form" onSubmit={save}>
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

        <fieldset>
          <legend>Network cards</legend>
          {form.nets.map((n, i) => (
            <div key={i} className="vm-net">
              <span className="muted">{i + 1}</span>
              <select
                aria-label={`Card ${i + 1} bridge`}
                value={n.bridge}
                onChange={e => updateNet(i, { bridge: e.target.value })}
              >
                {!(bridges.data ?? []).includes(n.bridge) && (
                  <option value={n.bridge}>{n.bridge || 'pick a bridge'}</option>
                )}
                {(bridges.data ?? []).map(b => (
                  <option key={b} value={b}>
                    {b}
                  </option>
                ))}
              </select>
              <input
                className="mono"
                aria-label={`Card ${i + 1} MAC`}
                value={n.mac}
                onChange={e => updateNet(i, { mac: e.target.value })}
                spellCheck={false}
                autoCapitalize="characters"
              />
              <button
                type="button"
                className="icon-button"
                title="New MAC"
                aria-label="New MAC"
                onClick={() => updateNet(i, { mac: randomMac() })}
              >
                <RefreshCw size={15} aria-hidden />
              </button>
              <button
                type="button"
                className="icon-button"
                title="Remove card"
                aria-label={`Remove card ${i + 1}`}
                onClick={() => update({ nets: form.nets.filter((_, j) => j !== i) })}
              >
                <Trash2 size={15} aria-hidden />
              </button>
            </div>
          ))}
          {form.nets.length < maxNets && (
            <button type="button" onClick={() => update({ nets: [...form.nets, newNet(bridges.data?.[0] ?? '')] })}>
              <Plus size={15} aria-hidden /> Add card
            </button>
          )}
          <p className="hint">
            Inside the guest a card keeps its name while its MAC stays the same. Changing a MAC is like swapping the
            card.
          </p>
        </fieldset>

        <div className="vm-form-foot">
          <div className="muted">
            Disk: <span className="mono">{vm.config.disk}</span>
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
