import { get, post } from '@/shared/api/http'

// Mirrors backend/HomeBackend/Installer/MachineInfo.cs

export interface Disk {
  /** kernel name: the device is /dev/<name> */
  name: string
  sizeBytes: number
  model: string | null
  serial: string | null
  /** sata, nvme, usb, virtio…; null when unknown */
  transport: string | null
  removable: boolean
  rotational: boolean
  /** something on it is mounted: the stick the installer runs from */
  inUse: boolean
}

export interface Nic {
  name: string
  mac: string
  /** a cable is in and the other end is up */
  link: boolean
  wireless: boolean
  driver: string | null
  /** IPv4 addresses it has now, e.g. 192.168.178.57/24 */
  addresses: string[]
}

export interface Machine {
  uefi: boolean
  cpu: string | null
  cpus: number
  memoryBytes: number
  disks: Disk[]
  nics: Nic[]
}

/** Resolves when this browser has a session; throws `Unauthorized` when it hasn't. */
export const fetchSession = () => get<void>('/api/installer/session')
/** The code from the machine's screen; throws with the server's reason when it's wrong. */
export const createSession = (code: string) => post('/api/installer/session', { code })
export const fetchMachine = () => get<Machine>('/api/installer/machine')

/** On the machine's own screen only: how a phone gets in. Throws (404) anywhere else. */
export const fetchLocalAccess = () => get<{ code: string; urls: string[] }>('/api/installer/local')
