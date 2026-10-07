import { describe, expect, it } from 'vitest'
import { keysymOf } from './keysyms'

describe('keysymOf', () => {
  it('uses ASCII and Latin-1 as they are', () => {
    expect(keysymOf('a')).toBe(0x61)
    expect(keysymOf(' ')).toBe(0x20)
    expect(keysymOf('ä')).toBe(0xe4)
  })

  it('maps other characters to Unicode keysyms', () => expect(keysymOf('ж')).toBe(0x01000436))
})
