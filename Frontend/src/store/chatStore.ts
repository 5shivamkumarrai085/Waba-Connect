// src/store/chatStore.ts
import { create } from 'zustand'
import { chatService } from '../services/chat/chatService'
import type { ChatAccount, Conversation, Message } from '../types/chat'

interface ChatStoreState {
  accounts: ChatAccount[]
  conversations: Conversation[]
  activeConversationId: number | null
  messages: Message[]
  isLoading: boolean
  isSending: boolean
  fromNumber: string
  conversationsFilter: string
  sidebarSearchQuery: string

  loadAccounts: () => Promise<void>
  loadConversations: () => Promise<void>
  refreshActiveMessages: () => Promise<void>
  selectConversation: (id: number | null) => Promise<void>
  sendMessage: (text: string) => Promise<void>
  setFromNumber: (fromNumber: string) => void
  setConversationsFilter: (filter: string) => void
  setSidebarSearchQuery: (query: string) => void
}

export const useChatStore = create<ChatStoreState>((set, get) => ({
  accounts: [],
  conversations: [],
  activeConversationId: null,
  messages: [],
  isLoading: false,
  isSending: false,
  fromNumber: '',
  conversationsFilter: 'All Chats',
  sidebarSearchQuery: '',

  loadAccounts: async () => {
    try {
      const accounts = await chatService.getAccounts()
      set((state) => ({
        accounts,
        fromNumber: state.fromNumber || accounts[0]?.phoneNumberId || ''
      }))
    } catch (err) {
      console.error('Error loading chat accounts:', err)
    }
  },

  loadConversations: async () => {
    const { sidebarSearchQuery, conversationsFilter } = get()
    set({ isLoading: true })
    try {
      const list = await chatService.getConversations(sidebarSearchQuery, conversationsFilter)
      set({ conversations: list })
    } catch (err) {
      console.error('Error loading conversations:', err)
    } finally {
      set({ isLoading: false })
    }
  },

  refreshActiveMessages: async () => {
    const { activeConversationId, isSending } = get()
    if (!activeConversationId) return
    if (isSending) return

    try {
      const [messages, conversations] = await Promise.all([
        chatService.getMessages(activeConversationId),
        chatService.getConversations(get().sidebarSearchQuery, get().conversationsFilter)
      ])
      set({ messages, conversations })
    } catch (err) {
      console.error('Error refreshing chat:', err)
    }
  },

  selectConversation: async (id) => {
    set({ activeConversationId: id })
    if (!id) {
      set({ messages: [] })
      return
    }

    set({ isLoading: true })
    try {
      const msgs = await chatService.getMessages(id)
      set({ messages: msgs })

      set((state) => ({
        conversations: state.conversations.map(c =>
          c.id === id ? { ...c, unreadCount: 0 } : c
        )
      }))
    } catch (err) {
      console.error('Error loading messages:', err)
    } finally {
      set({ isLoading: false })
    }
  },

  sendMessage: async (text) => {
    const { activeConversationId, fromNumber } = get()
    if (!activeConversationId) return

    const tempId = -Date.now()
    const tempMessage: Message = {
      id: tempId,
      type: 'outgoing',
      text,
      time: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
      createdAt: new Date().toISOString(),
      status: 'sending',
      isTemplate: false
    }

    set({ isSending: true })
    set((state) => ({
      messages: [...state.messages, tempMessage],
      conversations: state.conversations.map((conversation) =>
        conversation.id === activeConversationId
          ? { ...conversation, lastMessage: text, lastMessageTime: 'Now' }
          : conversation
      )
    }))

    try {
      const newMsg = await chatService.sendMessage(activeConversationId, text, fromNumber || undefined)
      if (newMsg) {
        set((state) => ({
          messages: upsertMessage(state.messages, newMsg, tempId)
        }))
      }

      const list = await chatService.getConversations(get().sidebarSearchQuery, get().conversationsFilter)
      set({ conversations: list })
    } catch (err) {
      console.error('Error sending message:', err)
      const errorMessage = err instanceof Error ? err.message : 'Failed to send message.'
      const failedMessage: Message = {
        id: tempId,
        type: 'outgoing',
        text,
        time: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
        createdAt: new Date().toISOString(),
        status: 'failed',
        isTemplate: false,
        errorMessage
      }

      set((state) => ({
        messages: state.messages.map((message) => (
          message.id === tempId ? failedMessage : message
        )),
        conversations: state.conversations.map((conversation) =>
          conversation.id === activeConversationId
            ? { ...conversation, lastMessage: text, lastMessageTime: 'Now' }
            : conversation
        )
      }))
    } finally {
      set({ isSending: false })
    }
  },

  setFromNumber: (fromNumber) => set({ fromNumber }),
  setConversationsFilter: (conversationsFilter) => set({ conversationsFilter }),
  setSidebarSearchQuery: (sidebarSearchQuery) => set({ sidebarSearchQuery })
}))

export default useChatStore

const upsertMessage = (messages: Message[], newMessage: Message, tempId?: number) => {
  const withoutTemp = typeof tempId === 'number'
    ? messages.filter((message) => message.id !== tempId)
    : messages

  const existingIndex = withoutTemp.findIndex((message) => message.id === newMessage.id)
  if (existingIndex === -1) {
    return [...withoutTemp, newMessage]
  }

  return withoutTemp.map((message, index) => (
    index === existingIndex ? newMessage : message
  ))
}
