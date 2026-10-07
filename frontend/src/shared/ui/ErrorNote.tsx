/** A failed request, shown in place; renders nothing without an error. */
export function ErrorNote({ error }: { error?: string }) {
  return error ? (
    <div className="error-note" role="alert">
      Failed to load: {error}
    </div>
  ) : null
}
