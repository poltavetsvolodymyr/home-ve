import type { Status } from '@/shared/ui'
import type { UpdateState } from './api'

export const updateBadge: Record<UpdateState, { status: Status; label: string }> = {
  never: { status: 'neutral', label: 'Not run since boot' },
  running: { status: 'warning', label: 'Running' },
  succeeded: { status: 'good', label: 'Done' },
  failed: { status: 'critical', label: 'Failed' },
}

export const confirmUpdate =
  'Update home-ve now?\n\n' +
  'The host pulls the latest version from GitHub and runs install.sh as root: backend, web UI and VM tools. ' +
  'The page loses the server for a few seconds while the backend restarts. The VMs keep running.'
