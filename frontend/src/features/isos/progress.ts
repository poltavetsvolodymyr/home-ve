import type { IsoDownload } from './api'

/** Whole percent done, or null when the server didn't say how big the file is. */
export function downloadPercent(d: Pick<IsoDownload, 'receivedBytes' | 'totalBytes'>): number | null {
  if (!d.totalBytes) return null
  return Math.min(100, Math.floor((d.receivedBytes / d.totalBytes) * 100))
}
