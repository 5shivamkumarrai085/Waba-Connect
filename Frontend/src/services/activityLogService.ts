import { apiClient } from './apiClient'
import type {
  LoginSuccessModel,
  LoginErrorModel,
  LoginAttemptPage,
  AuditLogPage,
  AuditLogFilters,
  AuditFilterOptions
} from '../types/reporting'

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

/**
 * Both sign-in endpoints return the same paged envelope and differ only in row shape, so they
 * share one fetch rather than two near-identical copies that have to be kept in step.
 */
const fetchLoginPage = async <T>(
  path: string,
  searchQuery: string | undefined,
  timeFilter: string | undefined,
  page: number,
  pageSize: number
): Promise<LoginAttemptPage<T>> => {
  const empty: LoginAttemptPage<T> = { items: [], totalCount: 0, page, pageSize, totalPages: 0 }
  try {
    const response = await apiClient.get(path, {
      params: { search: searchQuery || undefined, filter: timeFilter, page, pageSize },
      headers: timeZoneHeader()
    })
    return response.data?.data || empty
  } catch (error) {
    return empty
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
  getLoginSuccesses: async (
    searchQuery: string | undefined,
    timeFilter: string | undefined,
    page: number,
    pageSize: number
  ): Promise<LoginAttemptPage<LoginSuccessModel>> =>
    fetchLoginPage<LoginSuccessModel>('/Activity/login-successes', searchQuery, timeFilter, page, pageSize),

  getLoginErrors: async (
    searchQuery: string | undefined,
    timeFilter: string | undefined,
    page: number,
    pageSize: number
  ): Promise<LoginAttemptPage<LoginErrorModel>> =>
    fetchLoginPage<LoginErrorModel>('/Activity/login-errors', searchQuery, timeFilter, page, pageSize),

  /**
   * Audit events, filtered and paged server-side.
   *
   * Undefined parameters are dropped by axios rather than sent empty, so an unset filter is
   * genuinely absent from the query string instead of arriving as "" and matching nothing.
   */
  getAuditLogs: async (
    searchQuery: string | undefined,
    timeFilter: string | undefined,
    filters: AuditLogFilters,
    page: number,
    pageSize: number
  ): Promise<AuditLogPage> => {
    const empty: AuditLogPage = { items: [], totalCount: 0, page, pageSize, totalPages: 0 }
    try {
      const response = await apiClient.get('/Activity/audit-logs', {
        params: {
          search: searchQuery || undefined,
          filter: timeFilter,
          from: filters.from || undefined,
          to: filters.to || undefined,
          module: filters.module || undefined,
          action: filters.action || undefined,
          userId: filters.userId || undefined,
          status: filters.status || undefined,
          page,
          pageSize
        },
        headers: timeZoneHeader()
      })
      return response.data?.data || empty
    } catch (error) {
      return empty
    }
  },

  getAuditFilterOptions: async (): Promise<AuditFilterOptions> => {
    const empty: AuditFilterOptions = { modules: [], actions: [], statuses: [], users: [] }
    try {
      const response = await apiClient.get('/Activity/audit-filters')
      return response.data?.data || empty
    } catch (error) {
      return empty
    }
  }
}
