import { describe, expect, it } from 'vitest'
import { availableActions, confirmText, hasScreen, stateBadge } from './vmState'

describe('vm state', () => {
  it('labels every state', () => {
    expect(stateBadge('running')).toEqual(['good', 'Running'])
    expect(stateBadge('failed')).toEqual(['critical', 'Failed'])
    expect(stateBadge('stopped')).toEqual(['neutral', 'Stopped'])
  })

  it('offers start only when a VM is down', () => {
    expect(availableActions('stopped')).toEqual(['start'])
    expect(availableActions('failed')).toEqual(['start'])
    expect(availableActions('running')).toEqual(['shutdown', 'reboot', 'poweroff'])
  })

  it('keeps power off for a VM stuck starting or stopping', () =>
    expect(availableActions('stopping')).toEqual(['poweroff']))

  it('asks before interrupting a VM, not before starting one', () => {
    expect(confirmText('router', 'start')).toBeNull()
    expect(confirmText('router', 'poweroff')).toContain('router')
  })
})

describe('hasScreen', () => {
  it('is there while QEMU runs, stopping included', () => {
    expect(hasScreen('stopping')).toBe(true)
    expect(hasScreen('starting')).toBe(true)
    expect(hasScreen('stopped')).toBe(false)
    expect(hasScreen('failed')).toBe(false)
  })
})
