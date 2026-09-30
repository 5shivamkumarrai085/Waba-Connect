import { apiClient } from '../apiClient'
import type {
  UserPermission,
  DepartmentPermission,
  UserPermissionDashboard,
  DepartmentPermissionDashboard,
  AssignUserPermissionPayload,
  AssignDepartmentPermissionPayload,
  PermissionCandidates
} from '../../types/permission'

export const permissionService = {
  /** The real users and roles that connection access can be granted to. */
  getCandidates: async (): Promise<PermissionCandidates> => {
    const response = await apiClient.get<PermissionCandidates>('/permissions/candidates')
    return response.data
  },

  getUserDashboard: async (): Promise<UserPermissionDashboard> => {
    const response = await apiClient.get<UserPermissionDashboard>('/permissions/user/dashboard')
    return response.data
  },

  getUserPermissions: async (params?: { department?: string; connectionId?: number; activeOnly?: boolean }): Promise<UserPermission[]> => {
    const response = await apiClient.get<UserPermission[]>('/permissions/user', { params })
    return response.data
  },

  assignUserPermission: async (payload: AssignUserPermissionPayload): Promise<UserPermission> => {
    const response = await apiClient.post<UserPermission>('/permissions/user/assign', payload)
    return response.data
  },

  toggleUserPermissionStatus: async (id: number): Promise<void> => {
    await apiClient.post(`/permissions/user/${id}/toggle`)
  },

  deleteUserPermission: async (id: number): Promise<void> => {
    await apiClient.delete(`/permissions/user/${id}`)
  },

  getDepartmentDashboard: async (): Promise<DepartmentPermissionDashboard> => {
    const response = await apiClient.get<DepartmentPermissionDashboard>('/permissions/department/dashboard')
    return response.data
  },

  getDepartmentPermissions: async (params?: { connectionId?: number; activeOnly?: boolean }): Promise<DepartmentPermission[]> => {
    const response = await apiClient.get<DepartmentPermission[]>('/permissions/department', { params })
    return response.data
  },

  assignDepartmentPermission: async (payload: AssignDepartmentPermissionPayload): Promise<DepartmentPermission> => {
    const response = await apiClient.post<DepartmentPermission>('/permissions/department/assign', payload)
    return response.data
  },

  toggleDepartmentPermissionStatus: async (id: number): Promise<void> => {
    await apiClient.post(`/permissions/department/${id}/toggle`)
  },

  deleteDepartmentPermission: async (id: number): Promise<void> => {
    await apiClient.delete(`/permissions/department/${id}`)
  }
}
