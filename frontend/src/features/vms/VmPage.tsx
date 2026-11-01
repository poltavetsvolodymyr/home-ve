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
import { serialSetupCommands } from './serialSetup'
import { hasScreen, stateBadge } from './vmState'
import './vms.css'

// noVNC and xterm.js are big (and only needed here): loaded when their tab opens
const VmConsole = lazy(() => import('./VmConsole'))
const VmTerminal = lazy(() => import('./VmTerminal'))

const tabs = [
  { value: 'summary', label: 'Summary' },
  { value: 'console', label: 'Console' },
  { value: 'terminal', label: 'Terminal' },
  { value: 'settings', label: 'Settings' },
  { value: 'backups', label: 'Backups' },
  { value: 'logs', label: 'Logs' },
] as const
type Tab = (typeof tabs)[number]['value']

// Settings has two parts: what to do with the VM (power, delete) and how it is set up
const settingsSections = [
  { value: 'actions', label: 'Actions' },
  { value: 'configuration', label: 'Configuration' },
] as const
type SettingsSection = (typeof settingsSections)[number]['value']

/** `/vms/:name?tab=console`: one VM, with Start (when stopped) and six tabs; `?tab=settings&section=configuration`. The tab is in the URL, so a reload keeps it. */
export function VmPage() {
  const { name = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const tab: Tab = tabs.find(t => t.value === params.get('tab'))?.value ?? 'summary'
  const section: SettingsSection = settingsSections.find(s => s.value === params.get('section'))?.value ?? 'actions'
  // ?tab=console&setup=serial: the Terminal tab sent the person here to switch the serial console on
  const setup = tab === 'console' && params.get('setup') === 'serial'
  const openTerminal = () => setParams({ tab: 'terminal' }, { replace: true })
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
        // only Start up here, for a stopped VM: shutting down, rebooting and powering off are in Settings › Actions
        actions={vm && <VmActions vm={vm} only={['start']} onDone={() => refresh()} />}
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
            <VmConsole
              name={name}
              setup={setup ? { commands: serialSetupCommands, onDone: openTerminal } : undefined}
            />
          </Suspense>
        ) : (
          <Card title="Console">
            <p className="muted">
              {vm ? 'The VM is not running. Start it to see its screen.' : <Skeleton width="16em" />}
            </p>
          </Card>
        ))}
      {tab === 'terminal' &&
        (vm && hasScreen(vm.state) ? (
          <Suspense
            fallback={
              <Card title="Terminal">
                <Skeleton width="10em" />
              </Card>
            }
          >
            <VmTerminal name={name} onSetup={() => setParams({ tab: 'console', setup: 'serial' }, { replace: true })} />
          </Suspense>
        ) : (
          <Card title="Terminal">
            <p className="muted">
              {vm ? 'The VM is not running. Start it to use its serial console.' : <Skeleton width="16em" />}
            </p>
          </Card>
        ))}
      {tab === 'settings' && vm && (
        <>
          <Segmented
            label="Settings"
            value={section}
            options={[...settingsSections]}
            onChange={s =>
              setParams(s === 'actions' ? { tab: 'settings' } : { tab: 'settings', section: s }, { replace: true })
            }
          />
          {section === 'actions' ? (
            <>
              <Card title="Power">
                <VmActions vm={vm} onDone={() => refresh()} />
              </Card>
              <VmDelete vm={vm} />
            </>
          ) : (
            <VmSettingsForm vm={vm} onSaved={() => refresh()} />
          )}
        </>
      )}
      {tab === 'backups' && vm && <VmBackups vm={vm} />}
      {tab === 'logs' && <VmLogs name={name} />}
    </div>
  )
}
