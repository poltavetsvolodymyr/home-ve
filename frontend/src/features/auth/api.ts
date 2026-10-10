import { get, post } from '@/shared/api/http'

/** Resolves when the session cookie is valid; throws `Unauthorized` when it isn't. */
export const fetchMe = () => get<{ name: string | null }>('/api/auth/me')

/** Sets the session cookie; throws `Unauthorized` for a wrong password. */
export const login = (password: string) => post('/api/auth/login', { password })

export const logout = () => post('/api/auth/logout')

/** Whether the host has no password yet, so the first-run setup is shown instead of the login. */
export const fetchSetup = () => get<{ needed: boolean }>('/api/auth/setup')

/** The first password, with the setup code from the host; signs in. Throws with the server's reason. */
export const setUp = (code: string, password: string) => post('/api/auth/setup', { code, password })
