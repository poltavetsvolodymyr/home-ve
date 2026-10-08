import { OffsiteCard } from './OffsiteCard'
import { UpdateCard } from './UpdateCard'
import './settings.css'

/** Settings of the host itself (the gear in the header): updating home-ve and the offsite backup. */
export function SettingsPage() {
  return (
    <div className="page">
      <UpdateCard />
      <OffsiteCard />
    </div>
  )
}
