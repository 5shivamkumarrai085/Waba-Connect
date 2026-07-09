import { apiClient } from '../apiClient'
import type { Conversation, Message } from '../../types/chat'

export const chatService = {
  getConversations: async (): Promise<Conversation[]> => {
    try {
      const response = await apiClient.get('/Chat/conversations')
      return response.data?.data || []
    } catch (error) {
      console.error('Error fetching conversations:', error)
      return []
    }
  },

  getConversation: async (id: string): Promise<Conversation | null> => {
    try {
      const response = await apiClient.get(`/Chat/conversations/${id}`)
      return response.data?.data || null
    } catch (error) {
      console.error('Error fetching conversation:', error)
      return null
    }
  },

  getMessages: async (convId: string): Promise<Message[]> => {
    try {
      const response = await apiClient.get(`/Chat/conversations/${convId}/messages`)
      return response.data?.data || []
    } catch (error) {
      console.error('Error fetching messages:', error)
      return []
    }
  },

  sendMessage: async (convId: string, text: string): Promise<Message | null> => {
    try {
      const response = await apiClient.post(`/Chat/conversations/${convId}/messages`, { text })
      return response.data?.data || null
    } catch (error) {
      console.error('Error sending message:', error)
      return null
    }
  }
}
export default chatService
