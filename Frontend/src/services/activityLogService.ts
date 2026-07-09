import { apiClient } from './apiClient'
import type { LoginSuccessModel, AuditLogModel } from '../types/reporting'

export const activityLogService = {
  getActivityMetrics: async (timeFilter: string): Promise<any[]> => {
    try {
      const response = await apiClient.get(`/Activity/metrics?filter=${timeFilter}`)
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getLoginSuccesses: async (searchQuery?: string): Promise<LoginSuccessModel[]> => {
    try {
      const response = await apiClient.get('/Activity/login-successes', { params: { search: searchQuery } })
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getLoginErrors: async (searchQuery?: string): Promise<any[]> => {
    try {
      const response = await apiClient.get('/Activity/login-errors', { params: { search: searchQuery } })
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getAuditLogs: async (searchQuery?: string): Promise<AuditLogModel[]> => {
    try {
      const response = await apiClient.get('/Activity/audit-logs', { params: { search: searchQuery } })
      return response.data?.data || []
    } catch (error) {
      return []
    }
  }
}
