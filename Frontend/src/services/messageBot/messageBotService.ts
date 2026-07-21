import { apiClient } from '../apiClient'
import type { MessageBot, CreateMessageBotRequest, UpdateMessageBotRequest } from '../../types/messageBot'

export interface MessageBotsPagedResponse {
  items: MessageBot[]
  totalCount: number
  page: number
  pageSize: number
}

const getApiErrorMessage = (error: unknown): string => {
  const err = error as {
    response?: {
      data?: {
        message?: string
        errors?: string[]
      }
    }
    message?: string
  }

  const data = err.response?.data
  if (Array.isArray(data?.errors) && data.errors.length > 0) {
    return data.errors.join(', ')
  }

  return data?.message || err.message || 'Operation failed.'
}

export const messageBotService = {
  getBots: async (params: {
    page?: number
    pageSize?: number
    relationType?: string
    isActive?: boolean
    search?: string
    sortBy?: string
    sortDescending?: boolean
  }): Promise<MessageBotsPagedResponse> => {
    try {
      const response = await apiClient.get('/MessageBots', { params })
      return response.data?.data || { items: [], totalCount: 0, page: 1, pageSize: 10 }
    } catch (error) {
      return { items: [], totalCount: 0, page: 1, pageSize: 10 }
    }
  },

  getBotById: async (id: number): Promise<MessageBot | null> => {
    try {
      const response = await apiClient.get(`/MessageBots/${id}`)
      return response.data?.data || null
    } catch (error) {
      return null
    }
  },

  createBot: async (data: CreateMessageBotRequest): Promise<MessageBot> => {
    try {
      const response = await apiClient.post('/MessageBots', data)
      return response.data?.data
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  updateBot: async (id: number, data: UpdateMessageBotRequest): Promise<MessageBot> => {
    try {
      const response = await apiClient.put(`/MessageBots/${id}`, data)
      return response.data?.data
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  deleteBot: async (id: number): Promise<boolean> => {
    try {
      const response = await apiClient.delete(`/MessageBots/${id}`)
      return response.data?.success || false
    } catch (error) {
      return false
    }
  },

  cloneBot: async (id: number): Promise<MessageBot> => {
    try {
      const response = await apiClient.post(`/MessageBots/clone/${id}`)
      return response.data?.data
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  toggleBotActive: async (id: number): Promise<MessageBot | null> => {
    try {
      const response = await apiClient.patch(`/MessageBots/toggle/${id}`)
      return response.data?.data || null
    } catch (error) {
      return null
    }
  }
}

export default messageBotService
