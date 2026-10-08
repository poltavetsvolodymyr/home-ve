import type { ConfirmOptions, Status } from '@/shared/ui'
import type { JobState, VmBackups } from './api'

export const jobBadge: Record<Exclude<JobState, 'never'>, { status: Status; label: string }> = {
  running: { status: 'warning', label: 'Running' },
  succeeded: { status: 'good', label: 'Done' },
  failed: { status: 'critical', label: 'Failed' },
}

/** Something is writing or reading the disk: one job at a time per VM. */
export const busy = (b: VmBackups) => b.backup.state === 'running' || b.restore.state === 'running'

/** Why "Back up now" is off, or null when it can run. */
export function backUpBlocked(b: VmBackups): string | null {
  if (!b.mounted) return 'The backup volume /var/backups/vm is not mounted.'
  if (b.backup.state === 'running') return null // the button says "Backing up…"
  if (b.restore.state === 'running') return 'A restore is running.'
  return null
}

/** Why "Restore" is off, or null when it can run. */
export function restoreBlocked(b: VmBackups, vmState: string): string | null {
  if (vmState !== 'stopped' && vmState !== 'failed') return 'Shut the VM down to restore a backup.'
  if (busy(b)) return 'Wait for the running job to finish.'
  return null
}

export const confirmRestore = (name: string, when: string): ConfirmOptions => ({
  title: `Restore ${name} from ${when}?`,
  message: `Its disk is overwritten with the backup. The disk as it is now is kept as the snapshot ${name}-undo until the next restore.`,
  confirmLabel: 'Restore',
  danger: true,
  typeToConfirm: name,
})
