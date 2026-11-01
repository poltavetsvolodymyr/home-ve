import { describe, expect, it } from 'vitest'
import { functionKeys, panelKeys } from './keysyms'
import { isTerminalReport, keyBytes, typed } from './termKeys'

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

describe('isTerminalReport', () => {
  it("knows the terminal's own answers", () => {
    expect(isTerminalReport('\x1b[27;135R')).toBe(true)
    expect(isTerminalReport('\x1b[?27;135;1R')).toBe(true)
    expect(isTerminalReport('\x1b[0n')).toBe(true)
    expect(isTerminalReport('\x1b[?1;2c')).toBe(true)
    expect(isTerminalReport('\x1b[>0;276;0c')).toBe(true)
    expect(isTerminalReport('\x1b[?2004;1$y')).toBe(true)
    expect(isTerminalReport('\x1b[8;27;41t')).toBe(true)
    expect(isTerminalReport('\x1bP>|xterm.js(6.0.0)\x1b\\')).toBe(true)
    expect(isTerminalReport('\x1b]11;rgb:0000/0000/0000\x07')).toBe(true)
  })

  it('lets through what is typed: text, keys, a pasted escape, the mouse', () => {
    for (const typed of [
      'ls\r',
      'R',
      '\x1b',
      '\x1b[A',
      '\x1b[1;5A',
      '\x1b[15~',
      '\x1bOR',
      '\x1b[<0;10;5M',
      '\x1b[200~R;1R\x1b[201~',
    ])
      expect(isTerminalReport(typed)).toBe(false)
  })
})
