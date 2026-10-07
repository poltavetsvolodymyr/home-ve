import type { LucideIcon } from 'lucide-react'
import { Gauge, Layers } from 'lucide-react'
import type { ReactNode } from 'react'
import { HostPage } from '@/features/host/HostPage'
import { VmsPage } from '@/features/vms/VmsPage'

interface Page {
  /** The URL path, e.g. `/vms`. */
  path: string
  /** Tab title. */
  label: string
  /** Tab icon, from https://lucide.dev/icons */
  icon: LucideIcon
  element: ReactNode
}

/** The tabs, in order. A new page is one entry here. The first one is the start page. */
export const pages: readonly Page[] = [
  { path: '/vms', label: 'VMs', icon: Layers, element: <VmsPage /> },
  { path: '/host', label: 'Host', icon: Gauge, element: <HostPage /> },
]
