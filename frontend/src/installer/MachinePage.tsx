import { bytes } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { BrandMark, Card, ErrorNote, Skeleton } from '@/shared/ui'
import { useState } from 'react'
import { fetchInstall, fetchLocalAccess, fetchMachine } from './api'
import { InstallForm } from './InstallForm'
import { InstallProgress } from './InstallProgress'

/**
 * The installer: the machine, then the settings to install with, or the install running (also after the page was
 * opened again, or on another device). The machine is refreshed, so a cable plugged in shows up.
 */
export function MachinePage() {
  const { data: m, error } = usePoll(fetchMachine, 5000)
  const { data: install, refresh: refreshInstall } = usePoll(fetchInstall, 0)
  // what the user switched to; until then, whatever the install's state says
  const [view, setView] = useState<'form' | 'progress'>()
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

      {!m || !install ? (
        <Skeleton width="14em" />
      ) : view === 'progress' || (view === undefined && install.state !== 'idle') ? (
        <InstallProgress status={install} onRetry={() => setView('form')} />
      ) : (
        <InstallForm machine={m} onStarted={() => refreshInstall().then(() => setView('progress'))} />
      )}
    </div>
  )
}
