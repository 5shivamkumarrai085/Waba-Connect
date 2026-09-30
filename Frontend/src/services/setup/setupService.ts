import { fetchAllPages } from '../pagination'
import { apiClient } from '../apiClient'
import type {
  PermissionCatalog,
  SetupRoleDetail,
  SetupRoleListItem,
  SetupRolePayload,
  SetupUserDetail,
  SetupUserListItem,
  SetupUserPayload,
  UserDashboard
} from '../../types/setup'

/**
 * Setup module API.
 *
 * Write methods deliberately don't catch — the caller needs the server's message to explain
 * why a save was refused ("only remaining administrator", "role still assigned to 3 users").
 * Read methods that feed pickers fall back to an empty list so one failed lookup doesn't
 * blank an entire form.
 */
export const setupService = {
  // ── Permission catalogue ─────────────────────────────────────────────

  getPermissionCatalog: async (): Promise<PermissionCatalog> => {
    const response = await apiClient.get('/permission-catalog')
    return response.data?.data ?? { groups: [] }
  },

  // ── Users ────────────────────────────────────────────────────────────

  getUsers: async (params?: { search?: string; isActive?: boolean; roleId?: number }): Promise<SetupUserListItem[]> => {
    return fetchAllPages<SetupUserListItem>('/setup/users', { ...params })
  },

  getUserDashboard: async (): Promise<UserDashboard | null> => {
    try {
      const response = await apiClient.get('/setup/users/dashboard')
      return response.data?.data ?? null
    } catch {
      return null
    }
  },

  getUser: async (id: number): Promise<SetupUserDetail> => {
    const response = await apiClient.get(`/setup/users/${id}`)
    return response.data?.data
  },

  createUser: async (payload: SetupUserPayload): Promise<SetupUserDetail> => {
    const response = await apiClient.post('/setup/users', payload)
    return response.data?.data
  },

  updateUser: async (id: number, payload: SetupUserPayload): Promise<SetupUserDetail> => {
    const response = await apiClient.put(`/setup/users/${id}`, payload)
    return response.data?.data
  },

  deleteUser: async (id: number): Promise<void> => {
    await apiClient.delete(`/setup/users/${id}`)
  },

  toggleUserActive: async (id: number): Promise<SetupUserListItem> => {
    const response = await apiClient.patch(`/setup/users/${id}/toggle-active`)
    return response.data?.data
  },

  uploadAvatar: async (file: File): Promise<string> => {
    const formData = new FormData()
    formData.append('file', file)
    const response = await apiClient.post('/setup/users/upload-avatar', formData, {
      headers: { 'Content-Type': 'multipart/form-data' }
    })
    return response.data?.data
  },

  // ── Roles ────────────────────────────────────────────────────────────

  getRoles: async (): Promise<SetupRoleListItem[]> => {
    try {
      const response = await apiClient.get('/setup/roles')
      return response.data?.data ?? []
    } catch {
      return []
    }
  },

  getRole: async (id: number): Promise<SetupRoleDetail> => {
    const response = await apiClient.get(`/setup/roles/${id}`)
    return response.data?.data
  },

  createRole: async (payload: SetupRolePayload): Promise<SetupRoleDetail> => {
    const response = await apiClient.post('/setup/roles', payload)
    return response.data?.data
  },

  updateRole: async (id: number, payload: SetupRolePayload): Promise<SetupRoleDetail> => {
    const response = await apiClient.put(`/setup/roles/${id}`, payload)
    return response.data?.data
  },

  deleteRole: async (id: number): Promise<void> => {
    await apiClient.delete(`/setup/roles/${id}`)
  }
}

export default setupService
