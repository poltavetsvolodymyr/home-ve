import { createContext, useContext } from 'react'

export interface Session {
  /** `callServer = false` when the server already told us the session is gone. */
  logout: (callServer?: boolean) => void
}

/** Provided by the app shell; lets any data hook sign the user out on a 401. */
export const SessionContext = createContext<Session>({ logout: () => {} })

export const useSession = () => useContext(SessionContext)
