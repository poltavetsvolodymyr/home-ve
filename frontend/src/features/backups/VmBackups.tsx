import { Archive, RotateCcw, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { levelClass, levelLabel } from '@/features/vms/logLevel'
import type { Vm } from '@/features/vms/api'
import { bytes, dateTime } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { Badge, Card, confirm, ErrorNote, Skeleton } from '@/shared/ui'
import { backUpNow, deleteBackup, fetchBackups, restoreBackup, type BackupJob } from './api'
import { backUpBlocked, busy, confirmRestore, jobBadge, restoreBlocked } from './backups'
import '@/features/vms/logs.css'
import './backups.css'

/**
 * A VM's backups in /var/backups/vm/<vm>: taken without stopping it (a thin snapshot, compressed with zstd),
 * every night and on "Back up now". Restoring writes one back onto the stopped VM's disk.
 */
export function VmBackups({ vm }: { vm: Vm }) {
  const { data, error, refresh } = usePoll(() => fetchBackups(vm.name), 3000, vm.name)
  const [working, setWorking] = useState(false)
  const [actionError, setActionError] = useState<string>()

  const run = async (action: () => Promise<unknown>) => {
    setWorking(true)
    setActionError(undefined)
    try {
      await action()
      await refresh()
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
    } finally {
      setWorking(false)
    }
  }

  const restore = async (id: string, time: string) => {
    if (await confirm(confirmRestore(vm.name, dateTime(time)))) void run(() => restoreBackup(vm.name, id))
  }

  const remove = async (id: string, time: string) => {
    const ok = await confirm({
      title: `Delete this backup of ${vm.name}?`,
      message: `The one from ${dateTime(time)}.`,
      confirmLabel: 'Delete',
      danger: true,
    })
    if (ok) void run(() => deleteBackup(vm.name, id))
  }

  const backingUp = data?.backup.state === 'running'
  const blocked = data && backUpBlocked(data)
  const noRestore = data && restoreBlocked(data, vm.state)

  return (
    <>
      <Card
        title="Backups"
        actions={
          <button
            className="primary"
            disabled={!data || working || backingUp || blocked != null}
            onClick={() => run(() => backUpNow(vm.name))}
          >
            <Archive size={15} aria-hidden /> {backingUp ? 'Backing up…' : 'Back up now'}
          </button>
        }
      >
        <p className="muted backups-hint">
          {data?.enabled === false
            ? 'Not in the nightly backups (Settings).'
            : 'Backed up every night at about 3:30, without stopping the VM. Kept: the newest of each of the last 7 days, then of 4 more weeks.'}
          {data?.freeBytes != null &&
            data.totalBytes != null &&
            ` Backup volume: ${bytes(data.freeBytes)} free of ${bytes(data.totalBytes)}.`}
        </p>
        <ErrorNote error={error} />
        <ErrorNote error={actionError} />
        {data && !data.mounted && <div className="error-note">{blocked}</div>}

        {data && (
          <div className="backups-jobs">
            <Job label="Last backup" job={data.backup} />
            <Job label="Last restore" job={data.restore} />
          </div>
        )}

        {!data && !error && <Skeleton width="12em" />}
        {data && data.backups.length === 0 && <p className="muted">No backups yet.</p>}
        {data?.backups.map(b => (
          <div key={b.id} className="backup-row">
            <div className="backup-main">
              <span>{dateTime(b.time)}</span>
              <span className="muted">{bytes(b.sizeBytes)}</span>
            </div>
            <div className="backup-buttons">
              <button
                disabled={working || noRestore != null}
                title={noRestore ?? 'Restore this backup'}
                onClick={() => restore(b.id, b.time)}
              >
                <RotateCcw size={15} aria-hidden /> Restore
              </button>
              <button
                className="icon-button"
                disabled={working || (data.restore.state === 'running' && data.restore.id === b.id)}
                aria-label={`Delete the backup of ${dateTime(b.time)}`}
                title="Delete"
                onClick={() => remove(b.id, b.time)}
              >
                <Trash2 size={15} aria-hidden />
              </button>
            </div>
          </div>
        ))}
        {data && data.backups.length > 0 && noRestore && !busy(data) && <p className="muted">{noRestore}</p>}
      </Card>

      {data && data.log.length > 0 && (
        <Card title="Log" className="fit-screen">
          <div className="log">
            {data.log.map((l, i) => (
              <div key={i} className={`log-line ${levelClass(l.priority)}`}>
                <span className="log-time">{dateTime(l.time)}</span>
                <span className="log-level">{levelLabel(l.priority)}</span>
                <span className="log-src">{l.source}</span>
                <span className="log-msg">{l.message}</span>
              </div>
            ))}
          </div>
        </Card>
      )}
    </>
  )
}

function Job({ label, job }: { label: string; job: BackupJob }) {
  if (job.state === 'never') return null
  const badge = jobBadge[job.state]
  return (
    <div className="backups-job">
      <span>{label}</span>
      <Badge status={badge.status}>{badge.label}</Badge>
      {job.startedAt && <span className="muted">{dateTime(job.startedAt)}</span>}
      {job.error && <span className="error-text">{job.error}</span>}
    </div>
  )
}
