import { Link } from 'react-router'
import { fetchIsos } from '@/features/isos/api'
import { bytes } from '@/shared/format'
import { usePoll } from '@/shared/hooks/usePoll'

/** The CD drive: empty, or one of the host's ISO images. */
export function CdromField({ value, onChange }: { value: string | null; onChange: (iso: string | null) => void }) {
  const isos = usePoll(fetchIsos, 0)
  const files = isos.data?.files ?? []

  return (
    <label>
      <span>CD drive</span>
      <select value={value ?? ''} onChange={e => onChange(e.target.value || null)}>
        <option value="">empty</option>
        {value && !files.some(f => f.name === value) && <option value={value}>{value} (missing)</option>}
        {files.map(f => (
          <option key={f.name} value={f.name}>
            {f.name} · {bytes(f.sizeBytes)}
          </option>
        ))}
      </select>
      <small className="muted">
        <Link to="/isos">ISO images</Link>
      </small>
    </label>
  )
}
