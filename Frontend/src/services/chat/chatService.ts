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

  /**
   * @param channel 'WhatsApp' or 'Email'. Omitted means every channel.
   *
   * Filtered on the server rather than in the component: the response is already scoped to one
   * connection, so a client-side channel predicate could only ever remove rows from a list that
   * never contained the other channel's to begin with.
   */
  getConversations: async (
    search?: string,
    filter?: string,
    connectionId?: number,
    channel?: string
  ): Promise<Conversation[]> => {
    try {
      const response = await apiClient.get('/Chat/conversations', {
        params: {
          search: search || undefined,
          filter: filter || undefined,
          connectionId: connectionId || undefined,
          channel: channel || undefined
        }
      })
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  /**
   * Sends one email from a thread — reply, reply-all or forward.
   *
   * The three differ only in recipients and subject, which the composer decides, so they share
   * one call. A refused send comes back as `success: false` with a reason rather than throwing:
   * "that address has unsubscribed" is something the composer shows, not something it catches.
   */
  sendEmailReply: async (
    conversationId: number,
    payload: {
      subject: string
      bodyHtml: string
      to: string[]
      cc?: string[]
      bcc?: string[]
      inReplyToMessageId?: number | null
      attachments?: Array<{ fileName: string; contentType: string; base64Data: string }>
    }
  ): Promise<{ success: boolean; message: string }> => {
    try {
      const res = await apiClient.post(`/Chat/conversations/${conversationId}/email-reply`, payload)
      return {
        success: res.data?.success ?? false,
        message: res.data?.message ?? 'Sent.'
      }
    } catch (err: any) {
      return {
        success: false,
        message: err?.response?.data?.message ?? 'Could not reach the server to send this email.'
      }
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

  /**
   * Clears a conversation's unread count. Called once when the user opens it — reading the
   * messages deliberately no longer does this, so polling for new messages stays a pure read.
   */
  markConversationRead: async (id: number): Promise<void> => {
    await apiClient.post(`/Chat/conversations/${id}/read`)
  },

  deleteConversation: async (id: number): Promise<void> => {
    try {
      await apiClient.delete(`/Chat/conversations/${id}`)
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  /**
   * Soft-deletes messages. POST rather than DELETE because the ids travel in a body — one
   * endpoint serves both the single right-click delete and the multi-select delete.
   */
  deleteMessages: async (conversationId: number, messageIds: number[]): Promise<void> => {
    try {
      await apiClient.post(`/Chat/conversations/${conversationId}/messages/delete`, { messageIds })
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  }
}
export default chatService
