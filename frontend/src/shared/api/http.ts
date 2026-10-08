/** The server answered 401: there is no session, or it expired. */
export class Unauthorized extends Error {}

/** JSON over fetch with the session cookie. Errors carry the server's message when it sent one. */
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    ...init,
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  if (res.status === 401) throw new Unauthorized()
  if (res.status === 429) throw new Error('Too many attempts, wait a couple of minutes')
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      detail = body.detail ?? body.error ?? detail
    } catch {
      /* not json */
    }
    throw new Error(detail)
  }
  // 204, or a 202 that only says "started": no body
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const get = <T>(path: string) => request<T>(path)

export const post = <T>(path: string, body?: unknown) =>
  request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })

export const put = <T>(path: string, body: unknown) => request<T>(path, { method: 'PUT', body: JSON.stringify(body) })

export const del = <T = void>(path: string) => request<T>(path, { method: 'DELETE' })
