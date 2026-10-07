const periods = ['1 min', '5 min', '15 min']

/**
 * The load average: processes running or waiting for a CPU, averaged over 1, 5 and 15 minutes.
 * Equal to the number of cores means every core is busy; above it, work is queueing.
 */
export function LoadAverage({ values, cores }: { values: number[]; cores: number }) {
  return (
    <div title="Processes running or waiting for a CPU. Equal to the number of cores: every core is busy.">
      <div>Load avg · {cores} cores</div>
      <div className="load-values">
        {periods.map((period, i) => (
          <span key={period}>
            <b>{values[i]?.toFixed(2) ?? '—'}</b>
            {period}
          </span>
        ))}
      </div>
    </div>
  )
}
