import { describe, expect, it } from 'vitest'
import { updateBadge } from './update'

describe('updateBadge', () => {
  it('never shows a failure as anything but critical', () => expect(updateBadge.failed.status).toBe('critical'))
  it('marks a running update as a warning, not as done', () => expect(updateBadge.running.status).toBe('warning'))
})
