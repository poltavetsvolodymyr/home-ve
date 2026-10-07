import { Moon, Sun } from 'lucide-react'
import { useTheme } from './theme'

/** Sun in the dark theme, moon in the light one: the icon shows what a click switches to. */
export function ThemeToggle() {
  const [theme, toggleTheme] = useTheme()
  const dark = theme === 'dark'
  const Icon = dark ? Sun : Moon

  return (
    <button
      className="icon-button"
      onClick={toggleTheme}
      title={dark ? 'Light theme' : 'Dark theme'}
      aria-label={dark ? 'Switch to light theme' : 'Switch to dark theme'}
    >
      <Icon size={18} aria-hidden />
    </button>
  )
}
