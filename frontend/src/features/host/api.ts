import { get } from '@/shared/api/http'

// Mirrors backend/HomeBackend/Features/Host/HostFeature.cs and Features/SystemStatus/{SystemInfo,CpuTemperature}.cs

export interface SystemInfo {
  hostname: string
  os: string
  kernel: string
  uptimeSeconds: number
  /** processes running or waiting for a CPU, averaged over 1, 5 and 15 minutes */
  loadAverage: number[]
  cpuCount: number
  /** bytes */
  memoryTotal: number
  memoryAvailable: number
  diskTotal: number
  diskFree: number
}

export interface CpuTemperature {
  /** null when there is no reading; `problem` says why */
  celsius: number | null
  /** the chip's own critical limit, when it reports one (Intel does, AMD doesn't) */
  criticalCelsius: number | null
  source: 'local'
  /** e.g. "no CPU sensor"; null with a null `celsius` means no reading yet */
  problem: string | null
}

export interface Host {
  system: SystemInfo
  cpuPercent: number
  cpuTemperature: CpuTemperature
}

export const fetchHost = () => get<Host>('/api/host')
