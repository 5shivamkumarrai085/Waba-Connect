import { create } from 'zustand'
import { connectionService } from '../services/connections/connectionService'
import { createRequestCache } from './cacheHelpers'
import { isRequestCancelled } from '../services/apiClient'
import { useTemplateStore } from './templateStore'
import type { Connection, ConnectionDashboard } from '../types/connection'

/**
 * fetchDashboard is called unconditionally on mount from eight places — Connections, Templates,
 * Chat, Bulk Campaign, the campaign wizard and InitiateChatModal among them — and several of
 * those mount together. Deduping collapses that burst into one request; the 20s TTL keeps
 * navigating between those screens from re-issuing it.
 *
 * Anything that changes a connection passes `force: true`, so the cache is never the reason a
 * screen shows a stale connection.
 */
const dashboardCache = createRequestCache<ConnectionDashboard>({ ttlMs: 20_000 })

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

  fetchDashboard: (options?: { force?: boolean }) => Promise<void>
  fetchConnections: () => Promise<void>
  createConnection: (name: string, description?: string, nickname?: string) => Promise<Connection>
  updateConnection: (id: number, name: string, description?: string, nickname?: string) => Promise<void>
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

  fetchDashboard: async (options) => {
    set({ isLoading: true })
    try {
      const data = await dashboardCache.dedupe(
        'dashboard',
        () => connectionService.getDashboard(),
        options
      )
      set({ dashboard: data, connections: data.connections })
    } catch (err) {
      // A superseded request means a newer one is already running — leave the current data and
      // the loading flag to that request rather than flashing an empty state.
      if (isRequestCancelled(err)) return
    } finally {
      set({ isLoading: false })
    }
  },

  fetchConnections: async () => {
    set({ isLoading: true })
    try {
      const data = await connectionService.getConnections()
      set({ connections: data })
    } catch (err) {
      if (isRequestCancelled(err)) return
    } finally {
      set({ isLoading: false })
    }
  },

  createConnection: async (name, description, nickname) => {
    const conn = await connectionService.createConnection({ name, description, nickname })
    await get().fetchDashboard({ force: true })
    return conn
  },

  updateConnection: async (id, name, description, nickname) => {
    await connectionService.updateConnection(id, { name, description, nickname })
    await get().fetchDashboard({ force: true })
  },

  disconnectConnection: async (id) => {
    await connectionService.disconnectConnection(id)
    await get().fetchDashboard({ force: true })
    // Cross-module edge, stated explicitly rather than left to a global event bus: the backend
    // removes a connection's templates when it is disconnected, so an already-open Templates
    // list would otherwise keep showing rows that no longer exist.
    await useTemplateStore.getState().loadTemplates()
  },

  reconnectConnection: async (id) => {
    await connectionService.reconnectConnection(id)
    await get().fetchDashboard({ force: true })
    await useTemplateStore.getState().loadTemplates()
  },

  deleteConnection: async (id) => {
    await connectionService.deleteConnection(id)
    await get().fetchDashboard({ force: true })
    await useTemplateStore.getState().loadTemplates()
  }
}))
