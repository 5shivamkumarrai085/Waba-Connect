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

/** One keyset page; paging metadata travels in response headers so the body stays a list. */
export interface ChatPage<T> {
  items: T[]
  nextCursor: string | null
  hasMore: boolean
}

export const CONVERSATION_PAGE_SIZE = 30
export const MESSAGE_PAGE_SIZE = 50

const readChatPage = <T,>(response: { data?: any; headers?: any }): ChatPage<T> => ({
  items: (response.data?.data ?? []) as T[],
  nextCursor: (response.headers?.['x-next-cursor'] as string | undefined) ?? null,
  hasMore: String(response.headers?.['x-has-more'] ?? 'false') === 'true'
})

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
   * One page of the inbox, newest activity first.
   *
   * @param channel 'WhatsApp' or 'Email'. Omitted means every channel.
   * @param cursor  The `nextCursor` of the previous page; omitted starts from the top.
   *
   * Throws on failure (rather than returning an empty list) so the caller can keep what is on
   * screen instead of wiping the inbox because of one failed request.
   */
  getConversations: async (
    search?: string,
    filter?: string,
    connectionId?: number,
    channel?: string,
    options: { cursor?: string; limit?: number; state?: string; assignee?: string } = {}
  ): Promise<ChatPage<Conversation>> => {
    const response = await apiClient.get('/Chat/conversations', {
      params: {
        search: search || undefined,
        filter: filter || undefined,
        connectionId: connectionId || undefined,
        channel: channel || undefined,
        cursor: options.cursor,
        limit: options.limit ?? CONVERSATION_PAGE_SIZE,
        state: options.state && options.state !== 'all' ? options.state : undefined,
        assignee: options.assignee || undefined
      }
    })
    return readChatPage<Conversation>(response)
  },

  /** Agents who can take conversations on a connection, with their open workload. */
  getAssignableAgents: async (connectionId?: number): Promise<AssignableAgent[]> => {
    const response = await apiClient.get('/Chat/assignable-agents', { params: { connectionId: connectionId || undefined } })
    return response.data?.data ?? []
  },

  /** Assigns a conversation; null unassigns it. */
  assignConversation: async (conversationId: number, userId: number | null): Promise<void> => {
    try {
      await apiClient.post(`/Chat/conversations/${conversationId}/assign`, { userId })
    } catch (error) {
      throw new Error((error as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Could not assign the conversation.')
    }
  },

  setConversationStatus: async (conversationId: number, status: 'Open' | 'Pending' | 'Resolved' | 'Closed'): Promise<void> => {
    try {
      await apiClient.post(`/Chat/conversations/${conversationId}/status`, { status })
    } catch (error) {
      throw new Error((error as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Could not change the status.')
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

  /**
   * Messages of a thread in chronological order: the newest page by default, the page older than
   * `beforeId` when scrolling back, or only what arrived after `afterId` for a live thread.
   * Throws on failure so a transient error never blanks an open conversation.
   */
  getMessages: async (
    convId: number,
    options: { beforeId?: number; afterId?: number; limit?: number; signal?: AbortSignal } = {}
  ): Promise<ChatPage<Message>> => {
    const response = await apiClient.get(`/Chat/conversations/${convId}/messages`, {
      params: {
        beforeId: options.beforeId,
        afterId: options.afterId,
        limit: options.limit ?? MESSAGE_PAGE_SIZE
      },
      signal: options.signal
    })
    return readChatPage<Message>(response)
  },

  sendMessage: async (
    convId: number,
    text: string,
    fromPhoneNumberId?: string,
    mediaUrl?: string,
    mediaType?: string,
    mediaFileName?: string,
    connectionId?: number,
    replyButtons?: string[]
  ): Promise<Message | null> => {
    try {
      const response = await apiClient.post(`/Chat/conversations/${convId}/messages`, {
        text,
        fromPhoneNumberId,
        mediaUrl,
        mediaType,
        mediaFileName,
        connectionId,
        replyButtons: replyButtons && replyButtons.length > 0 ? replyButtons : undefined
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

export interface AssignableAgent {
  id: number
  name: string
  email: string
  openConversations: number
}
