import { apiClient } from '../apiClient'
import type { TemplateBot } from '../../types/templateBot'

export const templateBotService = {
  getTemplateBots: async (params?: {
    page?: number
    pageSize?: number
    relationType?: string
    isActive?: boolean
    search?: string
    sortBy?: string
    sortDescending?: boolean
  }) => {
    const response = await apiClient.get('/TemplateBots', { params })
    return response.data?.data
  },

  getTemplateBotById: async (id: number): Promise<TemplateBot> => {
    const response = await apiClient.get(`/TemplateBots/${id}`)
    return response.data?.data
  },

  createTemplateBot: async (data: any): Promise<TemplateBot> => {
    const response = await apiClient.post('/TemplateBots', data)
    return response.data?.data
  },

  updateTemplateBot: async (id: number, data: any): Promise<TemplateBot> => {
    const response = await apiClient.put(`/TemplateBots/${id}`, data)
    return response.data?.data
  },

  deleteTemplateBot: async (id: number): Promise<boolean> => {
    const response = await apiClient.delete(`/TemplateBots/${id}`)
    return response.data?.success || false
  },

  cloneTemplateBot: async (id: number): Promise<TemplateBot> => {
    const response = await apiClient.post(`/TemplateBots/clone/${id}`)
    return response.data?.data
  },

  toggleTemplateBotActive: async (id: number): Promise<TemplateBot> => {
    const response = await apiClient.patch(`/TemplateBots/toggle/${id}`)
    return response.data?.data
  }
}
