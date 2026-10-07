/**
 * A grey bar where a value will appear once the data arrives. Pages render their full layout from
 * the start with these in place of the values, so nothing jumps when the data comes in.
 */
export function Skeleton({ width = '5em' }: { width?: string }) {
  return <span className="skeleton" style={{ width }} aria-hidden />
}
