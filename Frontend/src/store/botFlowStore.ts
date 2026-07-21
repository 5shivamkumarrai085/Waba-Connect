import { create } from 'zustand'
import { toast } from 'react-hot-toast'
import { botFlowService } from '../services/botFlow/botFlowService'
import type { BotFlow } from '../types/botFlow'

interface BotFlowStoreState {
  flows: BotFlow[]
  totalCount: number
  isLoading: boolean
  page: number
  pageSize: number
  search: string
  currentFlow: BotFlow | null

  setPage: (page: number) => void
  setPageSize: (pageSize: number) => void
  setSearch: (search: string) => void
  setCurrentFlow: (flow: BotFlow | null) => void

  fetchFlows: () => Promise<void>
  fetchFlowById: (id: number) => Promise<BotFlow | null>
  createFlow: (name: string, description?: string) => Promise<BotFlow>
  updateFlow: (id: number, name: string, description?: string, flowData?: string) => Promise<BotFlow>
  deleteFlow: (id: number) => Promise<boolean>
  toggleFlowActive: (id: number) => Promise<void>
}

export const useBotFlowStore = create<BotFlowStoreState>((set, get) => ({
  flows: [],
  totalCount: 0,
  isLoading: false,
  page: 1,
  pageSize: 10,
  search: '',
  currentFlow: null,

  setPage: (page) => {
    set({ page })
    get().fetchFlows()
  },
  setPageSize: (pageSize) => {
    set({ pageSize, page: 1 })
    get().fetchFlows()
  },
  setSearch: (search) => {
    set({ search, page: 1 })
  },
  setCurrentFlow: (flow) => {
    set({ currentFlow: flow })
  },

  fetchFlows: async () => {
    set({ isLoading: true })
    try {
      const data = await botFlowService.getBotFlows({
        page: get().page,
        pageSize: get().pageSize,
        search: get().search || undefined
      })
      set({
        flows: data?.items || [],
        totalCount: data?.totalCount || 0
      })
    } catch {
      // Error handled silently
    } finally {
      set({ isLoading: false })
    }
  },

  fetchFlowById: async (id) => {
    try {
      const flow = await botFlowService.getBotFlowById(id)
      set({ currentFlow: flow })
      return flow
    } catch {
      return null
    }
  },

  createFlow: async (name, description) => {
    try {
      const flow = await botFlowService.createBotFlow({ name, description })
      get().fetchFlows()
      return flow
    } catch (error) {
      toast.error('Failed to save flow')
      throw error
    }
  },

  updateFlow: async (id, name, description, flowData) => {
    try {
      const flow = await botFlowService.updateBotFlow(id, { name, description, flowData })
      get().fetchFlows()
      if (get().currentFlow?.id === id) {
        set({ currentFlow: flow })
      }
      return flow
    } catch (error) {
      toast.error('Failed to save flow')
      throw error
    }
  },

  deleteFlow: async (id) => {
    try {
      const success = await botFlowService.deleteBotFlow(id)
      if (success) {
        get().fetchFlows()
      }
      return success
    } catch (error) {
      toast.error('Failed to delete bot flow')
      throw error
    }
  },

  toggleFlowActive: async (id) => {
    try {
      await botFlowService.toggleBotFlowActive(id)
      set({
        flows: get().flows.map(f => f.id === id ? { ...f, isActive: !f.isActive } : f)
      })
    } catch {
      toast.error('Failed to toggle status')
    }
  }
}))
