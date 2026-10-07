import { UpdateCard } from './UpdateCard'
import './settings.css'

/** Settings of the host itself (the gear in the header). For now: updating home-ve. */
export function SettingsPage() {
  return (
    <div className="page">
      <UpdateCard />
    </div>
  )
}
