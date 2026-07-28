import { apiClient } from '../apiClient'
import type { Connection, ConnectionDashboard, CreateConnectionPayload, UpdateConnectionPayload } from '../../types/connection'

export const connectionService = {
  getConnections: async (params?: { userId?: string; departmentId?: string }): Promise<Connection[]> => {
    const response = await apiClient.get<Connection[]>('/connections', { params })
    return response.data
  },

  getDashboard: async (params?: { userId?: string; departmentId?: string }): Promise<ConnectionDashboard> => {
    const response = await apiClient.get<ConnectionDashboard>('/connections/dashboard', { params })
    return response.data
  },

  getConnectionById: async (id: number): Promise<Connection> => {
    const response = await apiClient.get<Connection>(`/connections/${id}`)
    return response.data
  },

  createConnection: async (payload: CreateConnectionPayload): Promise<Connection> => {
    const response = await apiClient.post<Connection>('/connections', payload)
    return response.data
  },

  updateConnection: async (id: number, payload: UpdateConnectionPayload): Promise<Connection> => {
    const response = await apiClient.put<Connection>(`/connections/${id}`, payload)
    return response.data
  },

  disconnectConnection: async (id: number): Promise<void> => {
    await apiClient.post(`/connections/${id}/disconnect`)
  },

  reconnectConnection: async (id: number): Promise<void> => {
    await apiClient.post(`/connections/${id}/reconnect`)
  },

  deleteConnection: async (id: number): Promise<void> => {
    await apiClient.delete(`/connections/${id}`)
  }
}
