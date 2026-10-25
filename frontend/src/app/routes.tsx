import type { ReactNode } from 'react'
import { HostPage } from '@/features/host/HostPage'
import { VmsPage } from '@/features/vms/VmsPage'

interface Page {
  /** The URL path, e.g. `/vms`. */
  path: string
  /** Tab title; its key is its place in this list (1, 2, 3…). */
  label: string
  element: ReactNode
}

/** The tabs, in order. A new page is one entry here. The first one is the start page. */
export const pages: readonly Page[] = [
  { path: '/vms', label: 'VMs', element: <VmsPage /> },
  { path: '/host', label: 'Host', element: <HostPage /> },
]
