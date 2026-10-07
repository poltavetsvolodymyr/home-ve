import { createBrowserRouter, Navigate } from 'react-router'
import { SettingsPage } from '@/features/settings/SettingsPage'
import { VmPage } from '@/features/vms/VmPage'
import { Layout } from './Layout'
import { pages } from './routes'

const start = <Navigate to={pages[0].path} replace />

/**
 * Real paths (`/vms`, `/vms/router?tab=console`). nginx answers every path that isn't a file with
 * index.html (`try_files`, see docs/deployment.md), and the router picks the page from the path.
 */
export const router = createBrowserRouter([
  {
    element: <Layout />,
    children: [
      { index: true, element: start },
      ...pages.map(p => ({ path: p.path, element: p.element })),
      // a VM's own page: no tab of its own, the VMs tab stays lit
      { path: '/vms/:name', element: <VmPage /> },
      // the gear in the header, not a tab
      { path: '/settings', element: <SettingsPage /> },
      { path: '*', element: start },
    ],
  },
])
