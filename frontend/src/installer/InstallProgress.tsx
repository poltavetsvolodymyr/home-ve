import { useEffect, useRef, useState } from 'react'
import { usePoll } from '@/shared/hooks/usePoll'
import { Card, confirm, ErrorNote } from '@/shared/ui'
import { fetchInstall, reboot, type InstallStatus } from './api'

/** The install as it runs: the step, the log, and at the end the restart (or, if it failed, the form again). */
export function InstallProgress({ status: first, onRetry }: { status: InstallStatus; onRetry: () => void }) {
  const { data, error } = usePoll(fetchInstall, 2000)
  const s = data ?? first
  const [restarting, setRestarting] = useState(false)
  const [rebootError, setRebootError] = useState<string>()
  const log = useRef<HTMLPreElement>(null)

  // the newest lines in sight, unless the user scrolled up to read
  useEffect(() => {
    const el = log.current
    if (el && el.scrollHeight - el.scrollTop - el.clientHeight < 80) el.scrollTop = el.scrollHeight
  }, [s.log])

  async function restart() {
    const ok = await confirm({
      title: 'Restart now?',
      message: "Take the installer's stick out first, so the machine starts from its disk.",
      confirmLabel: 'Restart',
    })
    if (!ok) return
    try {
      await reboot()
      setRestarting(true)
    } catch (err) {
      setRebootError(err instanceof Error ? err.message : String(err))
    }
  }

  const percent = s.steps ? ((s.state === 'done' ? s.steps : Math.max(s.step - 1, 0)) / s.steps) * 100 : 0
  return (
    <>
      <ErrorNote error={error} />
      <Card title={title(s)}>
        <div className={s.state === 'failed' ? 'meter critical' : 'meter'}>
          <div style={{ width: `${percent}%` }} />
        </div>
        <p className="installer-step">
          {s.state === 'running' && s.step > 0 && (
            <>
              <span className="muted">
                Step {s.step} of {s.steps}:
              </span>{' '}
              {s.current}
            </>
          )}
          {s.state === 'running' && s.step === 0 && <span className="muted">Starting…</span>}
          {s.state === 'done' &&
            'Installed. The first start sets home-ve up: it takes a few minutes and needs the network.'}
          {s.state === 'failed' &&
            `It stopped at step ${s.step} of ${s.steps}: ${s.current ?? 'the start'}. The log below says why.`}
        </p>
        {s.log.length > 0 && (
          <pre ref={log} className="installer-log">
            {s.log.join('\n')}
          </pre>
        )}
      </Card>

      {s.state === 'done' && (
        <Card title="Next">
          <ol className="installer-next-steps">
            <li>Take the installer&apos;s stick out.</li>
            <li>Restart. The machine&apos;s screen shows the web UI&apos;s address once it is up.</li>
            <li>Open it and sign in with the web UI&apos;s password set here.</li>
          </ol>
          {restarting && (
            <p className="installer-step">
              Restarting. Once the machine&apos;s screen shows the web UI&apos;s address, reload this page: the browser
              warns about the certificate once more, it is the installed system&apos;s own.
            </p>
          )}
          {rebootError && <div className="error-note">{rebootError}</div>}
          <button className="primary installer-go" disabled={restarting} onClick={restart}>
            {restarting ? 'Restarting…' : 'Restart'}
          </button>
        </Card>
      )}
      {s.state === 'failed' && (
        <button className="installer-go" onClick={onRetry}>
          Back to the settings
        </button>
      )}
    </>
  )
}

function title(s: InstallStatus) {
  switch (s.state) {
    case 'done':
      return 'home-ve is installed'
    case 'failed':
      return 'The install failed'
    default:
      return 'Installing…'
  }
}
