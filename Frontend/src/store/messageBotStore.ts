import { create } from 'zustand'
import { messageBotService } from '../services/messageBot/messageBotService'
import type { MessageBot, CreateMessageBotRequest, UpdateMessageBotRequest } from '../types/messageBot'

interface MessageBotStoreState {
  bots: MessageBot[]
  totalCount: number
  isLoading: boolean
  page: number
  pageSize: number
  searchQuery: string
  relationTypeFilter: string
  isActiveFilter: boolean | null
  sortBy: string
  sortDescending: boolean

  setPage: (page: number) => void
  setPageSize: (size: number) => void
  setSearchQuery: (query: string) => void
  setRelationTypeFilter: (relation: string) => void
  setIsActiveFilter: (active: boolean | null) => void
  setSorting: (field: string, descending: boolean) => void

  loadBots: () => Promise<void>
  createBot: (data: CreateMessageBotRequest) => Promise<MessageBot>
  updateBot: (id: number, data: UpdateMessageBotRequest) => Promise<MessageBot>
  deleteBot: (id: number) => Promise<boolean>
  cloneBot: (id: number) => Promise<MessageBot>
  toggleBotActive: (id: number) => Promise<void>
}

export const useMessageBotStore = create<MessageBotStoreState>((set, get) => ({
  bots: [],
  totalCount: 0,
  isLoading: false,
  page: 1,
  pageSize: 10,
  searchQuery: '',
  relationTypeFilter: '',
  isActiveFilter: null,
  sortBy: 'id',
  sortDescending: true,

  setPage: (page) => {
    set({ page })
    get().loadBots()
  },
  setPageSize: (pageSize) => {
    set({ pageSize, page: 1 })
    get().loadBots()
  },
  setSearchQuery: (searchQuery) => {
    set({ searchQuery, page: 1 })
    get().loadBots()
  },
  setRelationTypeFilter: (relationTypeFilter) => {
    set({ relationTypeFilter, page: 1 })
    get().loadBots()
  },
  setIsActiveFilter: (isActiveFilter) => {
    set({ isActiveFilter, page: 1 })
    get().loadBots()
  },
  setSorting: (sortBy, sortDescending) => {
    set({ sortBy, sortDescending })
    get().loadBots()
  },

  loadBots: async () => {
    set({ isLoading: true })
    const { page, pageSize, relationTypeFilter, isActiveFilter, searchQuery, sortBy, sortDescending } = get()
    
    try {
      const response = await messageBotService.getBots({
        page,
        pageSize,
        relationType: relationTypeFilter || undefined,
        isActive: isActiveFilter === null ? undefined : isActiveFilter,
        search: searchQuery || undefined,
        sortBy,
        sortDescending
      })

      set({
        bots: response.items,
        totalCount: response.totalCount,
        page: response.page,
        pageSize: response.pageSize
      })
    } catch (error) {
      console.error('Error loading bots:', error)
    } finally {
      set({ isLoading: false })
    }
  },

  createBot: async (data) => {
    set({ isLoading: true })
    try {
      const bot = await messageBotService.createBot(data)
      await get().loadBots()
      return bot
    } catch (error) {
      console.error('Error creating bot:', error)
      throw error;
    } finally {
      set({ isLoading: false })
    }
  },

  updateBot: async (id, data) => {
    set({ isLoading: true })
    try {
      const bot = await messageBotService.updateBot(id, data)
      await get().loadBots()
      return bot
    } catch (error) {
      console.error('Error updating bot:', error)
      throw error;
    } finally {
      set({ isLoading: false })
    }
  },

  deleteBot: async (id) => {
    set({ isLoading: true })
    try {
      const success = await messageBotService.deleteBot(id)
      if (success) {
        await get().loadBots()
      }
      return success
    } catch (error) {
      console.error('Error deleting bot:', error)
      return false
    } finally {
      set({ isLoading: false })
    }
  },

  cloneBot: async (id) => {
    set({ isLoading: true })
    try {
      const bot = await messageBotService.cloneBot(id)
      await get().loadBots()
      return bot
    } catch (error) {
      console.error('Error cloning bot:', error)
      throw error;
    } finally {
      set({ isLoading: false })
    }
  },

  toggleBotActive: async (id) => {
    try {
      const updated = await messageBotService.toggleBotActive(id)
      if (updated) {
        set((state) => ({
          bots: state.bots.map((b) => (b.id === id ? { ...b, isActive: updated.isActive } : b))
        }))
      }
    } catch (error) {
      console.error('Error toggling bot status:', error)
    }
  }
}))
