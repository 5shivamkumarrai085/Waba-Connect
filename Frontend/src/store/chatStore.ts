// src/store/chatStore.ts
import { create } from 'zustand'
import { toast } from 'react-hot-toast'
import { chatService, CONVERSATION_PAGE_SIZE, MESSAGE_PAGE_SIZE } from '../services/chat/chatService'
import { isRequestCancelled } from '../services/apiClient'
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
    // localStorage unavailable — falls back to in-memory only.
  }
}

const persistChannelFilter = (channel: string) => {
  try {
    window.localStorage.setItem(SELECTED_CHANNEL_STORAGE_KEY, channel)
  } catch {
    // Same as above.
  }
}

/** Largest first page to re-read when refreshing an inbox the user has scrolled through. */
const MAX_REFRESH_PAGE = 100

interface ChatStoreState {
  accounts: ChatAccount[]
  /** Loaded conversations, newest activity first. Paged from the server with a cursor. */
  conversations: Conversation[]
  conversationsCursor: string | null
  hasMoreConversations: boolean
  isLoadingMoreConversations: boolean
  activeConversationId: number | null
  /**
   * The selected connection for single-channel mode (WhatsApp or Email).
   * NULL when ALL_CHANNELS is active — that mode uses no connection filter.
   */
  selectedConnectionId: number | null
  /** The open thread, chronological. Only a window of it is loaded; older pages load on demand. */
  messages: Message[]
  hasOlderMessages: boolean
  isLoadingOlderMessages: boolean
  /** Set when the open thread could not be (re)loaded; the messages already shown are kept. */
  threadError: string | null
  isLoading: boolean
  isLoadingConversations: boolean
  isSending: boolean
  isRefreshing: boolean
  fromNumber: string
  conversationsFilter: string
  /** Status tab: 'active' (Open + Pending), 'Resolved', 'Closed' or 'all'. */
  stateFilter: string
  /** Owner: '' (anyone), 'me' or 'unassigned'. */
  assigneeFilter: string
  /** ALL_CHANNELS, or a channel key the API understands ('WhatsApp' | 'Email'). */
  channelFilter: string
  sidebarSearchQuery: string
  activeAbortController: AbortController | null
  /** Sequence counter for loadConversations — only the latest response is applied. */
  _loadSeq: number

  setSelectedConnectionId: (id: number | null) => void
  setChannelFilter: (channel: string) => void
  loadAccounts: () => Promise<void>
  /** Reloads the inbox from the top (keeping as many rows as were loaded, up to a cap). */
  loadConversations: () => Promise<void>
  loadMoreConversations: () => Promise<void>
  /** Fetches only messages newer than the newest one shown. */
  refreshActiveMessages: () => Promise<void>
  /** Re-reads the newest page of the open thread, picking up status changes. */
  resyncActiveMessages: () => Promise<void>
  loadOlderMessages: () => Promise<void>
  selectConversation: (id: number | null) => Promise<void>
  /** replyButtons: up to three WhatsApp reply buttons under the text. */
  sendMessage: (text: string, mediaUrl?: string, mediaType?: string, mediaFileName?: string, replyButtons?: string[]) => Promise<void>
  deleteActiveConversation: () => Promise<void>
  deleteMessages: (messageIds: number[]) => Promise<void>
  setFromNumber: (fromNumber: string) => void
  setConversationsFilter: (filter: string) => void
  setStateFilter: (state: string) => void
  setAssigneeFilter: (assignee: string) => void
  setSidebarSearchQuery: (query: string) => void
}

/** Merges server messages into the current list by id, keeping unconfirmed optimistic ones. */
const mergeMessages = (current: Message[], incoming: Message[]): Message[] => {
  const byId = new Map<number, Message>()
  for (const m of current) byId.set(m.id, m)
  for (const m of incoming) byId.set(m.id, m)

  const confirmed = [...byId.values()].filter((m) => m.id > 0).sort((a, b) => a.id - b.id)
  const optimistic = current.filter((m) => m.id < 0)
  return [...confirmed, ...optimistic]
}

