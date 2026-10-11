import { bytes } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { BrandMark, Card, ErrorNote, Skeleton } from '@/shared/ui'
import { fetchLocalAccess, fetchMachine, type Disk, type Nic } from './api'

/** What the installer found: the machine, its disks and network cards. Refreshed, so a cable plugged in shows up. */
export function MachinePage() {
  const { data: m, error } = usePoll(fetchMachine, 5000)
  // only the machine's own screen gets an answer; the address may come later (a cable plugged in)
  const { data: local } = usePoll(fetchLocalAccess, 5000)

  return (
    <div className="installer">
      <header className="installer-head">
        <BrandMark />
        <div>
          <h1>Install home-ve</h1>
          <p className="muted">Debian with home-ve on top: a VM host run from the browser.</p>
        </div>
      </header>
      <ErrorNote error={error} />

      {local && (
        <Card title="From a phone or another computer" className="installer-remote">
          {local.urls.length ? (
            <p>
              Open <span className="mono">{local.urls.join('  or  ')}</span> and enter the code{' '}
              <span className="mono installer-code">{local.code}</span>
            </p>
          ) : (
            <p className="muted">No network yet: plug a cable in, the address shows up here.</p>
          )}
        </Card>
      )}

      <Card title="This machine">
        <dl className="kv">
          <dt>Firmware</dt>
          <dd>{m ? m.uefi ? 'UEFI' : 'BIOS (legacy)' : <Skeleton width="4em" />}</dd>
          <dt>CPU</dt>
          <dd>{m ? `${m.cpu ?? 'unknown'} · ${m.cpus} threads` : <Skeleton width="12em" />}</dd>
          <dt>Memory</dt>
          <dd>{m ? bytes(m.memoryBytes) : <Skeleton width="4em" />}</dd>
        </dl>
      </Card>

      <Card title="Disks">
        {!m && <Skeleton width="14em" />}
        {m?.disks.length === 0 && <p className="muted">No disks found.</p>}
        <ul className="installer-list">
          {m?.disks.map(d => (
            <DiskRow key={d.name} disk={d} />
          ))}
        </ul>
      </Card>

      <Card title="Network cards">
        {!m && <Skeleton width="14em" />}
        {m?.nics.length === 0 && <p className="muted">No network cards found.</p>}
        <ul className="installer-list">
          {m?.nics.map(n => (
            <NicRow key={n.name} nic={n} />
          ))}
        </ul>
      </Card>

      <p className="muted installer-next">Choosing the disk and the settings comes next.</p>
    </div>
  )
}

function DiskRow({ disk: d }: { disk: Disk }) {
  const kind = d.transport === 'nvme' ? 'NVMe' : d.rotational ? 'HDD' : d.transport === 'usb' ? 'USB' : 'SSD'
  return (
    <li className={d.inUse ? 'muted' : undefined}>
      <div className="installer-row-main">
        <span className="mono">/dev/{d.name}</span>
        <span>{bytes(d.sizeBytes)}</span>
      </div>
      <div className="installer-row-sub muted">
        {[d.model, kind, d.inUse ? 'the installer runs from it' : null].filter(Boolean).join(' · ')}
      </div>
    </li>
  )
}

function NicRow({ nic: n }: { nic: Nic }) {
  const state = n.wireless ? 'Wi-Fi: cannot be bridged' : n.link ? 'connected' : 'no cable'
  return (
    <li>
      <div className="installer-row-main">
        <span className="mono">{n.name}</span>
        <span className={n.link && !n.wireless ? 'installer-ok' : 'muted'}>{state}</span>
      </div>
      <div className="installer-row-sub muted">
        {[n.addresses.join(', ') || null, n.mac, n.driver].filter(Boolean).join(' · ')}
      </div>
    </li>
  )
}
