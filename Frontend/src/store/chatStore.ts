// src/store/chatStore.ts
import { create } from 'zustand'
import { chatService } from '../services/chat/chatService'
import type { ChatAccount, Conversation, Message } from '../types/chat'

interface ChatStoreState {
  accounts: ChatAccount[]
  conversations: Conversation[]
  activeConversationId: number | null
  messages: Message[]
  messagesCache: Record<number, Message[]>
  isLoading: boolean
  isSending: boolean
  fromNumber: string
  conversationsFilter: string
  sidebarSearchQuery: string
  activeAbortController: AbortController | null

  loadAccounts: () => Promise<void>
  loadConversations: () => Promise<void>
  refreshActiveMessages: () => Promise<void>
  selectConversation: (id: number | null) => Promise<void>
  sendMessage: (text: string, mediaUrl?: string, mediaType?: string, mediaFileName?: string) => Promise<void>
  deleteActiveConversation: () => Promise<void>
  setFromNumber: (fromNumber: string) => void
  setConversationsFilter: (filter: string) => void
  setSidebarSearchQuery: (query: string) => void
}

export const useChatStore = create<ChatStoreState>((set, get) => ({
  accounts: [],
  conversations: [],
  activeConversationId: null,
  messages: [],
  messagesCache: {},
  activeAbortController: null,
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

    const fetchId = activeConversationId

    try {
      const [messages, conversations] = await Promise.all([
        chatService.getMessages(fetchId),
        chatService.getConversations(get().sidebarSearchQuery, get().conversationsFilter)
      ])
      
      if (get().activeConversationId === fetchId) {
        set((state) => ({
          messages,
          messagesCache: {
            ...state.messagesCache,
            [fetchId]: messages
          },
          conversations
        }))
      }
    } catch (err) {
      console.error('Error refreshing chat:', err)
    }
  },

  selectConversation: async (id) => {
    const previousController = get().activeAbortController
    if (previousController) {
      previousController.abort()
    }

    const controller = new AbortController()

    set({ 
      activeConversationId: id, 
      messages: [],
      isLoading: id !== null,
      activeAbortController: controller
    })

    if (!id) return

    try {
      const msgs = await chatService.getMessages(id, controller.signal)
      if (get().activeConversationId === id) {
        set((state) => ({
          messages: msgs,
          messagesCache: {
            ...state.messagesCache,
            [id]: msgs
          },
          conversations: state.conversations.map(c =>
            c.id === id ? { ...c, unreadCount: 0 } : c
          )
        }))
      }
    } catch (err: any) {
      if (err.name === 'AbortError' || err.message === 'canceled') {
        return
      }
      console.error('Error loading messages:', err)
    } finally {
      if (get().activeConversationId === id) {
        set({ isLoading: false })
      }
    }
  },

  sendMessage: async (text, mediaUrl, mediaType, mediaFileName) => {
    const { activeConversationId, fromNumber } = get()
    if (!activeConversationId) return

    const tempId = -Date.now()
    const displayBody = mediaUrl 
      ? (text ? `[Attachment: ${mediaFileName || 'file'}]\n\n${text}` : `[Attachment: ${mediaFileName || 'file'}]`)
      : text;

    const tempMessage: Message = {
      id: tempId,
      type: 'outgoing',
      text: displayBody,
      time: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
      createdAt: new Date().toISOString(),
      status: 'sending',
      isTemplate: false,
      mediaUrl,
      mediaType,
      mediaFileName
    }

    set({ isSending: true })
    set((state) => {
      const updatedMsgs = [...state.messages, tempMessage]
      return {
        messages: updatedMsgs,
        messagesCache: {
          ...state.messagesCache,
          [activeConversationId]: updatedMsgs
        },
        conversations: state.conversations.map((conversation) =>
          conversation.id === activeConversationId
            ? { ...conversation, lastMessage: displayBody, lastMessageTime: 'Now' }
            : conversation
        )
      }
    })

    try {
      const newMsg = await chatService.sendMessage(
        activeConversationId,
        text,
        fromNumber || undefined,
        mediaUrl,
        mediaType,
        mediaFileName
      )
      if (newMsg) {
        set((state) => {
          const updatedMsgs = upsertMessage(state.messages, newMsg, tempId)
          return {
            messages: updatedMsgs,
            messagesCache: {
              ...state.messagesCache,
              [activeConversationId]: updatedMsgs
            }
          }
        })
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

      set((state) => {
        const updatedMsgs = state.messages.map((message) => (
          message.id === tempId ? failedMessage : message
        ))
        return {
          messages: updatedMsgs,
          messagesCache: {
            ...state.messagesCache,
            [activeConversationId]: updatedMsgs
          },
          conversations: state.conversations.map((conversation) =>
            conversation.id === activeConversationId
              ? { ...conversation, lastMessage: text, lastMessageTime: 'Now' }
              : conversation
          )
        }
      })
    } finally {
      set({ isSending: false })
    }
  },

  setFromNumber: (fromNumber) => set({ fromNumber }),
  setConversationsFilter: (conversationsFilter) => set({ conversationsFilter }),
  setSidebarSearchQuery: (sidebarSearchQuery) => set({ sidebarSearchQuery }),

  deleteActiveConversation: async () => {
    const { activeConversationId } = get()
    if (!activeConversationId) return
    await chatService.deleteConversation(activeConversationId)
    set((state) => ({
      activeConversationId: null,
      messages: [],
      conversations: state.conversations.filter(c => c.id !== activeConversationId)
    }))
  }
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
