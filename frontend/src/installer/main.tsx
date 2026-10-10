import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@/styles/index.css'
import { applyStoredTheme } from '@/app/theme'
import { InstallerApp } from './InstallerApp'

applyStoredTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <InstallerApp />
  </StrictMode>,
)
