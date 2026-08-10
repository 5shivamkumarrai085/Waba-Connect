// src/store/chatStore.ts
import { create } from 'zustand'
import { toast } from 'react-hot-toast'
import { chatService } from '../services/chat/chatService'
import type { ChatAccount, Conversation, Message } from '../types/chat'

const SELECTED_CONNECTION_STORAGE_KEY = 'chat_selected_connection_id'

const readPersistedConnectionId = (): number | null => {
  try {
    const raw = window.localStorage.getItem(SELECTED_CONNECTION_STORAGE_KEY)
    if (!raw) return null
    const parsed = Number(raw)
    return Number.isFinite(parsed) ? parsed : null
  } catch {
    return null
  }
}

const persistConnectionId = (id: number | null) => {
  try {
    if (id === null) {
      window.localStorage.removeItem(SELECTED_CONNECTION_STORAGE_KEY)
    } else {
      window.localStorage.setItem(SELECTED_CONNECTION_STORAGE_KEY, String(id))
    }
  } catch {
    // localStorage unavailable — ignore, falls back to in-memory only
  }
}

interface ChatStoreState {
  accounts: ChatAccount[]
  conversations: Conversation[]
  activeConversationId: number | null
  selectedConnectionId: number | null
  messages: Message[]
  messagesCache: Record<number, Message[]>
  isLoading: boolean
  isLoadingConversations: boolean
  isSending: boolean
  isRefreshing: boolean
  fromNumber: string
  conversationsFilter: string
  sidebarSearchQuery: string
  activeAbortController: AbortController | null

  setSelectedConnectionId: (id: number | null) => void
  loadAccounts: () => Promise<void>
  loadConversations: () => Promise<void>
  refreshActiveMessages: () => Promise<void>
  selectConversation: (id: number | null) => Promise<void>
  sendMessage: (text: string, mediaUrl?: string, mediaType?: string, mediaFileName?: string) => Promise<void>
  deleteActiveConversation: () => Promise<void>
  deleteMessages: (messageIds: number[]) => Promise<void>
  setFromNumber: (fromNumber: string) => void
  setConversationsFilter: (filter: string) => void
  setSidebarSearchQuery: (query: string) => void
}

export const useChatStore = create<ChatStoreState>((set, get) => ({
  accounts: [],
  conversations: [],
  activeConversationId: null,
  selectedConnectionId: readPersistedConnectionId(),
  messages: [],
  messagesCache: {},
  activeAbortController: null,
  isLoading: false,
  isLoadingConversations: false,
  isSending: false,
  isRefreshing: false,
  fromNumber: '',
  conversationsFilter: 'All Chats',
  sidebarSearchQuery: '',

  setSelectedConnectionId: (id) => {
    persistConnectionId(id)
    set({
      selectedConnectionId: id,
      conversations: [],
      activeConversationId: null,
      messages: []
    })
    get().loadConversations()
    get().loadAccounts()
  },

  loadAccounts: async () => {
    try {
      const accounts = await chatService.getAccounts(get().selectedConnectionId || undefined)
      set((state) => ({
        accounts,
        fromNumber: state.fromNumber || accounts[0]?.phoneNumberId || ''
      }))
    } catch (err) {
    }
  },

  loadConversations: async () => {
    const { sidebarSearchQuery, conversationsFilter, selectedConnectionId, isLoadingConversations } = get()
    if (isLoadingConversations) return
    // No connection resolved yet (fresh session before auto-select runs, or persisted id not restored) —
    // skip rather than hitting the unfiltered endpoint, which would return every connection's chats mixed together.
    if (!selectedConnectionId) return

    set({ isLoadingConversations: true })
    try {
      const list = await chatService.getConversations(sidebarSearchQuery, conversationsFilter, selectedConnectionId || undefined)
      set({ conversations: list })
    } catch {
      // Error handled silently
    } finally {
      set({ isLoadingConversations: false })
    }
  },

  refreshActiveMessages: async () => {
    const { activeConversationId, isSending, isRefreshing } = get()
    if (!activeConversationId || isSending || isRefreshing) return

    set({ isRefreshing: true })
    const fetchId = activeConversationId

    try {
      const serverMessages = await chatService.getMessages(fetchId)
      
      if (get().activeConversationId === fetchId) {
        set((state) => {
          // Preserve optimistic (temp) messages that haven't been confirmed by server yet
          const tempMessages = state.messages.filter(m => m.id < 0)
          const mergedMessages = [...serverMessages, ...tempMessages]
          return {
            messages: mergedMessages,
            messagesCache: {
              ...state.messagesCache,
              [fetchId]: mergedMessages
            }
          }
        })
      }
    } catch {
      // Error handled silently
    } finally {
      set({ isRefreshing: false })
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

        // Tell the server once, here. Fetching messages no longer clears the unread count as a
        // side effect, which is what let the message poll become a pure read. Fire-and-forget:
        // the badge is already cleared locally and a failure only means it reappears later.
        void chatService.markConversationRead(id).catch(() => {})
      }
    } catch (err: any) {
      if (err.name === 'AbortError' || err.message === 'canceled') {
        return
      }
    } finally {
      if (get().activeConversationId === id) {
        set({ isLoading: false })
      }
    }
  },

  sendMessage: async (text, mediaUrl, mediaType, mediaFileName) => {
    const { activeConversationId, fromNumber, conversations } = get()
    if (!activeConversationId) return

    const activeConversation = conversations.find((c) => c.id === activeConversationId)

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
        mediaFileName,
        activeConversation?.connectionId || get().selectedConnectionId || undefined
      )
      if (newMsg) {
        set((state) => {
          const sentMsg = { ...newMsg, status: 'sent' as const }
          const updatedMsgs = upsertMessage(state.messages, sentMsg, tempId)
          return {
            messages: updatedMsgs,
            messagesCache: {
              ...state.messagesCache,
              [activeConversationId]: updatedMsgs
            }
          }
        })
      }

      const list = await chatService.getConversations(get().sidebarSearchQuery, get().conversationsFilter, get().selectedConnectionId || undefined)
      set({ conversations: list })
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to send message.'
      toast.error(errorMessage)
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
  },

  deleteMessages: async (messageIds) => {
    const { activeConversationId } = get()
    if (!activeConversationId || messageIds.length === 0) return

    await chatService.deleteMessages(activeConversationId, messageIds)

    // Dropped locally rather than refetched: the server has already soft-deleted them, and a
    // refetch would make the bubbles linger for a round-trip after the user confirmed.
    const removed = new Set(messageIds)
    set((state) => ({ messages: state.messages.filter((m) => !removed.has(m.id)) }))
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
