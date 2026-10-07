import { describe, expect, it } from 'vitest'
import { keysymOf, needsShift } from './keysyms'

describe('keysymOf', () => {
  it('uses ASCII and Latin-1 as they are', () => {
    expect(keysymOf('a')).toBe(0x61)
    expect(keysymOf(' ')).toBe(0x20)
    expect(keysymOf('ä')).toBe(0xe4)
  })

  it('maps other characters to Unicode keysyms', () => expect(keysymOf('ж')).toBe(0x01000436))
})

describe('needsShift', () => {
  it('is true for capitals and shifted symbols', () => {
    for (const ch of 'AZ!@#$%^&*()_+{}|:"<>?~') expect(needsShift(ch), ch).toBe(true)
  })

  it('is false for the rest', () => {
    for (const ch of "az09-=[]\\;',./` ") expect(needsShift(ch), ch).toBe(false)
  })
})
