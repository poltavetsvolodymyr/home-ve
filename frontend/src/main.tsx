import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// global styles first: the feature styles imported by the components build on them
import './styles/index.css'
import { App } from './app/App'
import { applyStoredTheme } from './app/theme'

applyStoredTheme()

// An update replaces the build, and the old one's files go with it: a page opened before the update then fails
// to load the parts it loads later (the console). Load the page anew, which brings the new build. Not again
// within 10 s: a build that is really broken would reload forever.
window.addEventListener('vite:preloadError', event => {
  const key = 'reloaded-for-new-build'
  try {
    if (Date.now() - Number(sessionStorage.getItem(key)) < 10_000) return
    sessionStorage.setItem(key, String(Date.now()))
  } catch {
    return // no storage (private mode): no way to tell a second time, so no reload
  }
  event.preventDefault()
  location.reload()
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
