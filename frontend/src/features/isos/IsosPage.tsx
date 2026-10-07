import { ArrowLeft, Download, Trash2, X } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { bytes, dateTime } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'
import { Card, ErrorNote, Skeleton } from '@/shared/ui'
import { deleteIso, downloadIso, fetchIsos, type IsoDownload } from './api'
import { downloadPercent } from './progress'
import './isos.css'

/**
 * Installer images for the VMs' CD drives. The host downloads them itself from a link (handier on a phone
 * than uploading 700 MB), into home-backend's ISO directory, where vm-run takes them from.
 */
export function IsosPage() {
  const { data, error, refresh } = usePoll(fetchIsos, 2000)
  const [url, setUrl] = useState('')
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string>()

  const act = async (work: () => Promise<unknown>) => {
    setBusy(true)
    setActionError(undefined)
    try {
      await work()
      await refresh()
      return true
    } catch (e) {
      setActionError(e instanceof Error ? e.message : String(e))
      return false
    } finally {
      setBusy(false)
    }
  }

  const start = async (e: FormEvent) => {
    e.preventDefault()
    if (await act(() => downloadIso(url.trim(), name.trim()))) {
      setUrl('')
      setName('')
    }
  }

  const remove = (file: string) => {
    if (window.confirm(`Delete ${file}?`)) void act(() => deleteIso(file))
  }

  return (
    <div className="page">
      <Link className="back-link" to="/vms">
        <ArrowLeft size={16} aria-hidden /> All VMs
      </Link>

      <Card title="Download an ISO">
        <form className="iso-form" onSubmit={start}>
          <input
            type="url"
            placeholder="https://…/debian-13-amd64-netinst.iso"
            value={url}
            onChange={e => setUrl(e.target.value)}
            autoCapitalize="off"
            autoCorrect="off"
            spellCheck={false}
            aria-label="Link to the ISO"
          />
          <input
            placeholder="file name (optional)"
            value={name}
            onChange={e => setName(e.target.value)}
            autoCapitalize="off"
            autoCorrect="off"
            spellCheck={false}
            aria-label="File name"
          />
          <button className="primary" disabled={busy || !url.trim()}>
            <Download size={15} aria-hidden /> Download
          </button>
        </form>
        <p className="hint">
          The host fetches the file itself, the phone can be closed meanwhile. The name comes from the link unless
          given; it has to end in .iso.
        </p>
      </Card>

      <ErrorNote error={actionError ?? error} />

      {data?.downloads.map(d => (
        <DownloadRow key={d.name} download={d} onCancel={() => act(() => deleteIso(d.name))} />
      ))}

      <Card
        title="ISO images"
        actions={data?.freeBytes != null && <span className="muted">{bytes(data.freeBytes)} free</span>}
      >
        {!data && <Skeleton width="60%" />}
        {data && data.files.length === 0 && <p className="muted">None yet.</p>}
        {data?.files.map(f => (
          <div key={f.name} className="iso-row">
            <div className="iso-main">
              <span className="mono iso-name">{f.name}</span>
              <span className="muted">
                {bytes(f.sizeBytes)} · {dateTime(f.modified)}
                {f.usedBy.length > 0 && ` · in ${f.usedBy.join(', ')}`}
              </span>
            </div>
            <button
              className="icon-button"
              title={f.usedBy.length ? `In the CD drive of ${f.usedBy.join(', ')}` : 'Delete'}
              aria-label={`Delete ${f.name}`}
              disabled={busy || f.usedBy.length > 0}
              onClick={() => remove(f.name)}
            >
              <Trash2 size={15} aria-hidden />
            </button>
          </div>
        ))}
      </Card>
    </div>
  )
}

function DownloadRow({ download: d, onCancel }: { download: IsoDownload; onCancel: () => void }) {
  const percent = downloadPercent(d)
  return (
    <Card
      title={<span className="mono">{d.name}</span>}
      actions={
        <button
          className="icon-button"
          title={d.error ? 'Dismiss' : 'Cancel'}
          aria-label={d.error ? 'Dismiss' : 'Cancel download'}
          onClick={onCancel}
        >
          <X size={16} aria-hidden />
        </button>
      }
    >
      {d.error ? (
        <div className="error-note">Failed: {d.error}</div>
      ) : (
        <>
          <div
            className="meter"
            role="progressbar"
            aria-valuenow={percent ?? undefined}
            aria-valuemin={0}
            aria-valuemax={100}
          >
            <div style={{ width: `${percent ?? 0}%` }} />
          </div>
          <p className="muted iso-progress">
            {bytes(d.receivedBytes)}
            {d.totalBytes != null && ` of ${bytes(d.totalBytes)} · ${percent}%`}
          </p>
        </>
      )}
      <p className="muted iso-url">{d.url}</p>
    </Card>
  )
}
