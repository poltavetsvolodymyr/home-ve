import type { ConfirmOptions } from '@/shared/ui'
import type { Status } from '@/shared/ui'
import type { VmAction, VmState } from './api'

/** Badge colour and word for a VM state. */
export function stateBadge(state: VmState): [Status, string] {
  switch (state) {
    case 'running':
      return ['good', 'Running']
    case 'starting':
      return ['warning', 'Starting']
    case 'stopping':
      return ['warning', 'Stopping']
    case 'failed':
      return ['critical', 'Failed']
    default:
      return ['neutral', 'Stopped']
  }
}

/**
 * Which buttons make sense now. Power off stays available while a VM is starting or stopping:
 * that's when a guest hangs and has to be cut off.
 */
export function availableActions(state: VmState): VmAction[] {
  switch (state) {
    case 'running':
      return ['shutdown', 'reboot', 'poweroff']
    case 'starting':
    case 'stopping':
      return ['poweroff']
    default:
      return ['start']
  }
}

/** Whether QEMU runs and so there's a screen: also while starting (BIOS) and stopping (a guest that hangs there). */
export const hasScreen = (state: VmState) => state === 'running' || state === 'starting' || state === 'stopping'

export const actionLabels: Record<VmAction, string> = {
  start: 'Start',
  shutdown: 'Shut down',
  reboot: 'Reboot',
  poweroff: 'Power off',
}

/** The question before a button that interrupts a running VM; null when no question is needed. */
export function confirmAction(name: string, action: VmAction): ConfirmOptions | null {
  switch (action) {
    case 'shutdown':
      return {
        title: `Shut down ${name}?`,
        message:
          'The guest gets the power button and turns itself off. One that ignores it (an installer, a boot menu) is powered off after 2 minutes.',
        confirmLabel: 'Shut down',
      }
    case 'reboot':
      return { title: `Reboot ${name}?`, confirmLabel: 'Reboot' }
    case 'poweroff':
      return {
        title: `Power off ${name}?`,
        message: 'Right now, like pulling the plug: unsaved data in the guest is lost.',
        confirmLabel: 'Power off',
        danger: true,
      }
    default:
      return null
  }
}
