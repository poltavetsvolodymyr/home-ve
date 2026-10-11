import { useState, type FormEvent } from 'react'
import { bytes } from '@/shared/format'
import { Card, confirm, Segmented } from '@/shared/ui'
import { startInstall, type Disk, type InstallRequest, type Machine, type Nic } from './api'
import {
  browserTimezone,
  diskGb,
  newForm,
  poolGb,
  sizesFor,
  staticFrom,
  timezones,
  toRequest,
  validate,
  type InstallForm as Form,
} from './plan'

/** The install's settings: the disk and how it is split, the network, the host, the passwords. */
export function InstallForm({ machine: m, onStarted }: { machine: Machine; onStarted: () => void }) {
  const [form, setForm] = useState<Form>(() => newForm(m, browserTimezone()))
  const [tried, setTried] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const update = (patch: Partial<Form>) => setForm(f => ({ ...f, ...patch }))

  const disk = m.disks.find(d => d.name === form.disk)
  const problems = validate(form, m)

  async function install(e: FormEvent) {
    e.preventDefault()
    setTried(true)
    if (problems.length > 0 || !disk) return
    const ok = await confirm({
      title: `Erase /dev/${disk.name} and install?`,
      message: `Everything on /dev/${disk.name} (${[disk.model, bytes(disk.sizeBytes)].filter(Boolean).join(', ')}) is erased for good.`,
      confirmLabel: 'Erase and install',
      danger: true,
    })
    if (!ok) return
    setBusy(true)
    setError(undefined)
    try {
      await startInstall(toRequest(form))
      onStarted()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setBusy(false)
    }
  }

  return (
    <form className="installer-form" onSubmit={install} noValidate>
      <Card title="Disk">
        <p className="muted installer-note">The one home-ve goes onto. Everything on it is erased.</p>
        <ul className="installer-list installer-choices">
          {m.disks.map(d => (
            <li key={d.name}>
              <label className={d.inUse ? 'muted' : undefined}>
                <input
                  type="radio"
                  name="disk"
                  disabled={d.inUse}
                  checked={form.disk === d.name}
                  onChange={() => update({ disk: d.name, ...sizesFor(d, m.memoryBytes) })}
                />
                <DiskRow disk={d} />
              </label>
            </li>
          ))}
        </ul>
        {m.disks.length === 0 && <p className="muted">No disks found.</p>}
      </Card>

      {disk && (
        <Card title={`Space on /dev/${disk.name}`}>
          <SpaceBar form={form} disk={disk} />
          <div className="installer-fields">
            <SizeField
              label="System"
              hint="Debian and home-ve"
              value={form.rootGb}
              onChange={rootGb => update({ rootGb })}
            />
            <SizeField label="Swap" value={form.swapGb} onChange={swapGb => update({ swapGb })} />
            <SizeField
              label="Backups"
              hint="the VMs' nightly backups; 0: none"
              value={form.backupGb}
              onChange={backupGb => update({ backupGb })}
            />
          </div>
        </Card>
      )}

      <Card title="Network">
        <p className="muted installer-note">The card the VMs share with the host (the bridge br0).</p>
        <ul className="installer-list installer-choices">
          {m.nics.map(n => (
            <li key={n.name}>
              <label className={n.wireless ? 'muted' : undefined}>
                <input
                  type="radio"
                  name="nic"
                  disabled={n.wireless}
                  checked={form.nic === n.name}
                  onChange={() => update({ nic: n.name, ...staticFrom(n) })}
                />
                <NicRow nic={n} />
              </label>
            </li>
          ))}
        </ul>
        {m.nics.length === 0 && <p className="muted">No network cards found.</p>}
        <Segmented
          label="Address"
          value={form.network}
          options={[
            { value: 'dhcp', label: 'Automatic (DHCP)' },
            { value: 'static', label: 'Fixed address' },
          ]}
          onChange={network => update({ network })}
        />
        {form.network === 'dhcp' ? (
          <p className="muted installer-note">From the router, the same address the installer has now.</p>
        ) : (
          <div className="installer-fields">
            <TextField
              label="Address"
              placeholder="192.168.1.10/24"
              value={form.address}
              onChange={address => update({ address })}
            />
            <TextField
              label="Gateway"
              placeholder="192.168.1.1"
              value={form.gateway}
              onChange={gateway => update({ gateway })}
            />
            <TextField
              label="DNS"
              placeholder="192.168.1.1 1.1.1.1"
              value={form.dns}
              onChange={dns => update({ dns })}
            />
          </div>
        )}
      </Card>

      <Card title="This host">
        <div className="installer-fields">
          <TextField
            label="Host name"
            value={form.hostname}
            onChange={hostname => update({ hostname: hostname.toLowerCase() })}
          />
          <label>
            <span>Time zone</span>
            <select value={form.timezone} onChange={e => update({ timezone: e.target.value })}>
              {timezones(form.timezone).map(z => (
                <option key={z}>{z}</option>
              ))}
            </select>
          </label>
          {/* not a label: a click on its text would press the first button */}
          <div className="installer-field">
            <span>Updates</span>
            <Segmented
              label="Updates"
              value={form.channel}
              options={[
                { value: 'stable', label: 'Releases' },
                { value: 'dev', label: 'Every commit' },
              ]}
              onChange={channel => update({ channel })}
            />
          </div>
        </div>
      </Card>

      <Card title="Signing in">
        <div className="installer-fields">
          <TextField
            label="Your user"
            placeholder="e.g. alex"
            value={form.user}
            onChange={user => update({ user: user.toLowerCase() })}
          />
          {form.separatePasswords ? (
            <>
              <PasswordField
                label="Your password"
                value={form.userPassword}
                onChange={userPassword => update({ userPassword })}
              />
              <PasswordField
                label="Root's password"
                value={form.rootPassword}
                onChange={rootPassword => update({ rootPassword })}
              />
              <PasswordField
                label="Web UI password"
                value={form.webPassword}
                onChange={webPassword => update({ webPassword })}
              />
            </>
          ) : (
            <PasswordField
              label="Password"
              hint="for your user, root and the web UI"
              value={form.password}
              onChange={password => update({ password })}
            />
          )}
        </div>
        <label className="check">
          <input
            type="checkbox"
            checked={form.separatePasswords}
            onChange={e =>
              update(
                e.target.checked
                  ? {
                      separatePasswords: true,
                      userPassword: form.password,
                      rootPassword: form.password,
                      webPassword: form.password,
                    }
                  : { separatePasswords: false },
              )
            }
          />
          A password of their own for root and the web UI
        </label>
      </Card>

      {tried && problems.length > 0 && (
        <ul className="error-note installer-problems" role="alert">
          {problems.map(p => (
            <li key={p}>{p}</li>
          ))}
        </ul>
      )}
      {error && (
        <div className="error-note" role="alert">
          {error}
        </div>
      )}
      <button className="primary installer-go" disabled={busy}>
        {busy ? 'Starting…' : disk ? `Install onto /dev/${disk.name}` : 'Install'}
      </button>
    </form>
  )
}

