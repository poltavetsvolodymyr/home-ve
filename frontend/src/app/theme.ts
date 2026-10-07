import { useState } from 'react'

export type Theme = 'dark' | 'light'

const storageKey = 'theme'

/** Dark unless the user picked light; the choice is kept in this browser only. */
function storedTheme(): Theme {
  try {
    return localStorage.getItem(storageKey) === 'light' ? 'light' : 'dark'
  } catch {
    return 'dark'
  }
}

/** The theme is `<html data-theme>`; the colours for each are in styles/tokens.css. */
function applyTheme(theme: Theme) {
  const root = document.documentElement
  root.dataset.theme = theme
  // Safari and Chrome tint their own bars with this
  const page = getComputedStyle(root).getPropertyValue('--page').trim()
  if (page) document.querySelector('meta[name="theme-color"]')?.setAttribute('content', page)
}

/** Called before the first render, so a light-theme user doesn't see a dark flash. */
export function applyStoredTheme() {
  applyTheme(storedTheme())
}

export function useTheme(): [Theme, () => void] {
  const [theme, setTheme] = useState(storedTheme)
  const toggle = () => {
    const next: Theme = theme === 'dark' ? 'light' : 'dark'
    applyTheme(next)
    try {
      localStorage.setItem(storageKey, next)
    } catch {
      /* private mode: just don't remember */
    }
    setTheme(next)
  }
  return [theme, toggle]
}
