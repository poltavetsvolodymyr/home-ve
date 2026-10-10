import { del, get, post, put } from '@/shared/api/http'
import type { LogEntry } from './logLevel'

// Mirrors backend/HomeBackend/Features/Vms/{VmConfig,VmState,VmsFeature}.cs

export interface VmNet {
  /** host bridge the card is plugged into, e.g. br-lan */
  bridge: string
  /** AA:BB:CC:DD:EE:FF */
  mac: string
}

export interface VmConfig {
  name: string
  cpus: number
  /** MiB */
  memoryMb: number
  /** block device of the system disk, e.g. /dev/home/router */
  disk: string
  nets: VmNet[]
  /** started at boot */
  autostart: boolean
  /** GiB the disk gets when vm-run creates it at the first start; null for disks made by hand */
  diskSizeGb: number | null
  /** ISO file in the CD drive; null when empty */
  cdrom: string | null
  /** in the nightly backups */
  backup: boolean
}

/** An address the guest has, e.g. 192.168.178.119/24 on br0. */
export interface GuestAddress {
  /** the guest's name for the interface */
  interface: string
  address: string
  prefix: number
}

export type VmState = 'running' | 'starting' | 'stopping' | 'stopped' | 'failed'

export interface Vm {
  name: string
  state: VmState
  /** ISO date it was started; null when not running */
  since: string | null
  /** share of the VM's own CPUs in use, 0–100; null when not running or not measured yet */
  cpuPercent: number | null
  /** RAM the VM occupies on the host, bytes */
  memoryBytes: number | null
  config: VmConfig
  /** the guest's addresses, IPv4 first, as its guest agent tells them; null without an agent or when not running */
  addresses: GuestAddress[] | null
}

/** shutdown and reboot ask the guest; poweroff pulls the plug */
export type VmAction = 'start' | 'shutdown' | 'reboot' | 'poweroff'

/** Body of PUT /api/vms/{name}/config: everything but the name and the disk. */
export type VmSettings = Omit<VmConfig, 'name' | 'disk' | 'diskSizeGb'>

/** Body of POST /api/vms. */
export interface VmCreate extends VmSettings {
  name: string
  diskSizeGb: number
  /** start it right away, to install from the ISO in the console */
  start: boolean
}

const vmPath = (name: string) => `/api/vms/${encodeURIComponent(name)}`

export const fetchVms = () => get<Vm[]>('/api/vms')
export const fetchVm = (name: string) => get<Vm>(vmPath(name))
export const runVmAction = (name: string, action: VmAction) => post<Vm>(`${vmPath(name)}/${action}`)
export const saveVmSettings = (name: string, settings: VmSettings) => put<Vm>(`${vmPath(name)}/config`, settings)
export const createVm = (vm: VmCreate) => post<Vm>('/api/vms', vm)
/** Only a stopped VM; with `disk` its disk is deleted too. */
export const deleteVm = (name: string, disk: boolean) => del(`${vmPath(name)}${disk ? '?disk=true' : ''}`)
export const fetchBridges = () => get<string[]>('/api/bridges')
export const fetchVmLogs = (name: string, lines: number) => get<LogEntry[]>(`${vmPath(name)}/logs?lines=${lines}`)

/** The WebSocket the console (noVNC) talks VNC over. */
export const consoleUrl = (name: string) =>
  `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}${vmPath(name)}/console`
