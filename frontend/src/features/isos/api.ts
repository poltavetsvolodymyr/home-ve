import { del, get, post } from '@/shared/api/http'

// Mirrors backend/HomeBackend/Features/Isos/IsoModels.cs

export interface IsoFile {
  name: string
  sizeBytes: number
  modified: string
  /** VMs that have it in their CD drive; it can't be deleted while any do */
  usedBy: string[]
}

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
