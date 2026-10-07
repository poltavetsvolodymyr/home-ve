import { get, post } from '@/shared/api/http'

/** Resolves when the session cookie is valid; throws `Unauthorized` when it isn't. */
export const fetchMe = () => get<{ name: string | null }>('/api/auth/me')

/** Sets the session cookie; throws `Unauthorized` for a wrong password. */
export const login = (password: string) => post('/api/auth/login', { password })

export const logout = () => post('/api/auth/logout')
