import { Server } from 'lucide-react'

/**
 * The app's mark, as in the header: an outline server in a tile, the accent only on its strokes. `large` on the
 * login and setup pages. (The browser tab's icon, public/favicon.svg, is a filled one: outlines vanish that small.)
 */
export function BrandMark({ large = false }: { large?: boolean }) {
  return (
    <span className={large ? 'brand-mark large' : 'brand-mark'} aria-hidden>
      <Server size={large ? 26 : 18} strokeWidth={2} />
    </span>
  )
}
