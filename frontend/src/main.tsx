import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// global styles first: the feature styles imported by the components build on them
import './styles/index.css'
import { App } from './app/App'
import { applyStoredTheme } from './app/theme'

applyStoredTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
