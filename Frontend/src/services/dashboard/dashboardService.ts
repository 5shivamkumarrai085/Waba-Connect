import { apiClient } from '../apiClient'

export const dashboardService = {
  getSummary: async (timeFilter: string = 'all') => {
    try {
      const response = await apiClient.get(`/Dashboard/summary?timeFilter=${timeFilter}`)
      return response.data
    } catch (e: any) {
      return null
    }
  }
}
