import { create } from 'zustand'
import { templateService } from '../services/templates/templateService'
import { isRequestCancelled } from '../services/apiClient'
import type { Template, TemplateLanguage, TemplateCategory, TemplateStatus, TemplateType } from '../types/templates'
import { useDashboardStore } from './dashboardStore'

interface TemplateStoreState {
  templates: Template[]
  isLoading: boolean
  isRefreshing: boolean
  searchQuery: string
  
  // Cache for dropdowns
  languages: TemplateLanguage[] | null
  categories: TemplateCategory[] | null
  statuses: TemplateStatus[] | null
  types: TemplateType[] | null
  
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
  
  loadTemplates: (connectedConnectionIds?: number[]) => Promise<boolean>
  refreshTemplates: () => Promise<boolean>
  loadFilterOptions: () => Promise<void>
}

export const useTemplateStore = create<TemplateStoreState>((set, get) => ({
  templates: [],
  isLoading: false,
  isRefreshing: false,
  searchQuery: '',
  
  languages: null,
  categories: null,
  statuses: null,
  types: null,
  
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
  
  /**
   * Loads templates from the local database in a single request.
   *
   * This used to fan out one `/Templates/by-connection/{id}` call per connected connection,
   * each of which hit Meta's Graph API server-side before returning — so the page waited on N
   * sequential outbound HTTPS calls and could not render at all while Meta was slow. Templates
   * are kept current by TemplateSyncBackgroundService, with the "Load Templates" button for an
   * on-demand refresh.
   *
   * The `connectedConnectionIds` parameter is retained so existing callers keep compiling, but
   * is no longer used to decide what to fetch.
   */
  loadTemplates: async () => {
    const hasCache = get().templates.length > 0
    if (!hasCache) {
      set({ isLoading: true })
    }
    try {
      const fetched = await templateService.getTemplates()
      set({ templates: fetched, isLoading: false })
      return true
    } catch (err) {
      // A superseded request is not a failure: a newer load is already in flight and owns both
      // the data and the loading flag. Reporting it would show an error toast for a request the
      // app itself replaced.
      if (isRequestCancelled(err)) return true
      console.error('Error loading templates:', err)
      set({ isLoading: false })
      return false
    }
  },

  refreshTemplates: async () => {
    set({ isRefreshing: true })
    try {
      const fetched = await templateService.refreshTemplates()
      set({ templates: fetched, currentPage: 1, isRefreshing: false })
      useDashboardStore.getState().loadDashboardData(false)
      return true
    } catch (err) {
      console.error('Error refreshing templates:', err)
      set({ isRefreshing: false })
      return false
    }
  },

  loadFilterOptions: async () => {
    const { languages, categories, statuses, types } = get()
    if (languages && categories && statuses && types) return // already cached!

    try {
      const [lang, cat, stat, typ] = await Promise.all([
        templateService.getTemplateLanguages(),
        templateService.getTemplateCategories(),
        templateService.getTemplateStatuses(),
        templateService.getTemplateTypes()
      ])
      set({
        languages: lang,
        categories: cat,
        statuses: stat,
        types: typ
      })
    } catch (err) {
      console.error('Error loading template filter options:', err)
    }
  }
}))
export default useTemplateStore
