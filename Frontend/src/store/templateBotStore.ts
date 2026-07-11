import { create } from 'zustand'
import { templateBotService } from '../services/templateBot/templateBotService'
import type { TemplateBot } from '../types/templateBot'

interface TemplateBotStoreState {
  bots: TemplateBot[]
  totalCount: number
  isLoading: boolean
  page: number
  pageSize: number
  relationType: string
  isActive: boolean | undefined
  search: string
  
  setPage: (page: number) => void
  setPageSize: (pageSize: number) => void
  setRelationType: (relationType: string) => void
  setIsActive: (isActive: boolean | undefined) => void
  setSearch: (search: string) => void
  
  fetchBots: () => Promise<void>
  deleteBot: (id: number) => Promise<boolean>
  cloneBot: (id: number) => Promise<TemplateBot>
  toggleBotActive: (id: number) => Promise<void>
}

export const useTemplateBotStore = create<TemplateBotStoreState>((set, get) => ({
  bots: [],
  totalCount: 0,
  isLoading: false,
  page: 1,
  pageSize: 10,
  relationType: 'All',
  isActive: undefined,
  search: '',

  setPage: (page) => {
    set({ page })
    get().fetchBots()
  },
  setPageSize: (pageSize) => {
    set({ pageSize, page: 1 })
    get().fetchBots()
  },
  setRelationType: (relationType) => {
    set({ relationType, page: 1 })
    get().fetchBots()
  },
  setIsActive: (isActive) => {
    set({ isActive, page: 1 })
    get().fetchBots()
  },
  setSearch: (search) => {
    set({ search, page: 1 })
  },

  fetchBots: async () => {
    set({ isLoading: true })
    try {
      const relation = get().relationType === 'All' ? undefined : get().relationType
      const active = get().isActive
      const searchVal = get().search
      
      const data = await templateBotService.getTemplateBots({
        page: get().page,
        pageSize: get().pageSize,
        relationType: relation,
        isActive: active,
        search: searchVal || undefined
      })
      
      set({
        bots: data?.items || [],
        totalCount: data?.totalCount || 0
      })
    } catch (error) {
      console.error('Failed to load template bots:', error)
    } finally {
      set({ isLoading: false })
    }
  },

  deleteBot: async (id) => {
    try {
      const success = await templateBotService.deleteTemplateBot(id)
      if (success) {
        get().fetchBots()
      }
      return success;
    } catch (error) {
      console.error('Failed to delete template bot:', error)
      return false
    }
  },

  cloneBot: async (id) => {
    try {
      const cloned = await templateBotService.cloneTemplateBot(id)
      get().fetchBots()
      return cloned
    } catch (error) {
      console.error('Failed to clone template bot:', error)
      throw error
    }
  },

  toggleBotActive: async (id) => {
    try {
      await templateBotService.toggleTemplateBotActive(id)
      // Optimistically update or refetch
      set({
        bots: get().bots.map(b => b.id === id ? { ...b, isActive: !b.isActive } : b)
      })
    } catch (error) {
      console.error('Failed to toggle template bot active status:', error)
    }
  }
}))
