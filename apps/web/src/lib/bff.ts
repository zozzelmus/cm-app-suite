// Fetch wrapper that talks to the BFF (same origin). Cookies + CSRF only.
export class HttpError extends Error {
  status: number
  body: unknown
  constructor(status: number, message: string, body?: unknown) {
    super(message)
    this.status = status
    this.body = body
  }
}

export async function bff<T = unknown>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const res = await fetch(path, {
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      ...(init.headers ?? {}),
    },
    ...init,
  })
  if (!res.ok) {
    let body: unknown
    try { body = await res.json() } catch { /* ignore */ }
    throw new HttpError(res.status, `${res.status} ${res.statusText}`, body)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}
