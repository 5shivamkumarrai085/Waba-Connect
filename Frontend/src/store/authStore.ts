import { create } from 'zustand'
import toast from 'react-hot-toast'
import { authService } from '../services/auth/authService'
import {
  AUTH_TOKEN_STORAGE_KEY,
  REFRESH_TOKEN_STORAGE_KEY,
  refreshSession,
  resetApiClientCaches,
  setSessionHandlers,
  storeSessionTokens,
} from '../services/apiClient'
import { getErrorMessage } from '../utils/errorHelper'
import type { CurrentUser, LoginPayload, LoginResult } from '../types/auth'

/**
 * How long the startup session check may take before the app gives up and shows the login screen.
 *
 * Shorter than the client's own request timeout on purpose: nothing renders while this is
 * pending, so the cost of waiting is a user staring at a splash with no feedback. /auth/me answers
 * in ~0.3s against a warm backend but ~9.5s against one still starting; the deadline sits well
 * clear of that while still guaranteeing boot always terminates.
 */
const SESSION_RESTORE_TIMEOUT_MS = 20_000

interface AuthState {
  user: CurrentUser | null
  token: string | null
  /** True until the stored session has been revalidated on startup. Gates route guards so a
   *  refresh doesn't bounce a signed-in user to the login screen mid-check. */
  isInitializing: boolean
  isLoggingIn: boolean

  login: (payload: LoginPayload) => Promise<CurrentUser>
  logout: (options?: { silent?: boolean }) => Promise<void>
  /** Revalidates a stored session against the server. Called once on app start. */
  initialize: () => Promise<void>
  refreshUser: () => Promise<void>
  /** Adopts tokens and user returned by sign-in or a password change. */
  applySession: (session: LoginResult) => void
  clearMustChangePassword: () => void
}

const endSession = () => {
  storeSessionTokens(null)
  resetApiClientCaches()
}

/**
 * Session state.
 *
 * Persistence is hand-rolled rather than using zustand/persist — no store in this codebase uses
 * middleware, and the token has to be readable synchronously by the axios request interceptor,
 * which sits outside React.
 */
export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  token: localStorage.getItem(AUTH_TOKEN_STORAGE_KEY),
  isInitializing: true,
  isLoggingIn: false,

  login: async (payload) => {
    set({ isLoggingIn: true })
    try {
      const result = await authService.login(payload)

      storeSessionTokens({ token: result.token, refreshToken: result.refreshToken })
      // Drop anything cached against the previous session before any authenticated request
      // goes out, or the first page load can replay the logged-out response.
      resetApiClientCaches()

      set({ user: result.user, token: result.token, isLoggingIn: false })
      return result.user
    } catch (error) {
      set({ isLoggingIn: false })
      throw error
    }
  },

  logout: async (options) => {
    await authService.logout()

    endSession()
    set({ user: null, token: null })

    if (!options?.silent) {
      toast.success('Signed out successfully.')
    }
  },

  initialize: async () => {
    const hasSession =
      !!localStorage.getItem(AUTH_TOKEN_STORAGE_KEY) || !!localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)

    if (!hasSession) {
      set({ user: null, token: null, isInitializing: false })
      return
    }

    try {
      // An access token lasts minutes; a returning user usually needs a refresh first. The
      // request interceptor does that on its own, but doing it here keeps boot to one path.
      if (!localStorage.getItem(AUTH_TOKEN_STORAGE_KEY)) {
        await refreshSession()
      }

      // Boot must finish even if the network never answers: racing a short deadline means the
      // worst case is the login screen, which the user can act on.
      const user = await Promise.race([
        authService.getCurrentUser(),
        new Promise<never>((_, reject) =>
          setTimeout(() => reject(new Error('Session restore timed out')), SESSION_RESTORE_TIMEOUT_MS)
        ),
      ])
      set({ user, token: localStorage.getItem(AUTH_TOKEN_STORAGE_KEY), isInitializing: false })
    } catch {
      // Expired, revoked, the account was removed, or the backend did not answer in time.
      // Cleared quietly — an error toast on every cold start would be noise.
      endSession()
      set({ user: null, token: null, isInitializing: false })
    }
  },

  refreshUser: async () => {
    if (!get().token) return
    try {
      const user = await authService.getCurrentUser()
      set({ user })
    } catch (error) {
      toast.error(getErrorMessage(error, 'Could not refresh your account details.'))
    }
  },

  applySession: (session) => {
    storeSessionTokens({ token: session.token, refreshToken: session.refreshToken })
    set({ user: session.user, token: session.token })
  },

  clearMustChangePassword: () => {
    const user = get().user
    if (user) set({ user: { ...user, mustChangePassword: false } })
  },
}))

// Wired once, at module load. Registering from a component would re-run on every mount and risks
// the handlers being absent during the first requests of a cold start.
setSessionHandlers({
  onUnauthorized: () => {
    // Ignore 401s that arrive when we already know there's no session — e.g. the startup probe
    // with an expired session, which initialize() handles on its own.
    if (!useAuthStore.getState().token) return

    endSession()
    useAuthStore.setState({ user: null, token: null })
    toast.error('Your session has ended. Please sign in again.')
  },
  onTokensRefreshed: ({ token }) => {
    useAuthStore.setState({ token })
  },
  onPasswordChangeRequired: () => {
    const user = useAuthStore.getState().user
    if (user && !user.mustChangePassword) {
      useAuthStore.setState({ user: { ...user, mustChangePassword: true } })
    }
  },
})

export default useAuthStore
