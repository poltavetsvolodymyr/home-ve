import { describe, expect, it } from 'vitest'
import { functionKeys, panelKeys } from './keysyms'
import { keyBytes, typed } from './termKeys'

describe('keyBytes', () => {
  it('sends what a terminal sends for the panel keys', () => {
    expect(keyBytes(panelKeys.up)).toBe('\x1b[A')
    expect(keyBytes(panelKeys.left)).toBe('\x1b[D')
    expect(keyBytes(panelKeys.home)).toBe('\x1b[H')
    expect(keyBytes(panelKeys.pgDn)).toBe('\x1b[6~')
    expect(keyBytes(panelKeys.del)).toBe('\x1b[3~')
    expect(keyBytes(panelKeys.enter)).toBe('\r')
    expect(keyBytes(panelKeys.backspace)).toBe('\x7f')
    expect(keyBytes(panelKeys.esc)).toBe('\x1b')
    expect(keyBytes(functionKeys[0])).toBe('\x1bOP')
    expect(keyBytes(functionKeys[4])).toBe('\x1b[15~')
    expect(keyBytes(functionKeys[11])).toBe('\x1b[24~')
  })

  it('adds the modifiers the way xterm does', () => {
    expect(keyBytes(panelKeys.up, ['ctrl'])).toBe('\x1b[1;5A')
    expect(keyBytes(panelKeys.right, ['shift', 'alt'])).toBe('\x1b[1;4C')
    expect(keyBytes(panelKeys.pgUp, ['ctrl'])).toBe('\x1b[5;5~')
    expect(keyBytes(panelKeys.tab, ['shift'])).toBe('\x1b[Z')
    expect(keyBytes(panelKeys.space, ['ctrl'])).toBe('\0')
  })
})

describe('typed', () => {
  it('makes control characters with Ctrl', () => {
    expect(typed('c', ['ctrl'])).toBe('\x03')
    expect(typed('D', ['ctrl'])).toBe('\x04')
    expect(typed('[', ['ctrl'])).toBe('\x1b')
  })

  it('puts Esc before each character with Alt, and Shift makes capitals', () => {
    expect(typed('b', ['alt'])).toBe('\x1bb')
    expect(typed('ab', ['shift'])).toBe('AB')
  })

  it('leaves text without modifiers as it is', () => expect(typed('ls -la')).toBe('ls -la'))
})
