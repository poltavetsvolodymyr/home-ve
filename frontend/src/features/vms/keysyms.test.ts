import { describe, expect, it } from 'vitest'
import {
  KEY_SHIFT,
  functionKeys,
  keysymOf,
  lineEdit,
  needsShift,
  panelKeyEvents,
  panelKeys,
  textEvents,
} from './keysyms'

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

describe('panel keys', () => {
  it('press and release a key', () =>
    expect(panelKeyEvents(panelKeys.up)).toEqual([
      [0xff52, 'ArrowUp', true],
      [0xff52, 'ArrowUp', false],
    ]))

  it('hold the modifiers around it and let go in reverse', () =>
    expect(panelKeyEvents(panelKeys.del, ['ctrl', 'alt']).map(([sym, , down]) => [sym, down])).toEqual([
      [0xffe3, true],
      [0xffe9, true],
      [0xffff, true],
      [0xffff, false],
      [0xffe9, false],
      [0xffe3, false],
    ]))

  it('F1–F12 run on from 0xffbe', () => expect(functionKeys.map(k => k.keysym).at(-1)).toBe(0xffc9))

  it('text with Ctrl holds Ctrl for the whole of it', () =>
    expect(textEvents('c', ['ctrl']).map(([sym, , down]) => [sym, down])).toEqual([
      [0xffe3, true],
      [0x63, true],
      [0x63, false],
      [0xffe3, false],
    ]))

  it('a capital gets Shift around it', () =>
    expect(textEvents('A').map(([sym]) => sym)).toEqual([KEY_SHIFT, 0x41, 0x41, KEY_SHIFT]))
})

describe('lineEdit', () => {
  it('types what was added at the end', () =>
    expect(lineEdit('ls', 'ls -la')).toEqual({ backspaces: 0, typed: ' -la' }))

  it('erases what was deleted', () => expect(lineEdit('ls -la', 'ls -')).toEqual({ backspaces: 2, typed: '' }))

  it('turns a swapped word into Backspaces and the new word', () =>
    expect(lineEdit('sudo teh', 'sudo the ')).toEqual({ backspaces: 2, typed: 'he ' }))

  it('retypes the line from an edit in the middle', () =>
    expect(lineEdit('cat fiel.txt', 'cat file.txt')).toEqual({ backspaces: 6, typed: 'le.txt' }))

  it('counts characters, not UTF-16 units', () => expect(lineEdit('a😀', 'a')).toEqual({ backspaces: 1, typed: '' }))
})
