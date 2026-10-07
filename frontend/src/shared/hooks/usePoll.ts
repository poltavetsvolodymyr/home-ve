import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Unauthorized } from '@/shared/api/http'
import { useSession } from '@/shared/session'

export interface Polled<T> {
  data?: T
  error?: string
  refresh: () => Promise<void>
}

/**
 * The last answer of every `load` function, so a page opened again shows its values at once
 * instead of placeholders that fill in a moment later. Only functions that live as long as the app
 * (`fetchOverview`, not an inline arrow) are found again here; the rest simply start empty.
 */
let lastData = new WeakMap<() => Promise<unknown>, Map<string, unknown>>()

/** Forget all remembered answers, so the next user doesn't see the previous one's data. */
export function clearPollCache() {
  lastData = new WeakMap()
}

/**
 * Loads now, then every `intervalMs` while the tab is visible (`0` loads once).
 * A different `key` (or interval) starts over without the old data, e.g. another log unit.
 * A 401 signs the user out; other errors land in `error` and the last data stays.
 * A page opened again starts with the data it had last time and refreshes it right away.
 */
export function usePoll<T>(load: () => Promise<T>, intervalMs: number, key = ''): Polled<T> {
  const pollKey = `${intervalMs}|${key}`
  const remembered = () => lastData.get(load)?.get(pollKey) as T | undefined

  const [data, setData] = useState<T | undefined>(remembered)
  const [error, setError] = useState<string>()
  const { logout } = useSession()

  // swap the data as soon as the key changes (https://react.dev/learn/you-might-not-need-an-effect)
  const [dataKey, setDataKey] = useState(pollKey)
  if (dataKey !== pollKey) {
    setDataKey(pollKey)
    setData(remembered())
  }

  // always call the latest `load`: it closes over the caller's current parameters
  const loadRef = useRef(load)
  const pollKeyRef = useRef(pollKey)
  useLayoutEffect(() => {
    loadRef.current = load
    pollKeyRef.current = pollKey
  })

  const refresh = useCallback(async () => {
    try {
      const current = loadRef.current
      const forKey = pollKeyRef.current
      const fresh = await current()
      const byKey = lastData.get(current) ?? new Map<string, unknown>()
      lastData.set(current, byKey.set(forKey, fresh))
      setData(fresh)
      setError(undefined)
    } catch (e) {
      if (e instanceof Unauthorized) logout(false)
      else setError(e instanceof Error ? e.message : String(e))
    }
  }, [logout])

  useEffect(() => {
    refresh()
    if (!intervalMs) return
    const whenVisible = () => {
      if (!document.hidden) refresh()
    }
    const id = setInterval(whenVisible, intervalMs)
    document.addEventListener('visibilitychange', whenVisible)
    return () => {
      clearInterval(id)
      document.removeEventListener('visibilitychange', whenVisible)
    }
  }, [refresh, intervalMs, key])

  return { data, error, refresh }
}
