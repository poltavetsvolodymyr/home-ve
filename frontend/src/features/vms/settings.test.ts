import { describe, expect, it } from 'vitest'
import type { VmConfig } from './api'
import { needsRestart, randomMac, settingsFrom, validateSettings } from './settings'

const router: VmConfig = {
  name: 'router',
  cpus: 2,
  memoryMb: 2048,
  disk: '/dev/home/router',
  nets: [
    { bridge: 'br-lan', mac: 'BC:24:11:14:4D:CD' },
    { bridge: 'br-wan', mac: 'BC:24:11:C7:E4:7B' },
  ],
  autostart: true,
}

describe('vm settings', () => {
  it('accepts the current config', () => expect(validateSettings(settingsFrom(router), 16, 30000)).toEqual([]))

  it('keeps CPUs and memory within the host', () => {
    const errors = validateSettings({ ...settingsFrom(router), cpus: 32, memoryMb: 64 }, 16, 30000)
    expect(errors).toEqual(['CPUs: 1–16', 'Memory: 128–30000 MiB'])
  })

  it('checks every card', () => {
    const s = settingsFrom(router)
    s.nets[0].mac = 'nope'
    s.nets[1] = { bridge: '', mac: '01:00:5E:00:00:01' }
    expect(validateSettings(s, 16, 30000)).toEqual([
      'Card 1: MAC looks like AA:BB:CC:DD:EE:FF',
      'Card 2: pick a bridge',
      "Card 2: that's a multicast MAC",
    ])
  })

  it('refuses one MAC on two cards, whatever the case', () => {
    const s = settingsFrom(router)
    s.nets[1].mac = 'bc:24:11:14:4d:cd'
    expect(validateSettings(s, 16, 30000)).toEqual(['Two cards have the same MAC'])
  })

  it('makes QEMU-style MACs', () => {
    expect(randomMac(() => 0.5)).toBe('52:54:00:80:80:80')
    expect(randomMac()).toMatch(/^52:54:00(:[0-9A-F]{2}){3}$/)
  })

  it('knows that autostart needs no restart, hardware does', () => {
    expect(needsRestart(router, { ...settingsFrom(router), autostart: false })).toBe(false)
    expect(needsRestart(router, { ...settingsFrom(router), cpus: 4 })).toBe(true)
  })
})
