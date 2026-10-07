// X11 keysyms for what the console's text box sends. Latin-1 characters are their own keysym;
// anything else is 0x01000000 + its Unicode code point.

export const KEY_ENTER = 0xff0d
export const KEY_BACKSPACE = 0xff08
export const KEY_TAB = 0xff09
export const KEY_ESCAPE = 0xff1b

export function keysymOf(ch: string): number {
  const cp = ch.codePointAt(0) ?? 0
  return (cp >= 0x20 && cp <= 0x7e) || (cp >= 0xa0 && cp <= 0xff) ? cp : 0x01000000 + cp
}
