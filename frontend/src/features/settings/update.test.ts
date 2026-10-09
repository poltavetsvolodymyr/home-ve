import { describe, expect, it } from 'vitest'
import { releaseOf, updateBadge } from './update'

describe('updateBadge', () => {
  it('never shows a failure as anything but critical', () => expect(updateBadge.failed.status).toBe('critical'))
  it('marks a running update as a warning, not as done', () => expect(updateBadge.running.status).toBe('warning'))
})

describe('releaseOf', () => {
  it('shows a release as it is', () => expect(releaseOf('v0.1.0')).toBe('v0.1.0'))
  it('says nothing between releases', () => {
    expect(releaseOf('v0.1.0-3-gabc1234')).toBe('')
    expect(releaseOf('a205332')).toBe('')
    expect(releaseOf(null)).toBe('')
  })
})
