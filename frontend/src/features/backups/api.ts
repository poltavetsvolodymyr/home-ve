import { del, get, post } from '@/shared/api/http'
import type { LogEntry } from '@/features/vms/logLevel'

// Mirrors backend/HomeBackend/Features/Backups/BackupModels.cs

export interface VmBackup {
  /** YYYYmmdd-HHMMSS, the time it was taken */
  id: string
  /** ISO date */
  time: string
  /** the compressed file */
  sizeBytes: number
}

/** never = not run since the host booted (a backup) or the backend started (a restore) */
export type JobState = 'never' | 'running' | 'succeeded' | 'failed'

export interface BackupJob {
  state: JobState
  startedAt: string | null
  finishedAt: string | null
  /** for a restore: which backup */
  id: string | null
  /** for a failed restore: what went wrong */
  error: string | null
}

export interface VmBackups {
  /** the backup volume is mounted; without it nothing can be backed up */
  mounted: boolean
  /** in the nightly backups */
  enabled: boolean
  /** newest first */
  backups: VmBackup[]
  backup: BackupJob
  restore: BackupJob
  /** the last backup's and restore's journal lines, newest first */
  log: LogEntry[]
  freeBytes: number | null
  totalBytes: number | null
}

const path = (name: string) => `/api/vms/${encodeURIComponent(name)}/backups`

export const fetchBackups = (name: string) => get<VmBackups>(path(name))
export const backUpNow = (name: string) => post<VmBackups>(path(name))
/** Only onto a stopped VM. */
export const restoreBackup = (name: string, id: string) => post<VmBackups>(`${path(name)}/${id}/restore`)
export const deleteBackup = (name: string, id: string) => del<VmBackups>(`${path(name)}/${id}`)
