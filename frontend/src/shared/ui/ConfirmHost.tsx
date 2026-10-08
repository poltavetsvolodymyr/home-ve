import { useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { answer, confirmStore, type ConfirmRequest } from './confirm'

/** Rendered once (App.tsx); shows whatever confirm() asks, as a modal <dialog>. */
export function ConfirmHost() {
  const request = useSyncExternalStore(confirmStore.subscribe, confirmStore.current)
  return request ? <ConfirmDialog key={request.title + request.typeToConfirm} request={request} /> : null
}

function ConfirmDialog({ request }: { request: ConfirmRequest }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const [typed, setTyped] = useState('')
  const { title, message, confirmLabel = 'OK', danger = false, typeToConfirm } = request
  const ready = !typeToConfirm || typed.trim() === typeToConfirm

  // showModal: on top of everything, the page behind inert, Esc closes it (the "cancel" event). It focuses
  // the first button, which would show a focus ring on Cancel: the dialog itself takes the focus instead
  // (Tab still reaches the buttons), unless there is a name to type.
  useEffect(() => {
    const d = dialog.current
    if (!d || d.open) return
    d.showModal()
    if (!typeToConfirm) d.focus()
  }, [typeToConfirm])

  return (
    <dialog
      ref={dialog}
      className="confirm"
      tabIndex={-1}
      aria-labelledby="confirm-title"
      onCancel={e => {
        e.preventDefault()
        answer(false)
      }}
      // a tap on the dimmed backdrop lands on the <dialog> itself, not on its contents
      onClick={e => e.target === dialog.current && answer(false)}
    >
      <form
        method="dialog"
        onSubmit={e => {
          e.preventDefault()
          if (ready) answer(true)
        }}
      >
        <h2 id="confirm-title">{title}</h2>
        {message && <div className="confirm-message">{message}</div>}
        {typeToConfirm && (
          <label className="confirm-type">
            <span>
              Type <b className="mono">{typeToConfirm}</b> to confirm
            </span>
            <input
              className="mono"
              value={typed}
              onChange={e => setTyped(e.target.value)}
              autoCapitalize="off"
              autoCorrect="off"
              autoComplete="off"
              spellCheck={false}
              autoFocus
            />
          </label>
        )}
        <div className="confirm-buttons">
          <button type="button" onClick={() => answer(false)}>
            Cancel
          </button>
          <button className={danger ? 'danger-solid' : 'primary'} disabled={!ready}>
            {confirmLabel}
          </button>
        </div>
      </form>
    </dialog>
  )
}
