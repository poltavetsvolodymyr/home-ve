import { Skeleton } from './Skeleton'

/** Table rows of placeholders, one bar per column, for a table whose data hasn't arrived yet. */
export function SkeletonRows({ rows, widths }: { rows: number; widths: string[] }) {
  return Array.from({ length: rows }, (_, r) => (
    <tr key={r}>
      {widths.map((w, c) => (
        <td key={c}>
          <Skeleton width={w} />
        </td>
      ))}
    </tr>
  ))
}
