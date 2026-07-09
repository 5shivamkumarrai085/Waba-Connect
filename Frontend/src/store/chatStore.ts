// src/store/chatStore.ts
import { create } from 'zustand'
import { chatService } from '../services/chat/chatService'
import type { Conversation, Message } from '../types/chat'

interface ChatStoreState {
  conversations: Conversation[]
  activeConversationId: string | null
  messages: Message[]
  isLoading: boolean
  fromNumber: string
  conversationsFilter: string
  sidebarSearchQuery: string
  
  loadConversations: () => Promise<void>
  selectConversation: (id: string | null) => Promise<void>
  sendMessage: (text: string) => Promise<void>
  setFromNumber: (fromNumber: string) => void
  setConversationsFilter: (filter: string) => void
  setSidebarSearchQuery: (query: string) => void
}

export const useChatStore = create<ChatStoreState>((set, get) => ({
  conversations: [],
  activeConversationId: null,
  messages: [],
  isLoading: false,
  fromNumber: '+60108052877', // default from dropdown selector in Screenshot 3
  conversationsFilter: 'All Chats',
  sidebarSearchQuery: '',
  
  loadConversations: async () => {
    set({ isLoading: true })
    try {
      const list = await chatService.getConversations()
      set({ conversations: list })
    } catch (err) {
      console.error('Error loading conversations:', err)
    } finally {
      set({ isLoading: false })
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
      
      // Reset unread count locally once read
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
    const { activeConversationId } = get()
    if (!activeConversationId) return
    
    try {
      const newMsg = await chatService.sendMessage(activeConversationId, text)
      if (newMsg) {
        set((state) => ({
          messages: [...state.messages, newMsg]
        }))
      }
      
      // Reload conversations to update last message preview in list
      const list = await chatService.getConversations()
      set({ conversations: list })
    } catch (err) {
      console.error('Error sending message:', err)
    }
  },
  
  setFromNumber: (fromNumber) => set({ fromNumber }),
  setConversationsFilter: (conversationsFilter) => set({ conversationsFilter }),
  setSidebarSearchQuery: (sidebarSearchQuery) => set({ sidebarSearchQuery })
}))
export default useChatStore
