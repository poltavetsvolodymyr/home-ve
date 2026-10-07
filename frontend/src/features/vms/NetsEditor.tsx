import { Plus, RefreshCw, Trash2 } from 'lucide-react'
import type { VmNet } from './api'
import { maxNets, newNet, randomMac } from './settings'

/** The network cards of a VM: bridge and MAC per card, in slot order. */
export function NetsEditor({
  nets,
  bridges,
  onChange,
}: {
  nets: VmNet[]
  bridges: string[]
  onChange: (nets: VmNet[]) => void
}) {
  const updateNet = (i: number, patch: Partial<VmNet>) =>
    onChange(nets.map((n, j) => (j === i ? { ...n, ...patch } : n)))

  return (
    <fieldset>
      <legend>Network cards</legend>
      {nets.map((n, i) => (
        <div key={i} className="vm-net">
          <span className="muted">{i + 1}</span>
          <select
            aria-label={`Card ${i + 1} bridge`}
            value={n.bridge}
            onChange={e => updateNet(i, { bridge: e.target.value })}
          >
            {!bridges.includes(n.bridge) && <option value={n.bridge}>{n.bridge || 'pick a bridge'}</option>}
            {bridges.map(b => (
              <option key={b} value={b}>
                {b}
              </option>
            ))}
          </select>
          <input
            className="mono"
            aria-label={`Card ${i + 1} MAC`}
            value={n.mac}
            onChange={e => updateNet(i, { mac: e.target.value })}
            spellCheck={false}
            autoCapitalize="characters"
          />
          <button
            type="button"
            className="icon-button"
            title="New MAC"
            aria-label="New MAC"
            onClick={() => updateNet(i, { mac: randomMac() })}
          >
            <RefreshCw size={15} aria-hidden />
          </button>
          <button
            type="button"
            className="icon-button"
            title="Remove card"
            aria-label={`Remove card ${i + 1}`}
            onClick={() => onChange(nets.filter((_, j) => j !== i))}
          >
            <Trash2 size={15} aria-hidden />
          </button>
        </div>
      ))}
      {nets.length < maxNets && (
        <button type="button" onClick={() => onChange([...nets, newNet(bridges[0] ?? '')])}>
          <Plus size={15} aria-hidden /> Add card
        </button>
      )}
      <p className="hint">
        Inside the guest a card keeps its name while its MAC stays the same. Changing a MAC is like swapping the card.
      </p>
    </fieldset>
  )
}
