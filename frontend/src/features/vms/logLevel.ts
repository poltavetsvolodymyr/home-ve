// syslog priorities: 0–3 error and worse, 4 warning, 5–7 informational

/** Row highlight class: errors red, warnings amber. */
export const levelClass = (priority: number) => (priority <= 3 ? 'critical' : priority === 4 ? 'warning' : '')

export const levelLabel = (priority: number) =>
  priority <= 3 ? 'ERR' : priority === 4 ? 'WARN' : priority === 7 ? 'DBG' : 'INFO'

/** One journal line, as backend/HomeBackend/Features/Logs/LogEntry.cs sends it. */
export interface LogEntry {
  /** ISO date */
  time: string
  /** syslog priority */
  priority: number
  source: string
  message: string
}
