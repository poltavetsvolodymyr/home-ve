import { describe, expect, it } from 'vitest'
import type { CpuTemperature } from './api'
import { temperatureTile } from './cpuTemperature'

const reading = (
  celsius: number | null,
  criticalCelsius: number | null,
  problem: string | null = null,
): CpuTemperature => ({
  celsius,
  criticalCelsius,
  source: 'local',
  problem,
})

describe('temperatureTile', () => {
  it('shows the reading against the chip limit', () =>
    expect(temperatureTile(reading(47.4, 100))).toEqual({ value: '47 °C', meter: 47.4, sub: ['limit 100 °C'] }))

  it('scales to 100 °C when the chip has no limit (AMD)', () =>
    expect(temperatureTile(reading(80, null))).toEqual({ value: '80 °C', meter: 80, sub: ['bar up to 100 °C'] }))

  it('explains a missing reading', () => {
    expect(temperatureTile(reading(null, null, 'no CPU sensor'))).toEqual({ value: '—', sub: ['no CPU sensor'] })
    expect(temperatureTile(reading(null, null))).toEqual({ value: '—', sub: ['reading…'] })
  })
})
