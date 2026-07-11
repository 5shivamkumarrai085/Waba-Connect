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
  getAccounts: async (): Promise<ChatAccount[]> => {
    try {
      const response = await apiClient.get('/Chat/accounts')
      return response.data?.data || []
    } catch (error) {
      console.error('Error fetching chat accounts:', error)
      return []
    }
  },

  getConversations: async (search?: string, filter?: string): Promise<Conversation[]> => {
    try {
      const response = await apiClient.get('/Chat/conversations', {
        params: {
          search: search || undefined,
          filter: filter || undefined
        }
      })
      return response.data?.data || []
    } catch (error) {
      console.error('Error fetching conversations:', error)
      return []
    }
  },

  getConversation: async (id: number): Promise<Conversation | null> => {
    try {
      const response = await apiClient.get(`/Chat/conversations/${id}`)
      return response.data?.data || null
    } catch (error) {
      console.error('Error fetching conversation:', error)
      return null
    }
  },

  getMessages: async (convId: number): Promise<Message[]> => {
    try {
      const response = await apiClient.get(`/Chat/conversations/${convId}/messages`)
      return response.data?.data || []
    } catch (error) {
      console.error('Error fetching messages:', error)
      return []
    }
  },

  sendMessage: async (convId: number, text: string, fromPhoneNumberId?: string): Promise<Message | null> => {
    try {
      const response = await apiClient.post(`/Chat/conversations/${convId}/messages`, {
        text,
        fromPhoneNumberId
      })
      return response.data?.data || null
    } catch (error) {
      console.error('Error sending message:', error)
      throw new Error(getApiErrorMessage(error))
    }
  }
}
export default chatService
