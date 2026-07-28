import { create } from 'zustand'
import { connectionService } from '../services/connections/connectionService'
import type { Connection, ConnectionDashboard } from '../types/connection'

interface ConnectionStoreState {
  connections: Connection[]
  dashboard: ConnectionDashboard | null
  selectedConnection: Connection | null
  isLoading: boolean
  searchQuery: string
  statusFilter: string

  setSearchQuery: (query: string) => void
  setStatusFilter: (filter: string) => void
  setSelectedConnection: (conn: Connection | null) => void

  fetchDashboard: () => Promise<void>
  fetchConnections: () => Promise<void>
  createConnection: (name: string, description?: string) => Promise<Connection>
  updateConnection: (id: number, name: string, description?: string) => Promise<void>
  disconnectConnection: (id: number) => Promise<void>
  reconnectConnection: (id: number) => Promise<void>
  deleteConnection: (id: number) => Promise<void>
}

export const useConnectionStore = create<ConnectionStoreState>((set, get) => ({
  connections: [],
  dashboard: null,
  selectedConnection: null,
  isLoading: false,
  searchQuery: '',
  statusFilter: 'All Status',

  setSearchQuery: (query) => set({ searchQuery: query }),
  setStatusFilter: (filter) => set({ statusFilter: filter }),
  setSelectedConnection: (conn) => set({ selectedConnection: conn }),

  fetchDashboard: async () => {
    set({ isLoading: true })
    try {
      const data = await connectionService.getDashboard()
      set({ dashboard: data, connections: data.connections })
    } catch {
      // Handled silently
    } finally {
      set({ isLoading: false })
    }
  },

  fetchConnections: async () => {
    set({ isLoading: true })
    try {
      const data = await connectionService.getConnections()
      set({ connections: data })
    } catch {
      // Handled silently
    } finally {
      set({ isLoading: false })
    }
  },

  createConnection: async (name, description) => {
    const conn = await connectionService.createConnection({ name, description })
    await get().fetchDashboard()
    return conn
  },

  updateConnection: async (id, name, description) => {
    await connectionService.updateConnection(id, { name, description })
    await get().fetchDashboard()
  },

  disconnectConnection: async (id) => {
    await connectionService.disconnectConnection(id)
    await get().fetchDashboard()
  },

  reconnectConnection: async (id) => {
    await connectionService.reconnectConnection(id)
    await get().fetchDashboard()
  },

  deleteConnection: async (id) => {
    await connectionService.deleteConnection(id)
    await get().fetchDashboard()
  }
}))