/** How the disk is split, to scale: the system, swap, backups, the VMs. */
function SpaceBar({ form, disk }: { form: Pick<InstallRequest, 'rootGb' | 'swapGb' | 'backupGb'>; disk: Disk }) {
  const total = diskGb(disk)
  const vms = poolGb(form, disk)
  const parts = [
    { name: 'System', gb: form.rootGb, cls: 'root' },
    { name: 'Swap', gb: form.swapGb, cls: 'swap' },
    { name: 'Backups', gb: form.backupGb, cls: 'backups' },
    { name: 'VMs', gb: Math.max(vms, 0), cls: 'vms' },
  ]
  const width = (gb: number) => `${Math.max(0, (Number.isFinite(gb) ? gb : 0) / total) * 100}%`
  return (
    <div className="installer-space">
      <div className="installer-space-bar" aria-hidden>
        {parts.map(p => (
          <div key={p.cls} className={`installer-space-${p.cls}`} style={{ width: width(p.gb) }} />
        ))}
      </div>
      <ul className="installer-space-legend">
        {parts.map(p => (
          <li key={p.cls}>
            <span className={`installer-space-dot installer-space-${p.cls}`} aria-hidden />
            {p.name} <span className="mono">{Number.isFinite(p.gb) ? p.gb : '?'} GB</span>
          </li>
        ))}
      </ul>
      {vms >= 0 && <p className="muted installer-note">VMs: thin volumes, they take space as the guests write.</p>}
    </div>
  )
}

function SizeField({
  label,
  hint,
  value,
  onChange,
}: {
  label: string
  hint?: string
  value: number
  onChange: (v: number) => void
}) {
  return (
    <label>
      <span>{label}, GB</span>
      <input
        type="number"
        inputMode="numeric"
        min={0}
        value={Number.isFinite(value) ? value : ''}
        onChange={e => onChange(e.target.valueAsNumber)}
      />
      {hint && <small className="muted">{hint}</small>}
    </label>
  )
}

function TextField(props: { label: string; placeholder?: string; value: string; onChange: (v: string) => void }) {
  return (
    <label>
      <span>{props.label}</span>
      <input
        value={props.value}
        placeholder={props.placeholder}
        autoCapitalize="off"
        autoCorrect="off"
        spellCheck={false}
        onChange={e => props.onChange(e.target.value)}
      />
    </label>
  )
}

function PasswordField(props: { label: string; hint?: string; value: string; onChange: (v: string) => void }) {
  const [shown, setShown] = useState(false)
  return (
    <label>
      <span>{props.label}</span>
      <span className="installer-password">
        <input
          type={shown ? 'text' : 'password'}
          autoComplete="new-password"
          autoCapitalize="off"
          autoCorrect="off"
          spellCheck={false}
          value={props.value}
          onChange={e => props.onChange(e.target.value)}
        />
        <button type="button" onClick={() => setShown(s => !s)}>
          {shown ? 'Hide' : 'Show'}
        </button>
      </span>
      {props.hint && <small className="muted">{props.hint}</small>}
    </label>
  )
}

export function DiskRow({ disk: d }: { disk: Disk }) {
  const kind = d.transport === 'nvme' ? 'NVMe' : d.rotational ? 'HDD' : d.transport === 'usb' ? 'USB' : 'SSD'
  return (
    <div className="installer-row">
      <div className="installer-row-main">
        <span className="mono">/dev/{d.name}</span>
        <span>{bytes(d.sizeBytes)}</span>
      </div>
      <div className="installer-row-sub muted">
        {[d.model, kind, d.inUse ? 'the installer runs from it' : null].filter(Boolean).join(' · ')}
      </div>
    </div>
  )
}

export function NicRow({ nic: n }: { nic: Nic }) {
  const state = n.wireless ? 'Wi-Fi: cannot be bridged' : n.link ? 'connected' : 'no cable'
  return (
    <div className="installer-row">
      <div className="installer-row-main">
        <span className="mono">{n.name}</span>
        <span className={n.link && !n.wireless ? 'installer-ok' : 'muted'}>{state}</span>
      </div>
      <div className="installer-row-sub muted">
        {[n.addresses.join(', ') || null, n.mac, n.driver].filter(Boolean).join(' · ')}
      </div>
    </div>
  )
}
