import type { ConfirmOptions, Status } from '@/shared/ui'
import type { UpdateState } from './api'

export const updateBadge: Record<UpdateState, { status: Status; label: string }> = {
  never: { status: 'neutral', label: 'Not run since boot' },
  running: { status: 'warning', label: 'Running' },
  succeeded: { status: 'good', label: 'Done' },
  failed: { status: 'critical', label: 'Failed' },
}

/** What an update does: said in the dialog before it starts. */
export const confirmUpdate: ConfirmOptions = {
  title: 'Update home-ve now?',
  message:
    'Runs /opt/home-ve/deploy/update.sh as root: pulls the latest version from GitHub and installs the backend, ' +
    'this web UI, vm-run and the units.\n\n' +
    'The page loses the server for a few seconds while the backend restarts. The VMs keep running; a VM picks up ' +
    'a new vm-run at its next restart.',
  confirmLabel: 'Update',
}

export const confirmOffsite: ConfirmOptions = {
  title: 'Upload the backups now?',
  message:
    "Copies /var/backups/vm and an archive of the host's settings to the offsite store, encrypted on the host. " +
    'Only what changed goes up, at most 5 GB per run (offsite.conf). It also runs every night after the backups.',
  confirmLabel: 'Upload',
}
