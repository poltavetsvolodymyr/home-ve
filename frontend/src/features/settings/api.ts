import { get, post } from '@/shared/api/http'
import type { LogEntry } from '@/features/vms/logLevel'

// Mirrors backend/HomeBackend/Features/Update/UpdateStatus.cs

/** never = not run since the host booted */
export type UpdateState = 'never' | 'running' | 'succeeded' | 'failed'

export interface UpdateStatus {
  state: UpdateState
  /** ISO dates of the last run; null when it never ran (finishedAt also while running) */
  startedAt: string | null
  finishedAt: string | null
  /** that run's journal lines, newest first */
  log: LogEntry[]
}

export const fetchUpdate = () => get<UpdateStatus>('/api/host/update')
export const startUpdate = () => post<UpdateStatus>('/api/host/update')
