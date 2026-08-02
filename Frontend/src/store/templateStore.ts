import { create } from 'zustand'
import { templateService } from '../services/templates/templateService'
import type { Template, TemplateLanguage, TemplateCategory, TemplateStatus, TemplateType } from '../types/templates'

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
  
  loadTemplates: (connectedConnectionIds?: number[]) => Promise<void>
  refreshTemplates: () => Promise<void>
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
  
  loadTemplates: async (connectedConnectionIds?: number[]) => {
    const hasCache = get().templates.length > 0
    if (!hasCache) {
      set({ isLoading: true })
    }
    try {
      let fetched: Template[] = []
      
      if (connectedConnectionIds && connectedConnectionIds.length > 0) {
        // Fetch templates dynamically from Meta for each connected connection
        const results = await Promise.all(
          connectedConnectionIds.map(id => templateService.getTemplatesByConnection(id))
        )
        // Merge and deduplicate by template name
        const seen = new Set<string>()
        for (const connTemplates of results) {
          for (const t of connTemplates) {
            if (!seen.has(t.name)) {
              seen.add(t.name)
              fetched.push(t)
            }
          }
        }
      }
      // If no connected connections, fetched stays empty — no templates to show
      
      set({ templates: fetched, isLoading: false })
    } catch (err) {
      console.error('Error loading templates:', err)
      set({ isLoading: false })
    }
  },
  
  refreshTemplates: async () => {
    set({ isRefreshing: true })
    try {
      const fetched = await templateService.refreshTemplates()
      set({ templates: fetched, currentPage: 1, isRefreshing: false })
    } catch (err) {
      console.error('Error refreshing templates:', err)
      set({ isRefreshing: false })
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
