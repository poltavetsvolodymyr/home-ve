import { get, post, put } from '@/shared/api/http'
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

// Mirrors backend/HomeBackend/Features/Update/UpdateChannel.cs

/** stable = the latest release, dev = every commit on main */
export type Channel = 'stable' | 'dev'

export interface ChannelInfo {
  channel: Channel
  /** what runs now: v0.1.0, or v0.1.0-3-gabc1234 for 3 commits after it; null when unknown */
  version: string | null
}

export const fetchChannel = () => get<ChannelInfo>('/api/host/update/channel')
export const saveChannel = (channel: Channel) => put<ChannelInfo>('/api/host/update/channel', { channel })

// Mirrors backend/HomeBackend/Features/Offsite/OffsiteStatus.cs

export interface OffsiteStatus extends UpdateStatus {
  /** /etc/vm-offsite/rclone.conf is there: the offsite store has been set up */
  configured: boolean
}

export const fetchOffsite = () => get<OffsiteStatus>('/api/host/offsite')
export const startOffsite = () => post<OffsiteStatus>('/api/host/offsite')
