// src/store/chatStore.ts
import { create } from 'zustand'
import { toast } from 'react-hot-toast'
import { chatService } from '../services/chat/chatService'
import type { ChatAccount, Conversation, Message } from '../types/chat'

const SELECTED_CONNECTION_STORAGE_KEY = 'chat_selected_connection_id'
const SELECTED_CHANNEL_STORAGE_KEY = 'chat_channel_filter'

/**
 * The "no channel filter" selection.
 *
 * A sentinel rather than null so the picker renders from one list without a special case for the
 * default, and so the value is readable wherever it appears. It is deliberately not a
 * MessageChannel: the API takes a real channel name or nothing at all.
 */
export const ALL_CHANNELS = 'All Channels'

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

const readPersistedChannelFilter = (): string => {
  try {
    return window.localStorage.getItem(SELECTED_CHANNEL_STORAGE_KEY) ?? ALL_CHANNELS
  } catch {
    return ALL_CHANNELS
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

const persistChannelFilter = (channel: string) => {
  try {
    window.localStorage.setItem(SELECTED_CHANNEL_STORAGE_KEY, channel)
  } catch {}
}

interface ChatStoreState {
  accounts: ChatAccount[]
  conversations: Conversation[]
  activeConversationId: number | null
  /**
   * The selected connection for single-channel mode (WhatsApp or Email).
   * NULL when ALL_CHANNELS is active — that mode uses no connection filter.
   */
  selectedConnectionId: number | null
  messages: Message[]
  messagesCache: Record<number, Message[]>
  isLoading: boolean
  isLoadingConversations: boolean
  isSending: boolean
  isRefreshing: boolean
  fromNumber: string
  conversationsFilter: string
  /** ALL_CHANNELS, or a channel key the API understands ('WhatsApp' | 'Email'). */
  channelFilter: string
  sidebarSearchQuery: string
  activeAbortController: AbortController | null
  /** Sequence counter for loadConversations — only the latest response is applied. */
  _loadSeq: number

  setSelectedConnectionId: (id: number | null) => void
  setChannelFilter: (channel: string) => void
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
  // On ALL_CHANNELS start with null (no specific connection required)
  selectedConnectionId: readPersistedChannelFilter() === ALL_CHANNELS ? null : readPersistedConnectionId(),
  messages: [],
  messagesCache: {},
  activeAbortController: null,
  isLoading: false,
  isLoadingConversations: false,
  isSending: false,
  isRefreshing: false,
  fromNumber: '',
  conversationsFilter: 'All Chats',
  channelFilter: readPersistedChannelFilter(),
  sidebarSearchQuery: '',
  _loadSeq: 0,

  setSelectedConnectionId: (id) => {
    persistConnectionId(id)
    set({
      selectedConnectionId: id,
      // Do NOT clear conversations here when ALL_CHANNELS — this is only called for
      // single-channel connection changes
      conversations: [],
      activeConversationId: null,
      messages: []
    })
    get().loadConversations()
    get().loadAccounts()
  },

  /**
   * Changing channel filter.
   *
   * ALL_CHANNELS: keep no specific connection (null). The backend returns all channels.
   * WhatsApp/Email: clear to null then let the auto-select pick the right connection.
   *
   * We do NOT clear the conversation list immediately — we keep the old list until new data
   * arrives to prevent "flash of empty content".
   */
  setChannelFilter: (channel) => {
    if (get().channelFilter === channel) return

    persistChannelFilter(channel)
    persistConnectionId(null)

    set({
      channelFilter: channel,
      selectedConnectionId: null,
      // Keep old conversations visible until new data loads (no flash of empty)
      activeConversationId: null,
      messages: [],
      accounts: [],
      fromNumber: ''
    })

    // Immediately trigger a load for the new filter
    get().loadConversations()
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
    const { sidebarSearchQuery, conversationsFilter, selectedConnectionId, channelFilter } = get()

    // ALL_CHANNELS: load with no connectionId (backend returns all channels)
    // Single channel: load with connectionId if available, but DO NOT block if null
    // — null simply means "no connection filter yet", which is valid for ALL_CHANNELS
    const isAllChannels = channelFilter === ALL_CHANNELS

    // For single-channel mode: if we have no connection yet, don't load
    // (connection auto-select will fire and call loadConversations again)
    if (!isAllChannels && !selectedConnectionId) return

    // Increment sequence. Only the response for the LATEST sequence is applied.
    const seq = get()._loadSeq + 1
    set({ _loadSeq: seq, isLoadingConversations: true })

    try {
      const list = await chatService.getConversations(
        sidebarSearchQuery,
        conversationsFilter,
        // ALL_CHANNELS passes no connectionId — backend returns all
        isAllChannels ? undefined : (selectedConnectionId || undefined),
        // ALL_CHANNELS passes no channel — backend returns all channels
        isAllChannels ? undefined : channelFilter
      )

      set((state) => {
        if (state._loadSeq !== seq) return {}
        // Never replace a valid list with [] from a background poll error.
        // If the new list is empty but we had conversations before, keep the old ones
        // unless the user explicitly changed filters/search.
        if (list.length === 0 && state.conversations.length > 0 && !sidebarSearchQuery) {
          // Empty response during polling — might be transient. Keep existing.
          return {}
        }
        return { conversations: list }
      })
    } catch {
      // On error: keep existing conversations visible. Never show empty on error.
    } finally {
      set((state) => {
        if (state._loadSeq !== seq) return {}
        return { isLoadingConversations: false }
      })
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
          // De-duplicate by id
          const seen = new Set<number>()
          const deduped = [...serverMessages, ...tempMessages].filter(m => {
            if (seen.has(m.id)) return false
            seen.add(m.id)
            return true
          })
          return {
            messages: deduped,
            messagesCache: {
              ...state.messagesCache,
              [fetchId]: deduped
            }
          }
        })
      }
    } catch {
      // Error handled silently — keep existing messages
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

      // Refresh conversation list — use loadConversations which handles ALL_CHANNELS correctly
      get().loadConversations()
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
