import { describe, expect, it } from 'vitest'
import type { BackupJob, VmBackups } from './api'
import { backUpBlocked, restoreBlocked } from './backups'

const idle: BackupJob = { state: 'never', startedAt: null, finishedAt: null, id: null, error: null }
const base: VmBackups = {
  mounted: true,
  enabled: true,
  backups: [],
  backup: idle,
  restore: idle,
  log: [],
  freeBytes: null,
  totalBytes: null,
}

describe('backups', () => {
  it('restores only onto a stopped VM with nothing else running', () => {
    expect(restoreBlocked(base, 'stopped')).toBeNull()
    expect(restoreBlocked(base, 'failed')).toBeNull()
    expect(restoreBlocked(base, 'running')).toMatch(/Shut/)
    expect(restoreBlocked({ ...base, backup: { ...idle, state: 'running' } }, 'stopped')).toMatch(/Wait/)
  })

  it('backs up only onto the mounted volume and not during a restore', () => {
    expect(backUpBlocked(base)).toBeNull()
    expect(backUpBlocked({ ...base, mounted: false })).toMatch(/not mounted/)
    expect(backUpBlocked({ ...base, restore: { ...idle, state: 'running' } })).toMatch(/restore/)
  })
})
