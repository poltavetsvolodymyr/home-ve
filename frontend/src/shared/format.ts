const nf1 = new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 })
const nf0 = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 })

/** 1.3 GB. Binary steps (1024), like `ip -s link`. */
export function bytes(n: number): string {
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let i = 0
  // 999.6 would round to "1,000": switch units before that
  while (n >= 999.5 && i < units.length - 1) {
    n /= 1024
    i++
  }
  return `${(n >= 100 || i === 0 ? nf0 : nf1).format(n)} ${units[i]}`
}

/** Network rates are shown in bits per second, like every ISP contract. */
export function rate(bytesPerSecond: number): string {
  let n = bytesPerSecond * 8
  const units = ['bit/s', 'kbit/s', 'Mbit/s', 'Gbit/s']
  let i = 0
  while (n >= 999.5 && i < units.length - 1) {
    n /= 1000
    i++
  }
  return `${(n >= 100 || i === 0 ? nf0 : nf1).format(n)} ${units[i]}`
}

/** 12d 7h, 3h 5m, 42m: the two largest units. */
export function duration(seconds: number): string {
  const d = Math.floor(seconds / 86400)
  const h = Math.floor((seconds % 86400) / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  if (d > 0) return `${d}d ${h}h`
  if (h > 0) return `${h}h ${m}m`
  return `${m}m`
}

/** Time elapsed since an ISO date, e.g. a service's start. */
export function since(iso: string | null): string {
  if (!iso) return '—'
  return duration(Math.max(0, (Date.now() - Date.parse(iso)) / 1000))
}

/** Time left until an ISO date, e.g. a DHCP lease's expiry. */
export function until(iso: string | null): string {
  if (!iso) return 'never'
  const s = (Date.parse(iso) - Date.now()) / 1000
  return s <= 0 ? 'expired' : `in ${duration(s)}`
}

const timeFmt = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
const dateTimeFmt = new Intl.DateTimeFormat('en-GB', {
  day: '2-digit',
  month: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
})

/** 14:05:09 */
export const clock = (t: number | string) => timeFmt.format(new Date(t))
/** 04/10, 14:05:09 */
export const dateTime = (t: number | string) => dateTimeFmt.format(new Date(t))
/** 42% */
export const percent = (n: number) => `${nf0.format(n)}%`
