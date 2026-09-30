import { apiClient } from '../apiClient'

export const dashboardService = {
  /**
   * The dashboard summary for a period. Errors are thrown, not swallowed: a failed load used to
   * come back as null and render as a dashboard full of zeros, which reads as real data.
   */
  getSummary: async (timeFilter: string = 'all') => {
    const response = await apiClient.get('/Dashboard/summary', { params: { timeFilter } })
    return response.data
  }
}
