import { Link } from 'react-router'
import { fetchIsos, imageDiskGb, isIso, type IsoFile } from '@/features/isos/api'
import { usePoll } from '@/shared/hooks/usePoll'

/**
 * What a new VM's disk starts as: empty (to install from the CD), or one of the host's qcow2 images, which
 * vm-run writes onto it at the first start. Picking one hands the image back, for its size.
 */
export function ImageField({ value, onChange }: { value: string | null; onChange: (image: IsoFile | null) => void }) {
  const isos = usePoll(fetchIsos, 0)
  const images = (isos.data?.files ?? []).filter(f => !isIso(f))

  return (
    <label className="wide">
      <span>Disk contents</span>
      <select value={value ?? ''} onChange={e => onChange(images.find(f => f.name === e.target.value) ?? null)}>
        <option value="">empty</option>
        {value && !images.some(f => f.name === value) && <option value={value}>{value} (missing)</option>}
        {images.map(f => (
          <option key={f.name} value={f.name}>
            {f.name} · {imageDiskGb(f)} GiB
          </option>
        ))}
      </select>
      <small className="muted">
        a qcow2 from <Link to="/isos">Images</Link>, written onto the disk at the first start
      </small>
    </label>
  )
}

/** The firmware: the BIOS, or UEFI, which some images need (Home Assistant OS). */
export function FirmwareField({ uefi, onChange }: { uefi: boolean; onChange: (uefi: boolean) => void }) {
  return (
    <label>
      <span>Firmware</span>
      <select value={uefi ? 'uefi' : 'bios'} onChange={e => onChange(e.target.value === 'uefi')}>
        <option value="bios">BIOS</option>
        <option value="uefi">UEFI</option>
      </select>
      <small className="muted">a system boots with the one it was installed with</small>
    </label>
  )
}
