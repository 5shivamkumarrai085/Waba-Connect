import { apiClient, REFRESH_TOKEN_STORAGE_KEY } from '../apiClient'
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
   * revalidate a stored session, and after a password change.
   */
  getCurrentUser: async (): Promise<CurrentUser> => {
    // apiClient.request rather than the de-duplicated get: this runs right after sign-in, when
    // the session reset aborts in-flight GETs, and must not be one of them.
    const response = await apiClient.request({ method: 'GET', url: '/auth/me' })
    return response.data?.data as CurrentUser
  },

  /** Changes the password. The server ends every other session and returns fresh tokens. */
  changePassword: async (payload: ChangePasswordPayload): Promise<LoginResult> => {
    const response = await apiClient.post('/auth/change-password', payload)
    return response.data?.data as LoginResult
  },

  logout: async (): Promise<void> => {
    // Revokes this session's refresh token on the server, so it can never mint another access
    // token. Best-effort: signing out locally must succeed even when the server is unreachable.
    try {
      await apiClient.post('/auth/logout', {
        refreshToken: localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY) ?? undefined,
      })
    } catch {
      // An expired session is the most likely failure and is irrelevant here.
    }
  },
}
