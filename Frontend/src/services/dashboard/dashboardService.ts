import { apiClient } from '../apiClient'

export const dashboardService = {
  getSummary: async () => {
    try {
      const response = await apiClient.get('/Dashboard/summary')
      return response.data
    } catch (e: any) {
      console.error("Dashboard summary error:", e)
      return null
    }
  }
}
