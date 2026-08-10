import { apiClient } from '../apiClient'
import type { ChangePasswordPayload, CurrentUser, LoginPayload, LoginResult } from '../../types/auth'

/**
 * Auth endpoints.
 *
 * Unlike the read-heavy services in this codebase, nothing here swallows errors — the caller
 * always needs to know why a sign-in failed in order to say so on screen.
 */
export const authService = {
  login: async (payload: LoginPayload): Promise<LoginResult> => {
    const response = await apiClient.post('/auth/login', payload)
    return response.data?.data as LoginResult
  },

  /**
   * Re-reads the signed-in user and their current permissions. Called on app start to
   * revalidate a stored token, and after a password change.
   */
  getCurrentUser: async (): Promise<CurrentUser> => {
    // Deliberately apiClient.request, not apiClient.get: the patched getter dedupes by URL and
    // would hand a session-restore call the response from an unrelated in-flight /auth/me. It
    // also aborts in-flight GETs on sign-in via resetApiClientCaches() — which is exactly when
    // this call runs, so routing it through the patch would cancel the request the login flow
    // is waiting on. Every other GET does want the patch; this one is the deliberate exception.
    const response = await apiClient.request({ method: 'GET', url: '/auth/me' })
    return response.data?.data as CurrentUser
  },

  changePassword: async (payload: ChangePasswordPayload): Promise<void> => {
    await apiClient.post('/auth/change-password', payload)
  },

  logout: async (): Promise<void> => {
    // Best-effort: tokens are stateless, so the client discarding its copy is what actually
    // ends the session. The call exists so the event reaches the audit trail.
    try {
      await apiClient.post('/auth/logout')
    } catch {
      // An expired token is the most likely failure and is irrelevant here.
    }
  }
}
