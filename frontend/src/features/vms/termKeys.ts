import type { Modifier, PanelKey } from './keysyms'

const ESC = '\x1b'

/** Keys sent as Esc [ <letter>: arrows, Home, End. */
const csiKeys: Record<string, string> = {
  ArrowUp: 'A',
  ArrowDown: 'B',
  ArrowRight: 'C',
  ArrowLeft: 'D',
  Home: 'H',
  End: 'F',
}
/** Keys sent as Esc [ <number> ~. */
const tildeKeys: Record<string, number> = {
  Delete: 3,
  PageUp: 5,
  PageDown: 6,
  F5: 15,
  F6: 17,
  F7: 18,
  F8: 19,
  F9: 20,
  F10: 21,
  F11: 23,
  F12: 24,
}
/** F1–F4: Esc O <letter>. */
const ss3Keys: Record<string, string> = { F1: 'P', F2: 'Q', F3: 'R', F4: 'S' }
const plainKeys: Record<string, string> = { Escape: ESC, Tab: '\t', Enter: '\r', Space: ' ', Backspace: '\x7f' }

/** xterm's way of adding modifiers to a key's sequence: 1, plus 1 for Shift, 2 for Alt, 4 for Ctrl. */
const modifierParam = (mods: Modifier[]) =>
  1 + (mods.includes('shift') ? 1 : 0) + (mods.includes('alt') ? 2 : 0) + (mods.includes('ctrl') ? 4 : 0)

/**
 * A key of the on-screen panel as the bytes a terminal sends for it (what xterm, and so most programs, expect):
 * ↑ is Esc [ A, Ctrl+↑ is Esc [ 1 ; 5 A, F5 is Esc [ 1 5 ~.
 */
export function keyBytes(k: PanelKey, mods: Modifier[] = []): string {
  const m = modifierParam(mods)
  const letter = csiKeys[k.code]
  if (letter) return m > 1 ? `${ESC}[1;${m}${letter}` : `${ESC}[${letter}`
  const ss3 = ss3Keys[k.code]
  if (ss3) return m > 1 ? `${ESC}[1;${m}${ss3}` : `${ESC}O${ss3}`
  const n = tildeKeys[k.code]
  if (n) return m > 1 ? `${ESC}[${n};${m}~` : `${ESC}[${n}~`
  if (k.code === 'Tab' && mods.includes('shift')) return `${ESC}[Z`
  return typed(plainKeys[k.code] ?? '', mods)
}

/** Ctrl with a character: @, A–Z, [ \ ] ^ _ become 0–31 (Ctrl+C is 3), Space is 0, ? is Del. Others stay. */
function control(ch: string): string {
  if (ch === ' ') return '\0'
  if (ch === '?') return '\x7f'
  const c = ch.toUpperCase().charCodeAt(0)
  return c >= 0x40 && c <= 0x5f ? String.fromCharCode(c - 0x40) : ch
}

/** Typed text with the panel's modifiers on: Shift makes capitals, Ctrl control characters, Alt puts Esc before each. */
export function typed(text: string, mods: Modifier[] = []): string {
  let chars = [...(mods.includes('shift') ? text.toUpperCase() : text)]
  if (mods.includes('ctrl')) chars = chars.map(control)
  if (mods.includes('alt')) chars = chars.map(ch => ESC + ch)
  return chars.join('')
}

/**
 * Whether what xterm.js sends is its own answer to a program's question, not something typed: where the cursor is
 * (Esc [ row ; col R, also Esc [ ? row ; col ; page R), its status (Esc [ 0 n), what terminal it is (Esc [ ? … c,
 * Esc [ > … c, Esc P … Esc \), the state of a mode (Esc [ ? … $ y), its window (Esc [ … t), its colours (Esc ] … BEL).
 * Ctrl or Shift+F3 from a real keyboard looks like a cursor report (Esc [ 1 ; 5 R) and goes too; the panel's doesn't.
 */
export function isTerminalReport(data: string): boolean {
  // escape sequences are what it matches
  // eslint-disable-next-line no-control-regex
  return /^\x1b(\[\??\d+;\d+(;\d+)?R|\[0n|\[[?>][\d;]*c|\[\??[\d;]*\$y|\[[\d;]+t|P[\s\S]*\x1b\\|\][\s\S]*(\x07|\x1b\\))$/.test(
    data,
  )
}
