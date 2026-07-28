import { apiClient } from '../apiClient'
import type { ChatAccount, Conversation, Message } from '../../types/chat'

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

  return data?.message || err.message || 'Failed to send message.'
}

export const chatService = {
  getAccounts: async (connectionId?: number): Promise<ChatAccount[]> => {
    try {
      const response = await apiClient.get('/Chat/accounts', {
        params: { connectionId: connectionId || undefined }
      })
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getConversations: async (search?: string, filter?: string, connectionId?: number): Promise<Conversation[]> => {
    try {
      const response = await apiClient.get('/Chat/conversations', {
        params: {
          search: search || undefined,
          filter: filter || undefined,
          connectionId: connectionId || undefined
        }
      })
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getConversation: async (id: number): Promise<Conversation | null> => {
    try {
      const response = await apiClient.get(`/Chat/conversations/${id}`)
      return response.data?.data || null
    } catch (error) {
      return null
    }
  },

  getMessages: async (convId: number, signal?: AbortSignal): Promise<Message[]> => {
    try {
      const response = await apiClient.get(`/Chat/conversations/${convId}/messages`, { signal })
      return response.data?.data || []
    } catch (error) {
      if (error && (error as any).name === 'CanceledError') {
        return []
      }
      return []
    }
  },

  sendMessage: async (
    convId: number,
    text: string,
    fromPhoneNumberId?: string,
    mediaUrl?: string,
    mediaType?: string,
    mediaFileName?: string,
    connectionId?: number
  ): Promise<Message | null> => {
    try {
      const response = await apiClient.post(`/Chat/conversations/${convId}/messages`, {
        text,
        fromPhoneNumberId,
        mediaUrl,
        mediaType,
        mediaFileName,
        connectionId
      })
      return response.data?.data || null
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  sendTemplateMessage: async (contactId: number, templateId: number, variables: Record<string, string>, connectionId?: number): Promise<Message | null> => {
    try {
      const response = await apiClient.post('/Chat/send-template-to-contact', {
        contactId,
        templateId,
        variables,
        connectionId
      })
      return response.data?.data || null
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  deleteConversation: async (id: number): Promise<void> => {
    try {
      await apiClient.delete(`/Chat/conversations/${id}`)
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  }
}
export default chatService
