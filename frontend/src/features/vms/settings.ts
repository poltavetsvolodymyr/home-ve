import type { VmConfig, VmCreate, VmNet, VmSettings } from './api'

// The same limits as backend/HomeBackend/Features/Vms/VmConfigFile.cs; the server checks again.
export const maxCpus = 64
export const minMemoryMb = 128
export const maxNets = 8
export const maxDiskSizeGb = 4096
const namePattern = /^[a-z][a-z0-9-]{0,11}$/
/** taken by the host's own volumes, and "new" by the New VM page's address */
export const reservedNames = ['root', 'swap', 'data', 'new']
const macPattern = /^[0-9A-F]{2}(:[0-9A-F]{2}){5}$/

/** The settings form starts from the VM's current config. */
export const settingsFrom = (c: VmConfig): VmSettings => ({
  cpus: c.cpus,
  memoryMb: c.memoryMb,
  nets: c.nets.map(n => ({ ...n })),
  autostart: c.autostart,
  cdrom: c.cdrom,
  backup: c.backup,
})

/** A new VM's starting point: small, one card on the first bridge, started for installing. */
export const newVm = (bridge: string): VmCreate => ({
  name: '',
  cpus: 2,
  memoryMb: 2048,
  diskSizeGb: 20,
  nets: [newNet(bridge)],
  autostart: false,
  cdrom: null,
  backup: true,
  start: true,
})

/** The settings checks plus the name and the disk size. */
export function validateNewVm(vm: VmCreate, existing: string[], hostCpus: number, hostMemoryMb: number): string[] {
  const errors: string[] = []
  if (!namePattern.test(vm.name)) errors.push('Name: 1–12 lowercase letters, digits or dashes, starting with a letter')
  else if (reservedNames.includes(vm.name)) errors.push(`Name: "${vm.name}" is reserved`)
  else if (existing.includes(vm.name)) errors.push(`Name: there already is a VM ${vm.name}`)
  if (!Number.isInteger(vm.diskSizeGb) || vm.diskSizeGb < 1 || vm.diskSizeGb > maxDiskSizeGb)
    errors.push(`Disk: 1–${maxDiskSizeGb} GiB`)
  return [...errors, ...validateSettings(vm, hostCpus, hostMemoryMb)]
}

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
  (before.cdrom ?? null) !== (after.cdrom ?? null) ||
  before.memoryMb !== after.memoryMb ||
  JSON.stringify(before.nets.map(n => [n.bridge, n.mac.toUpperCase()])) !==
    JSON.stringify(after.nets.map(n => [n.bridge, n.mac.toUpperCase()]))
