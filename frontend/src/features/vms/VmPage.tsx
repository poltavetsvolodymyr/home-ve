import { ArrowLeft } from 'lucide-react'
import { lazy, Suspense } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { VmBackups } from '@/features/backups/VmBackups'
import { usePoll } from '@/shared/hooks/usePoll'
import { Badge, Card, ErrorNote, Segmented, Skeleton } from '@/shared/ui'
import { fetchVm } from './api'
import { VmActions } from './VmActions'
import { VmLogs } from './VmLogs'
import { VmDelete } from './VmDelete'
import { VmSettingsForm } from './VmSettingsForm'
import { VmSummary } from './VmSummary'
import { hasScreen, stateBadge } from './vmState'
import './vms.css'

// noVNC is big (and only needed here): loaded when the console tab opens
const VmConsole = lazy(() => import('./VmConsole'))

const tabs = [
  { value: 'summary', label: 'Summary' },
  { value: 'console', label: 'Console' },
  { value: 'settings', label: 'Settings' },
  { value: 'backups', label: 'Backups' },
  { value: 'logs', label: 'Logs' },
] as const
type Tab = (typeof tabs)[number]['value']

/** `/vms/:name?tab=console`: one VM, with its buttons and five tabs. The tab is in the URL, so a reload keeps it. */
export function VmPage() {
  const { name = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const tab: Tab = tabs.find(t => t.value === params.get('tab'))?.value ?? 'summary'
  const { data: vm, error, refresh } = usePoll(() => fetchVm(name), 2000, name)
  const [status, label] = vm ? stateBadge(vm.state) : ['neutral' as const, '']

  return (
    // on the Logs tab the log takes the rest of the screen: no page scrolling, only the log scrolls
    <div className={tab === 'logs' ? 'page fill-screen' : 'page'}>
      <Link className="back-link" to="/vms">
        <ArrowLeft size={16} aria-hidden /> All VMs
      </Link>
      <ErrorNote error={error} />
      <Card
        title={
          <>
            {name} {vm ? <Badge status={status}>{label}</Badge> : <Skeleton width="4em" />}
          </>
        }
        actions={vm && <VmActions vm={vm} onDone={() => refresh()} />}
      >
        <Segmented
          label="Section"
          value={tab}
          options={[...tabs]}
          onChange={t => setParams(t === 'summary' ? {} : { tab: t }, { replace: true })}
        />
      </Card>

      {tab === 'summary' && <VmSummary vm={vm} />}
      {tab === 'console' &&
        (vm && hasScreen(vm.state) ? (
          <Suspense
            fallback={
              <Card title="Console">
                <Skeleton width="10em" />
              </Card>
            }
          >
            <VmConsole name={name} />
          </Suspense>
        ) : (
          <Card title="Console">
            <p className="muted">
              {vm ? 'The VM is not running. Start it to see its screen.' : <Skeleton width="16em" />}
            </p>
          </Card>
        ))}
      {tab === 'settings' && vm && (
        <>
          <VmSettingsForm vm={vm} onSaved={() => refresh()} />
          <VmDelete vm={vm} />
        </>
      )}
      {tab === 'backups' && vm && <VmBackups vm={vm} />}
      {tab === 'logs' && <VmLogs name={name} />}
    </div>
  )
}
