// src/store/templateStore.ts
import { create } from 'zustand'
import { templateService } from '../services/templates/templateService'
import type { Template } from '../types/templates'

interface TemplateStoreState {
  templates: Template[]
  isLoading: boolean
  isRefreshing: boolean
  searchQuery: string
  
  // Specific filters matching Screenshot 1
  nameOperator: 'contains' | 'equals' | string
  nameQuery: string
  languageFilter: string
  categoryFilter: string
  typeFilter: string
  statusFilter: string
  
  currentPage: number
  pageSize: number
  sortColumn: string
  sortOrder: 'asc' | 'desc'
  
  setSearchQuery: (query: string) => void
  setNameOperator: (operator: string) => void
  setNameQuery: (query: string) => void
  setLanguageFilter: (language: string) => void
  setCategoryFilter: (category: string) => void
  setTypeFilter: (type: string) => void
  setStatusFilter: (status: string) => void
  
  setCurrentPage: (page: number) => void
  setPageSize: (size: number) => void
  setSort: (column: string, order: 'asc' | 'desc') => void
  
  loadTemplates: () => Promise<void>
  refreshTemplates: () => Promise<void>
}

export const useTemplateStore = create<TemplateStoreState>((set) => ({
  templates: [],
  isLoading: false,
  isRefreshing: false,
  searchQuery: '',
  
  nameOperator: 'contains',
  nameQuery: '',
  languageFilter: 'All',
  categoryFilter: 'All',
  typeFilter: 'All',
  statusFilter: 'All',
  
  currentPage: 1,
  pageSize: 10,
  sortColumn: 'id',
  sortOrder: 'asc',
  
  setSearchQuery: (searchQuery) => set({ searchQuery, currentPage: 1 }),
  setNameOperator: (nameOperator) => set({ nameOperator, currentPage: 1 }),
  setNameQuery: (nameQuery) => set({ nameQuery, currentPage: 1 }),
  setLanguageFilter: (languageFilter) => set({ languageFilter, currentPage: 1 }),
  setCategoryFilter: (categoryFilter) => set({ categoryFilter, currentPage: 1 }),
  setTypeFilter: (typeFilter) => set({ typeFilter, currentPage: 1 }),
  setStatusFilter: (statusFilter) => set({ statusFilter, currentPage: 1 }),
  
  setCurrentPage: (currentPage) => set({ currentPage }),
  setPageSize: (pageSize) => set({ pageSize, currentPage: 1 }),
  setSort: (sortColumn, sortOrder) => set({ sortColumn, sortOrder }),
  
  loadTemplates: async () => {
    set({ isLoading: true })
    try {
      const fetched = await templateService.getTemplates()
      set({ templates: fetched })
    } catch (err) {
      console.error('Error loading templates:', err)
    } finally {
      set({ isLoading: false })
    }
  },
  
  refreshTemplates: async () => {
    set({ isRefreshing: true })
    try {
      const fetched = await templateService.refreshTemplates()
      set({ templates: fetched, currentPage: 1 })
    } catch (err) {
      console.error('Error refreshing templates:', err)
    } finally {
      set({ isRefreshing: false })
    }
  }
}))
export default useTemplateStore
