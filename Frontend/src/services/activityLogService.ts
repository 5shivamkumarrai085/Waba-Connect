import { apiClient } from './apiClient'
import type { LoginSuccessModel, LoginErrorModel, AuditLogModel } from '../types/reporting'

/**
 * The backend cuts "today"/"yesterday" on the caller's midnight, not UTC's — otherwise an IST
 * user's "today" would start at 05:30 and quietly drop the morning. It needs to be told which
 * zone that is; the browser is the only thing that knows.
 */
const timeZoneHeader = () => {
  try {
    return { 'X-Timezone': Intl.DateTimeFormat().resolvedOptions().timeZone }
  } catch {
    return {}
  }
}

export const activityLogService = {
  getActivityMetrics: async (timeFilter: string): Promise<any[]> => {
    try {
      const response = await apiClient.get('/Activity/metrics', {
        params: { filter: timeFilter },
        headers: timeZoneHeader()
      })
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  // The list endpoints take the time filter too. Without it the KPI cards moved with the
  // filter while the tables underneath kept showing everything, which read as the cards
  // being wrong.
  getLoginSuccesses: async (searchQuery?: string, timeFilter?: string): Promise<LoginSuccessModel[]> => {
    try {
      const response = await apiClient.get('/Activity/login-successes', {
        params: { search: searchQuery, filter: timeFilter },
        headers: timeZoneHeader()
      })
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getLoginErrors: async (searchQuery?: string, timeFilter?: string): Promise<LoginErrorModel[]> => {
    try {
      const response = await apiClient.get('/Activity/login-errors', {
        params: { search: searchQuery, filter: timeFilter },
        headers: timeZoneHeader()
      })
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getAuditLogs: async (searchQuery?: string, timeFilter?: string): Promise<AuditLogModel[]> => {
    try {
      const response = await apiClient.get('/Activity/audit-logs', {
        params: { search: searchQuery, filter: timeFilter },
        headers: timeZoneHeader()
      })
      return response.data?.data || []
    } catch (error) {
      return []
    }
  }
}
