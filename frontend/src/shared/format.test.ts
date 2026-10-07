import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { bytes, clock, dateTime, duration, percent, rate, since, until } from './format'

describe('bytes', () => {
  it.each([
    [0, '0 B'],
    [999, '999 B'],
    [1536, '1.5 KB'],
    [1395864371, '1.3 GB'],
    [150 * 1024 ** 2, '150 MB'],
  ])('%d → %s', (n, expected) => expect(bytes(n)).toBe(expected))

  it('switches units instead of showing 1,000', () => expect(bytes(1023 * 1024)).toBe('1 MB'))
})

describe('rate', () => {
  it.each([
    [0, '0 bit/s'],
    [1337, '10.7 kbit/s'],
    [15250, '122 kbit/s'],
    [99.94e6 / 8, '99.9 Mbit/s'],
    [999.4e6 / 8, '999 Mbit/s'],
  ])('%d B/s → %s', (bytesPerSecond, expected) => expect(rate(bytesPerSecond)).toBe(expected))

  it('switches units instead of showing 1,000', () => expect(rate(999.9e6 / 8)).toBe('1 Gbit/s'))
})

describe('duration', () => {
  it.each([
    [42, '0m'],
    [3 * 60, '3m'],
    [3 * 3600 + 5 * 60, '3h 5m'],
    [12 * 86400 + 7 * 3600 + 59, '12d 7h'],
  ])('%d s → %s', (seconds, expected) => expect(duration(seconds)).toBe(expected))
})

describe('relative times', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-10-04T10:00:00Z'))
  })
  afterEach(() => vi.useRealTimers())

  it('since', () => {
    expect(since('2026-10-04T08:30:00Z')).toBe('1h 30m')
    expect(since(null)).toBe('—')
  })

  it('until', () => {
    expect(until('2026-10-04T19:00:00Z')).toBe('in 9h 0m')
    expect(until('2026-10-04T09:59:00Z')).toBe('expired')
    expect(until(null)).toBe('never')
  })
})

describe('clock and dateTime', () => {
  const local = new Date(2026, 9, 4, 14, 5, 9).getTime() // 4 Oct 14:05:09 in the local time zone

  it('24-hour time', () => expect(clock(local)).toBe('14:05:09'))
  it('day/month and time', () => expect(dateTime(local)).toBe('04/10, 14:05:09'))
})

it('percent rounds to whole numbers', () => expect(percent(1.6)).toBe('2%'))
