import { create } from 'zustand'
import toast from 'react-hot-toast'
import { authService } from '../services/auth/authService'
import { AUTH_TOKEN_STORAGE_KEY, resetApiClientCaches, setUnauthorizedHandler } from '../services/apiClient'
import { getErrorMessage } from '../utils/errorHelper'
import type { CurrentUser, LoginPayload } from '../types/auth'

/**
 * How long the startup token check may take before the app gives up and shows the login screen.
 *
 * Shorter than the client's own request timeout on purpose: nothing renders while this is
 * pending, so the cost of waiting is a user staring at a splash with no feedback.
 *
 * Measured: /auth/me answers in ~0.3s against a warm backend but took ~9.5s against one that
 * was still starting (migration check, seeding, EF model build). The deadline sits well clear
 * of that — being bounced to the login screen while holding a perfectly good session is a worse
 * failure than waiting a few more seconds — while still guaranteeing boot always terminates.
 */
const SESSION_RESTORE_TIMEOUT_MS = 20_000

interface AuthState {
  user: CurrentUser | null
  token: string | null
  /** True until the stored token has been revalidated on startup. Gates route guards so a
   *  refresh doesn't bounce a signed-in user to the login screen mid-check. */
  isInitializing: boolean
  isLoggingIn: boolean

  login: (payload: LoginPayload) => Promise<CurrentUser>
  logout: (options?: { silent?: boolean }) => Promise<void>
  /** Revalidates a stored token against the server. Called once on app start. */
  initialize: () => Promise<void>
  refreshUser: () => Promise<void>
  clearMustChangePassword: () => void
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

      localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, result.token)
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

    localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY)
    resetApiClientCaches()
    set({ user: null, token: null })

    if (!options?.silent) {
      toast.success('Signed out successfully.')
    }
  },

  initialize: async () => {
    const token = localStorage.getItem(AUTH_TOKEN_STORAGE_KEY)

    if (!token) {
      set({ user: null, token: null, isInitializing: false })
      return
    }

    try {
      // Boot must finish even if the network never answers. The app renders nothing but
      // "Restoring your session…" while isInitializing is true, so an unbounded wait here is
      // indistinguishable from a crash — the user is left on a blank splash with no way out.
      // Racing a short deadline means the worst case is the login screen, which they can act on.
      const user = await Promise.race([
        authService.getCurrentUser(),
        new Promise<never>((_, reject) =>
          setTimeout(() => reject(new Error('Session restore timed out')), SESSION_RESTORE_TIMEOUT_MS)
        )
      ])
      set({ user, token, isInitializing: false })
    } catch {
      // Expired, tampered with, the account was removed, or the backend did not answer in time.
      // Clear it quietly — being bounced to the login screen with an error toast on every cold
      // start would be noise.
      localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY)
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

  clearMustChangePassword: () => {
    const user = get().user
    if (user) set({ user: { ...user, mustChangePassword: false } })
  }
}))

// Wire the 401 handler once, at module load. Registering from a component would re-run on every
// mount and risks the handler being absent during the first requests of a cold start.
setUnauthorizedHandler(() => {
  const { token } = useAuthStore.getState()
  // Ignore 401s that arrive when we already know there's no session — e.g. the initial
  // /auth/me probe with a stale token, which initialize() handles on its own.
  if (!token) return

  localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY)
  resetApiClientCaches()
  useAuthStore.setState({ user: null, token: null })
  toast.error('Your session has expired. Please sign in again.')
})

export default useAuthStore
