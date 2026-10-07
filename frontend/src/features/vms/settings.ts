import type { VmConfig, VmNet, VmSettings } from './api'

// The same limits as backend/HomeBackend/Features/Vms/VmConfigFile.cs; the server checks again.
export const maxCpus = 64
export const minMemoryMb = 128
export const maxNets = 8
const macPattern = /^[0-9A-F]{2}(:[0-9A-F]{2}){5}$/

/** The settings form starts from the VM's current config. */
export const settingsFrom = (c: VmConfig): VmSettings => ({
  cpus: c.cpus,
  memoryMb: c.memoryMb,
  nets: c.nets.map(n => ({ ...n })),
  autostart: c.autostart,
})

/** What's wrong with the form, field by field; empty when it can be saved. */
export function validateSettings(s: VmSettings, hostCpus: number, hostMemoryMb: number): string[] {
  const errors: string[] = []
  if (!Number.isInteger(s.cpus) || s.cpus < 1 || s.cpus > Math.min(maxCpus, hostCpus))
    errors.push(`CPUs: 1–${Math.min(maxCpus, hostCpus)}`)
  if (!Number.isInteger(s.memoryMb) || s.memoryMb < minMemoryMb || s.memoryMb > hostMemoryMb)
    errors.push(`Memory: ${minMemoryMb}–${hostMemoryMb} MiB`)
  if (s.nets.length > maxNets) errors.push(`At most ${maxNets} network cards`)
  s.nets.forEach((n, i) => {
    const mac = n.mac.toUpperCase()
    if (!n.bridge) errors.push(`Card ${i + 1}: pick a bridge`)
    if (!macPattern.test(mac)) errors.push(`Card ${i + 1}: MAC looks like AA:BB:CC:DD:EE:FF`)
    else if (parseInt(mac.slice(0, 2), 16) & 1) errors.push(`Card ${i + 1}: that's a multicast MAC`)
  })
  const macs = s.nets.map(n => n.mac.toUpperCase())
  if (new Set(macs).size !== macs.length) errors.push('Two cards have the same MAC')
  return errors
}

/** A new card's MAC: QEMU's own prefix 52:54:00 and three random bytes, like libvirt does. */
export function randomMac(random: () => number = Math.random): string {
  const byte = () =>
    Math.floor(random() * 256)
      .toString(16)
      .padStart(2, '0')
  return ['52', '54', '00', byte(), byte(), byte()].join(':').toUpperCase()
}

export const newNet = (bridge: string): VmNet => ({ bridge, mac: randomMac() })

/** Whether saving would change anything the VM only picks up on its next start. */
export const needsRestart = (before: VmConfig, after: VmSettings) =>
  before.cpus !== after.cpus ||
  before.memoryMb !== after.memoryMb ||
  JSON.stringify(before.nets.map(n => [n.bridge, n.mac.toUpperCase()])) !==
    JSON.stringify(after.nets.map(n => [n.bridge, n.mac.toUpperCase()]))