const describe = (error: unknown) =>
  (error as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Could not load messages.'

export const useChatStore = create<ChatStoreState>((set, get) => ({
  accounts: [],
  conversations: [],
  conversationsCursor: null,
  hasMoreConversations: false,
  isLoadingMoreConversations: false,
  activeConversationId: null,
  // On ALL_CHANNELS start with null (no specific connection required).
  selectedConnectionId: readPersistedChannelFilter() === ALL_CHANNELS ? null : readPersistedConnectionId(),
  messages: [],
  hasOlderMessages: false,
  isLoadingOlderMessages: false,
  threadError: null,
  activeAbortController: null,
  isLoading: false,
  isLoadingConversations: false,
  isSending: false,
  isRefreshing: false,
  fromNumber: '',
  conversationsFilter: 'All Chats',
  stateFilter: 'active',
  assigneeFilter: '',
  channelFilter: readPersistedChannelFilter(),
  sidebarSearchQuery: '',
  _loadSeq: 0,

  setSelectedConnectionId: (id) => {
    persistConnectionId(id)
    set({
      selectedConnectionId: id,
      conversations: [],
      conversationsCursor: null,
      hasMoreConversations: false,
      activeConversationId: null,
      messages: [],
      threadError: null,
    })
    void get().loadConversations()
    void get().loadAccounts()
  },

  /**
   * Changing channel filter. The old list stays visible until the new one arrives, so switching
   * does not flash an empty inbox.
   */
  setChannelFilter: (channel) => {
    if (get().channelFilter === channel) return

    persistChannelFilter(channel)
    persistConnectionId(null)

    set({
      channelFilter: channel,
      selectedConnectionId: null,
      activeConversationId: null,
      messages: [],
      threadError: null,
      accounts: [],
      fromNumber: '',
    })

    void get().loadConversations()
  },

  loadAccounts: async () => {
    try {
      const accounts = await chatService.getAccounts(get().selectedConnectionId || undefined)
      set((state) => ({
        accounts,
        fromNumber: state.fromNumber || accounts[0]?.phoneNumberId || '',
      }))
    } catch {
      // The picker keeps its previous options.
    }
  },

  loadConversations: async () => {
    const { sidebarSearchQuery, conversationsFilter, selectedConnectionId, channelFilter, conversations, stateFilter, assigneeFilter } = get()
    const isAllChannels = channelFilter === ALL_CHANNELS

    // Single-channel mode waits for its connection to be chosen (the auto-select calls back in).
    if (!isAllChannels && !selectedConnectionId) return

    const seq = get()._loadSeq + 1
    set({ _loadSeq: seq, isLoadingConversations: true })

    try {
      // Re-read as many rows as are on screen (bounded), so a live refresh does not collapse a
      // list the user has already scrolled through back to its first page.
      const limit = Math.min(MAX_REFRESH_PAGE, Math.max(CONVERSATION_PAGE_SIZE, conversations.length))
      const page = await chatService.getConversations(
        sidebarSearchQuery,
        conversationsFilter,
        isAllChannels ? undefined : selectedConnectionId || undefined,
        isAllChannels ? undefined : channelFilter,
        { limit, state: stateFilter, assignee: assigneeFilter }
      )

      if (get()._loadSeq !== seq) return
      set({ conversations: page.items, conversationsCursor: page.nextCursor, hasMoreConversations: page.hasMore })
    } catch (error) {
      // Keep the inbox that is on screen; a failed refresh must never empty it.
      if (!isRequestCancelled(error) && import.meta.env.DEV) console.warn('Inbox refresh failed', error)
    } finally {
      if (get()._loadSeq === seq) set({ isLoadingConversations: false })
    }
  },

  loadMoreConversations: async () => {
    const { conversationsCursor, hasMoreConversations, isLoadingMoreConversations } = get()
    if (!hasMoreConversations || !conversationsCursor || isLoadingMoreConversations) return

    const { sidebarSearchQuery, conversationsFilter, selectedConnectionId, channelFilter, stateFilter, assigneeFilter } = get()
    const isAllChannels = channelFilter === ALL_CHANNELS
    const seq = get()._loadSeq

    set({ isLoadingMoreConversations: true })
    try {
      const page = await chatService.getConversations(
        sidebarSearchQuery,
        conversationsFilter,
        isAllChannels ? undefined : selectedConnectionId || undefined,
        isAllChannels ? undefined : channelFilter,
        { cursor: conversationsCursor, state: stateFilter, assignee: assigneeFilter }
      )

      // A reload started meanwhile owns the list now.
      if (get()._loadSeq !== seq) return

      set((state) => {
        const known = new Set(state.conversations.map((c) => c.id))
        return {
          conversations: [...state.conversations, ...page.items.filter((c) => !known.has(c.id))],
          conversationsCursor: page.nextCursor,
          hasMoreConversations: page.hasMore,
        }
      })
    } catch (error) {
      if (!isRequestCancelled(error)) toast.error('Could not load more conversations.')
    } finally {
      set({ isLoadingMoreConversations: false })
    }
  },

  refreshActiveMessages: async () => {
    const { activeConversationId, isSending, isRefreshing, messages } = get()
    if (!activeConversationId || isSending || isRefreshing) return

    const newestId = messages.filter((m) => m.id > 0).reduce((max, m) => Math.max(max, m.id), 0)
    if (newestId === 0) return get().resyncActiveMessages()

    set({ isRefreshing: true })
    const fetchId = activeConversationId
    try {
      // Only what is new: a live thread no longer re-downloads its whole history every second.
      const page = await chatService.getMessages(fetchId, { afterId: newestId, limit: 200 })
      if (get().activeConversationId === fetchId && page.items.length > 0) {
        set((state) => ({ messages: mergeMessages(state.messages, page.items), threadError: null }))
      }
    } catch (error) {
      if (!isRequestCancelled(error)) set({ threadError: describe(error) })
    } finally {
      set({ isRefreshing: false })
    }
  },

  resyncActiveMessages: async () => {
    const { activeConversationId } = get()
    if (!activeConversationId) return

    const fetchId = activeConversationId
    try {
      const page = await chatService.getMessages(fetchId, { limit: MESSAGE_PAGE_SIZE })
      if (get().activeConversationId === fetchId) {
        set((state) => ({ messages: mergeMessages(state.messages, page.items), threadError: null }))
      }
    } catch (error) {
      if (!isRequestCancelled(error)) set({ threadError: describe(error) })
    }
  },

  loadOlderMessages: async () => {
    const { activeConversationId, hasOlderMessages, isLoadingOlderMessages, messages } = get()
    if (!activeConversationId || !hasOlderMessages || isLoadingOlderMessages) return

    const oldestId = messages.filter((m) => m.id > 0).reduce((min, m) => Math.min(min, m.id), Number.MAX_SAFE_INTEGER)
    if (oldestId === Number.MAX_SAFE_INTEGER) return

    const fetchId = activeConversationId
    set({ isLoadingOlderMessages: true })
    try {
      const page = await chatService.getMessages(fetchId, { beforeId: oldestId, limit: MESSAGE_PAGE_SIZE })
      if (get().activeConversationId === fetchId) {
        set((state) => ({ messages: mergeMessages(state.messages, page.items), hasOlderMessages: page.hasMore }))
      }
    } catch (error) {
      if (!isRequestCancelled(error)) toast.error('Could not load earlier messages.')
    } finally {
      set({ isLoadingOlderMessages: false })
    }
  },

  selectConversation: async (id) => {
    get().activeAbortController?.abort()
    const controller = new AbortController()

    set({
      activeConversationId: id,
      messages: [],
      hasOlderMessages: false,
      threadError: null,
      isLoading: id !== null,
      activeAbortController: controller,
    })

    if (!id) return

    try {
      const page = await chatService.getMessages(id, { limit: MESSAGE_PAGE_SIZE, signal: controller.signal })
      if (get().activeConversationId === id) {
        set((state) => ({
          messages: page.items,
          hasOlderMessages: page.hasMore,
          conversations: state.conversations.map((c) => (c.id === id ? { ...c, unreadCount: 0 } : c)),
        }))

        void chatService.markConversationRead(id).catch(() => {})
      }
    } catch (error) {
      if (isRequestCancelled(error)) return
      if (get().activeConversationId === id) set({ threadError: describe(error) })
    } finally {
      if (get().activeConversationId === id) set({ isLoading: false })
    }
  },

  sendMessage: async (text, mediaUrl, mediaType, mediaFileName, replyButtons) => {
    const { activeConversationId, fromNumber, conversations } = get()
    if (!activeConversationId) return

    const activeConversation = conversations.find((c) => c.id === activeConversationId)

    const tempId = -Date.now()
    const displayBody = mediaUrl
      ? (text ? `[Attachment: ${mediaFileName || 'file'}]\n\n${text}` : `[Attachment: ${mediaFileName || 'file'}]`)
      : replyButtons && replyButtons.length > 0 ? `${text}

[Buttons: ${replyButtons.join(' | ')}]` : text

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
      mediaFileName,
    }

    set((state) => ({
      isSending: true,
      messages: [...state.messages, tempMessage],
      conversations: state.conversations.map((conversation) =>
        conversation.id === activeConversationId
          ? { ...conversation, lastMessage: displayBody, lastMessageTime: 'Now' }
          : conversation
      ),
    }))

    try {
      const newMsg = await chatService.sendMessage(
        activeConversationId,
        text,
        fromNumber || undefined,
        mediaUrl,
        mediaType,
        mediaFileName,
        activeConversation?.connectionId || get().selectedConnectionId || undefined,
        replyButtons
      )

      set((state) => ({
        messages: newMsg
          ? mergeMessages(state.messages.filter((m) => m.id !== tempId), [{ ...newMsg, status: newMsg.status ?? 'sent' }])
          : state.messages.filter((m) => m.id !== tempId),
      }))

      void get().loadConversations()
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to send message.'
      toast.error(errorMessage)

      set((state) => ({
        messages: state.messages.map((message) =>
          message.id === tempId ? { ...message, status: 'failed', errorMessage } : message
        ),
      }))
    } finally {
      set({ isSending: false })
    }
  },

  setFromNumber: (fromNumber) => set({ fromNumber }),
  setStateFilter: (stateFilter) => {
    set({ stateFilter, conversationsCursor: null })
    void get().loadConversations()
  },

  setAssigneeFilter: (assigneeFilter) => {
    set({ assigneeFilter, conversationsCursor: null })
    void get().loadConversations()
  },

  setConversationsFilter: (conversationsFilter) => set({ conversationsFilter }),
  setSidebarSearchQuery: (sidebarSearchQuery) => set({ sidebarSearchQuery }),

  deleteActiveConversation: async () => {
    const { activeConversationId } = get()
    if (!activeConversationId) return
    await chatService.deleteConversation(activeConversationId)
    set((state) => ({
      activeConversationId: null,
      messages: [],
      conversations: state.conversations.filter((c) => c.id !== activeConversationId),
    }))
  },

  deleteMessages: async (messageIds) => {
    const { activeConversationId } = get()
    if (!activeConversationId || messageIds.length === 0) return

    await chatService.deleteMessages(activeConversationId, messageIds)

    const removed = new Set(messageIds)
    set((state) => ({ messages: state.messages.filter((m) => !removed.has(m.id)) }))
  },
}))

export default useChatStore
