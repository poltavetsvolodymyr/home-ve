import type { ReactNode } from 'react'

export interface ConfirmOptions {
  title: string
  /** What happens; plain text keeps its line breaks. */
  message?: ReactNode
  /** The confirming button, e.g. "Power off". */
  confirmLabel?: string
  /** A red confirming button: the action loses data or cuts something off. */
  danger?: boolean
  /** The confirming button stays off until this (the VM's name) is typed: for what can't be undone. */
  typeToConfirm?: string
}

export interface ConfirmRequest extends ConfirmOptions {
  resolve: (ok: boolean) => void
}

// one question at a time, shown by the single <ConfirmHost /> in the app
let current: ConfirmRequest | null = null
const listeners = new Set<() => void>()
const emit = () => listeners.forEach(l => l())

/**
 * Asks in our own dialog instead of the browser's confirm()/prompt(): true for the confirming button,
 * false for Cancel, Esc or a tap outside. `if (!(await confirm({ title: 'Delete x?' }))) return`
 */
export function confirm(options: ConfirmOptions): Promise<boolean> {
  current?.resolve(false)
  return new Promise(resolve => {
    current = { ...options, resolve }
    emit()
  })
}

/** The open dialog's answer; ConfirmHost calls it. */
export function answer(ok: boolean) {
  const r = current
  current = null
  emit()
  r?.resolve(ok)
}

/** For ConfirmHost: the question being asked (null: none) and change notifications. */
export const confirmStore = {
  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
  current: () => current,
}
