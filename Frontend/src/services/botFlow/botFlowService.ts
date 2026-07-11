import { apiClient } from '../apiClient'
import type { BotFlow } from '../../types/botFlow'

export const botFlowService = {
  getBotFlows: async (params?: {
    page?: number
    pageSize?: number
    isActive?: boolean
    search?: string
  }) => {
    const response = await apiClient.get('/BotFlows', { params })
    return response.data?.data
  },

  getBotFlowById: async (id: number): Promise<BotFlow> => {
    const response = await apiClient.get(`/BotFlows/${id}`)
    return response.data?.data
  },

  createBotFlow: async (data: any): Promise<BotFlow> => {
    const response = await apiClient.post('/BotFlows', data)
    return response.data?.data
  },

  updateBotFlow: async (id: number, data: any): Promise<BotFlow> => {
    const response = await apiClient.put(`/BotFlows/${id}`, data)
    return response.data?.data
  },

  deleteBotFlow: async (id: number): Promise<boolean> => {
    const response = await apiClient.delete(`/BotFlows/${id}`)
    return response.data?.success || false
  },

  toggleBotFlowActive: async (id: number): Promise<BotFlow> => {
    const response = await apiClient.patch(`/BotFlows/toggle/${id}`)
    return response.data?.data
  }
}
