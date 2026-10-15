import { del, get, post } from '@/shared/api/http'

// Mirrors backend/HomeBackend/Features/Isos/IsoModels.cs

/** An ISO for the CD drive, or a qcow2 disk image a new VM's disk is made from. */
export interface IsoFile {
  name: string
  sizeBytes: number
  modified: string
  /** VMs that have it in their CD drive; it can't be deleted while any do */
  usedBy: string[]
  /** a qcow2's disk size, the least a disk made from it can have; null for an ISO */
  diskBytes: number | null
}

export const isIso = (f: IsoFile) => f.name.endsWith('.iso')
/** GiB a VM's disk needs to take the image */
export const imageDiskGb = (f: IsoFile) => Math.ceil((f.diskBytes ?? 0) / 2 ** 30)

export interface IsoDownload {
  name: string
  url: string
  receivedBytes: number
  /** null when the server doesn't say */
  totalBytes: number | null
  /** why it failed; null while it runs */
  error: string | null
}

export interface IsoList {
  files: IsoFile[]
  downloads: IsoDownload[]
  /** free space where the images live; null when unknown */
  freeBytes: number | null
}

export const fetchIsos = () => get<IsoList>('/api/isos')
/** Starts a download on the host; `name` overrides the file name taken from the link. */
export const downloadIso = (url: string, name?: string) => post<void>('/api/isos', { url, name: name || null })
/** Deletes an image, or cancels or dismisses a download. */
export const deleteIso = (name: string) => del(`/api/isos/${encodeURIComponent(name)}`)
