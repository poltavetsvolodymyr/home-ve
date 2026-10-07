import type { CpuTemperature } from './api'

/** Without a limit from the chip, the bar is scaled to 100 °C: amber from 75 °C, red from 90 °C. */
const defaultLimit = 100

export interface TemperatureTile {
  value: string
  /** 0–100, percent of the limit; the tile turns amber at 75, red at 90 */
  meter?: number
  /** one entry per line under the value */
  sub: string[]
}

/** What the CPU temperature tile shows: the reading against the chip's limit, or why there is none. */
export function temperatureTile(t: CpuTemperature): TemperatureTile {
  if (t.celsius === null) return { value: '—', sub: [t.problem ?? 'reading…'] }

  return {
    value: `${Math.round(t.celsius)} °C`,
    meter: (t.celsius / (t.criticalCelsius ?? defaultLimit)) * 100,
    sub: [t.criticalCelsius ? `limit ${Math.round(t.criticalCelsius)} °C` : `bar up to ${defaultLimit} °C`],
  }
}
