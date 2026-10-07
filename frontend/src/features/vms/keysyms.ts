// X11 keysyms for what the console's text box sends. Latin-1 characters are their own keysym;
// anything else is 0x01000000 + its Unicode code point.

export const KEY_ENTER = 0xff0d
export const KEY_BACKSPACE = 0xff08
export const KEY_TAB = 0xff09
export const KEY_ESCAPE = 0xff1b
export const KEY_SHIFT = 0xffe1

/**
 * Whether the character needs Shift on a US keyboard. QEMU's VNC server turns a keysym into a bare
 * scancode and drops the modifier (ui/keymaps.c keysym2scancode), so "R" alone arrives as "r" and "!"
 * as "1": the sender has to hold Shift itself.
 */
export function needsShift(ch: string): boolean {
  return /^[A-Z~!@#$%^&*()_+{}|:"<>?]$/.test(ch)
}

export function keysymOf(ch: string): number {
  const cp = ch.codePointAt(0) ?? 0
  return (cp >= 0x20 && cp <= 0x7e) || (cp >= 0xa0 && cp <= 0xff) ? cp : 0x01000000 + cp
}

/** A key of the on-screen panel: what noVNC sends (X11 keysym + the browser's KeyboardEvent.code). */
export interface PanelKey {
  label: string
  keysym: number
  code: string
}

const key = (label: string, keysym: number, code: string): PanelKey => ({ label, keysym, code })

export const panelKeys = {
  esc: key('Esc', 0xff1b, 'Escape'),
  tab: key('Tab', 0xff09, 'Tab'),
  enter: key('Enter', KEY_ENTER, 'Enter'),
  space: key('Space', 0x20, 'Space'),
  backspace: key('⌫', 0xff08, 'Backspace'),
  del: key('Del', 0xffff, 'Delete'),
  home: key('Home', 0xff50, 'Home'),
  end: key('End', 0xff57, 'End'),
  pgUp: key('PgUp', 0xff55, 'PageUp'),
  pgDn: key('PgDn', 0xff56, 'PageDown'),
  up: key('↑', 0xff52, 'ArrowUp'),
  down: key('↓', 0xff54, 'ArrowDown'),
  left: key('←', 0xff51, 'ArrowLeft'),
  right: key('→', 0xff53, 'ArrowRight'),
}

/** F1–F12: keysyms 0xffbe… in a row. */
export const functionKeys: PanelKey[] = Array.from({ length: 12 }, (_, i) => key(`F${i + 1}`, 0xffbe + i, `F${i + 1}`))

export type Modifier = 'ctrl' | 'alt' | 'shift'

export const modifierKeys: Record<Modifier, PanelKey> = {
  ctrl: key('Ctrl', 0xffe3, 'ControlLeft'),
  alt: key('Alt', 0xffe9, 'AltLeft'),
  shift: key('Shift', KEY_SHIFT, 'ShiftLeft'),
}

/** One key event for noVNC's sendKey: keysym, code (null: by keysym only), down. */
export type KeyEvent = [keysym: number, code: string | null, down: boolean]

/** Holds the modifiers around some presses: all down, the presses, all up again in reverse. */
function withModifiers(mods: Modifier[], presses: KeyEvent[]): KeyEvent[] {
  const held = mods.map(m => modifierKeys[m])
  return [
    ...held.map((k): KeyEvent => [k.keysym, k.code, true]),
    ...presses,
    ...held.reverse().map((k): KeyEvent => [k.keysym, k.code, false]),
  ]
}

/** A panel key with the modifiers that are switched on, e.g. Ctrl + ↑. */
export const panelKeyEvents = (k: PanelKey, mods: Modifier[] = []): KeyEvent[] =>
  withModifiers(mods, [
    [k.keysym, k.code, true],
    [k.keysym, k.code, false],
  ])

/** Text, key by key, holding Shift where a US keyboard needs it (unless Shift is on anyway). */
export function textEvents(text: string, mods: Modifier[] = []): KeyEvent[] {
  const shiftOn = mods.includes('shift')
  const presses = [...text].flatMap((ch): KeyEvent[] => {
    const sym = keysymOf(ch)
    const press: KeyEvent[] = [
      [sym, null, true],
      [sym, null, false],
    ]
    return needsShift(ch) && !shiftOn
      ? [[KEY_SHIFT, 'ShiftLeft', true], ...press, [KEY_SHIFT, 'ShiftLeft', false]]
      : press
  })
  return withModifiers(mods, presses)
}
