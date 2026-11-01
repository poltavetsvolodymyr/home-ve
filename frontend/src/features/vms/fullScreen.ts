import { useCallback, useEffect, useState, useSyncExternalStore, type CSSProperties, type RefObject } from 'react'

// the visible part of the page, which a phone's keyboard makes smaller: "top height", for useSyncExternalStore
const viewportNow = () => {
  const vv = window.visualViewport
  return vv ? `${vv.offsetTop} ${vv.height}` : ''
}
const onViewport = (changed: () => void) => {
  const vv = window.visualViewport
  vv?.addEventListener('resize', changed)
  vv?.addEventListener('scroll', changed)
  return () => {
    vv?.removeEventListener('resize', changed)
    vv?.removeEventListener('scroll', changed)
  }
}

/**
 * Full screen for a part of the page (the screen or the terminal with its keys): the browser's own where it has it,
 * else laid over the whole page, as high as what the keyboard leaves of it. Safari on an iPhone has full screen only
 * for video, so there the button did nothing. `style` goes on the part when laid over.
 */
export function useFullScreen(ref: RefObject<HTMLElement | null>) {
  const [real, setReal] = useState(false)
  const [overlay, setOverlay] = useState(false)
  const viewport = useSyncExternalStore(onViewport, viewportNow, () => '')

  useEffect(() => {
    const changed = () => setReal(ref.current !== null && document.fullscreenElement === ref.current)
    document.addEventListener('fullscreenchange', changed)
    return () => document.removeEventListener('fullscreenchange', changed)
  }, [ref])

  // the page behind doesn't scroll along
  useEffect(() => {
    if (!overlay) return
    const before = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = before
    }
  }, [overlay])

  const enter = useCallback(() => {
    const el = ref.current
    if (el && document.fullscreenEnabled) el.requestFullscreen().catch(() => setOverlay(true))
    else setOverlay(true)
  }, [ref])

  const exit = useCallback(() => {
    if (document.fullscreenElement) void document.exitFullscreen()
    setOverlay(false)
  }, [])

  const [top, height] = viewport.split(' ').map(Number)
  const style: CSSProperties | undefined = overlay && viewport ? { top, height } : undefined
  return { full: real || overlay, overlay, style, enter, exit }
}
