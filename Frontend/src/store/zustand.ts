import { create } from 'zustand'
import { reportingService } from '../services/reportingService'
import { activityLogService } from '../services/activityLogService'
import type { AuditLogPage, AuditLogFilters, AuditFilterOptions, LoginAttemptPage } from '../types/reporting'

// Sidebar state store
interface SidebarState {
  isCollapsed: boolean
  toggleSidebar: () => void
  setCollapsed: (collapsed: boolean) => void
}

export const useSidebarStore = create<SidebarState>((set) => ({
  isCollapsed: false,
  toggleSidebar: () => set((state) => ({ isCollapsed: !state.isCollapsed })),
  setCollapsed: (collapsed) => set({ isCollapsed: collapsed }),
}))

// Data Preview/Empty state mode store
interface PreviewState {
  previewMode: boolean
  togglePreviewMode: () => void
}

export const usePreviewStore = create<PreviewState>((set) => ({
  previewMode: true, // Default to true so it matches the screenshots on load, can be toggled in header
  togglePreviewMode: () => set((state) => ({ previewMode: !state.previewMode })),
}))

// Campaign Form state store
interface CampaignFormState {
  campaignName: string
  relationType: string
  template: string
  scheduledTime: string
  ignoreScheduledTime: boolean
  
  setCampaignName: (name: string) => void
  setRelationType: (type: string) => void
  setTemplate: (temp: string) => void
  setScheduledTime: (time: string) => void
  setIgnoreScheduledTime: (ignore: boolean) => void
  resetForm: () => void
}

export const useCampaignStore = create<CampaignFormState>((set) => ({
  campaignName: '',
  relationType: '',
  template: '',
  scheduledTime: '',
  ignoreScheduledTime: false,
  
  setCampaignName: (campaignName) => set({ campaignName }),
  setRelationType: (relationType) => set({ relationType }),
  setTemplate: (template) => set({ template }),
  setScheduledTime: (scheduledTime) => set({ scheduledTime }),
  setIgnoreScheduledTime: (ignoreScheduledTime) => set({ ignoreScheduledTime }),
  resetForm: () => set({
    campaignName: '',
    relationType: '',
    template: '',
    scheduledTime: '',
    ignoreScheduledTime: false,
  }),
}))

// Theme state store
interface ThemeState {
  theme: 'light' | 'dark'
  toggleTheme: () => void
}

export const useThemeStore = create<ThemeState>((set) => ({
  theme: 'light',
  toggleTheme: () => set((state) => ({ theme: state.theme === 'light' ? 'dark' : 'light' })),
}))

// Filter state store
interface FilterState {
  reportingTimeFilter: 'today' | 'week' | 'month' | 'all'
  activityTimeFilter: 'today' | 'yesterday' | 'week' | 'month' | 'all'
  setReportingTimeFilter: (filter: 'today' | 'week' | 'month' | 'all') => void
  setActivityTimeFilter: (filter: 'today' | 'yesterday' | 'week' | 'month' | 'all') => void
}

export const useFilterStore = create<FilterState>((set) => ({
  reportingTimeFilter: 'month', // Default active filter matches screenshot 1 ("This Month")
  activityTimeFilter: 'month',  // Default active filter matches screenshot 4 ("This Month")
  setReportingTimeFilter: (reportingTimeFilter) => set({ reportingTimeFilter }),
  setActivityTimeFilter: (activityTimeFilter) => set({ activityTimeFilter }),
}))

// Reporting page UI state store
interface ReportingStoreState {
  reportingTimeFilter: 'today' | 'week' | 'month' | 'all'
  setReportingTimeFilter: (filter: 'today' | 'week' | 'month' | 'all') => void
  
  // Cache fields
  metricsCache: Record<string, any[]>
  accuracyRecords: any[] | null
  freshnessRecords: any[] | null
  exportItems: any[] | null
  features: string[] | null
  
  isLoading: boolean
  isBackgroundSyncing: boolean
  setIsLoading: (loading: boolean) => void
  loadReportingData: (forceShowSkeleton?: boolean) => Promise<void>
  /**
   * Drops the per-time-filter metric cache and refetches if the page is mounted.
   *
   * Reporting metrics are derived from campaigns, so a campaign created or deleted elsewhere
   * makes every cached filter wrong. Without this, `metricsCache` is kept for the life of the
   * session and the Reporting page would keep showing pre-change numbers.
   */
  invalidate: () => void
}

export const useReportingStore = create<ReportingStoreState>((set, get) => ({
  reportingTimeFilter: 'month',
  setReportingTimeFilter: (reportingTimeFilter) => {
    set({ reportingTimeFilter })
    get().loadReportingData()
  },
  metricsCache: {},
  accuracyRecords: null,
  freshnessRecords: null,
  exportItems: null,
  features: null,
  isLoading: false,
  isBackgroundSyncing: false,
  setIsLoading: (isLoading) => set({ isLoading }),
  loadReportingData: async (forceShowSkeleton = false) => {
    const { reportingTimeFilter, metricsCache, accuracyRecords, freshnessRecords, exportItems, features } = get()
    const hasCache = metricsCache[reportingTimeFilter] && accuracyRecords && freshnessRecords && exportItems && features
    
    if (!hasCache || forceShowSkeleton) {
      set({ isLoading: true })
    } else {
      set({ isBackgroundSyncing: true })
    }

    try {
      const [
        fetchedMetrics,
        fetchedAccuracy,
        fetchedFreshness,
        fetchedExports,
        fetchedFeatures
      ] = await Promise.all([
        reportingService.getReportingMetrics(reportingTimeFilter),
        reportingService.getAccuracyRecords(),
        reportingService.getFreshnessRecords(),
        reportingService.getExportItems(),
        reportingService.getCustomisationFeatures()
      ])

      set((state) => ({
        metricsCache: {
          ...state.metricsCache,
          [reportingTimeFilter]: fetchedMetrics
        },
        accuracyRecords: fetchedAccuracy,
        freshnessRecords: fetchedFreshness,
        exportItems: fetchedExports,
        features: fetchedFeatures,
        isLoading: false,
        isBackgroundSyncing: false
      }))
    } catch (err) {
      console.error('Error fetching reporting logs data:', err)
      set({ isLoading: false, isBackgroundSyncing: false })
    }
  },

  invalidate: () => {
    const hadData = Object.keys(get().metricsCache).length > 0
    set({ metricsCache: {}, accuracyRecords: null, freshnessRecords: null })
    // Only refetch for a session that has actually opened Reporting; otherwise clearing the
    // cache is enough and the next visit loads fresh.
    if (hadData) void get().loadReportingData()
  }
}))

// Activity Logs UI state store
type ActivityTab = 'errors' | 'successes' | 'audits'
type ActivityTimeFilter = 'today' | 'yesterday' | 'week' | 'month' | 'all'

const emptyAuditPage: AuditLogPage = { items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 0 }

const emptyLoginPage = <T,>(): LoginAttemptPage<T> => ({
  items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 0
})

/** No filters applied. Used both as the initial state and by Reset. */
const emptyAuditFilters: AuditLogFilters = {}

interface ActivityLogStoreState {
  activeTab: ActivityTab
  searchQuery: string
  activityTimeFilter: ActivityTimeFilter

  /** Audit tab only: server-side filters, applied on demand rather than per keystroke. */
  auditFilters: AuditLogFilters
  auditPage: number
  auditPageSize: number
  auditFilterOptions: AuditFilterOptions

  /** Each tab pages independently, so moving through one does not disturb the others. */
  loginPage: Record<'errors' | 'successes', number>
  loginPageSize: number

  setActiveTab: (tab: ActivityTab) => void
  setSearchQuery: (query: string) => void
  setActivityTimeFilter: (filter: ActivityTimeFilter) => void
  setAuditFilters: (filters: AuditLogFilters) => void
  resetAuditFilters: () => void
  setAuditPage: (page: number) => void
  setAuditPageSize: (pageSize: number) => void
  setLoginPage: (tab: 'errors' | 'successes', page: number) => void
  setLoginPageSize: (pageSize: number) => void
  loadAuditFilterOptions: () => Promise<void>

  // Cache fields. The key must carry every input that changes the result — filter, search,
  // audit filters, page and page size. Keying on fewer of them serves one query's rows under
  // another's key, which reads as the filter silently not working.
  metricsCache: Record<string, any[]>
  successesCache: Record<string, LoginAttemptPage<any>>
  errorsCache: Record<string, LoginAttemptPage<any>>
  auditsCache: Record<string, AuditLogPage>

  isLoading: boolean
  isBackgroundSyncing: boolean
  setIsLoading: (loading: boolean) => void
  loadActivityData: (forceShowSkeleton?: boolean) => Promise<void>
}

/**
 * Search debounce.
 *
 * Every keystroke used to fire four requests. With the audit query now filtering and counting
 * across the whole table, that is four server-side scans per character typed.
 */
let searchDebounceTimer: ReturnType<typeof setTimeout> | undefined

export const useActivityLogStore = create<ActivityLogStoreState>((set, get) => ({
  activeTab: 'errors', // Default matches screenshot 4 ("Login Errors")
  searchQuery: '',
  activityTimeFilter: 'month',
  auditFilters: emptyAuditFilters,
  auditPage: 1,
  auditPageSize: 10,
  auditFilterOptions: { modules: [], actions: [], statuses: [], users: [] },
  loginPage: { errors: 1, successes: 1 },
  loginPageSize: 10,

  setActiveTab: (activeTab) => {
    set({ activeTab })
    // Only the active tab's data is fetched, so switching to one that has never been opened
    // needs a load. A cache hit makes this free.
    get().loadActivityData()
  },

  setSearchQuery: (searchQuery) => {
    // Page 1 everywhere: results for a new search term start at the beginning, and staying on
    // page 7 of a narrower result set would show an empty table.
    set({ searchQuery, auditPage: 1, loginPage: { errors: 1, successes: 1 } })
    if (searchDebounceTimer) clearTimeout(searchDebounceTimer)
    searchDebounceTimer = setTimeout(() => get().loadActivityData(), 350)
  },

  setActivityTimeFilter: (activityTimeFilter) => {
    set({ activityTimeFilter, auditPage: 1, loginPage: { errors: 1, successes: 1 } })
    get().loadActivityData()
  },

  setAuditFilters: (auditFilters) => {
    set({ auditFilters, auditPage: 1 })
    get().loadActivityData()
  },

  resetAuditFilters: () => {
    set({ auditFilters: emptyAuditFilters, searchQuery: '', auditPage: 1 })
    get().loadActivityData()
  },

  setAuditPage: (auditPage) => {
    set({ auditPage })
    get().loadActivityData()
  },

  setAuditPageSize: (auditPageSize) => {
    set({ auditPageSize, auditPage: 1 })
    get().loadActivityData()
  },

  setLoginPage: (tab, page) => {
    set((state) => ({ loginPage: { ...state.loginPage, [tab]: page } }))
    get().loadActivityData()
  },

  setLoginPageSize: (loginPageSize) => {
    set({ loginPageSize, loginPage: { errors: 1, successes: 1 } })
    get().loadActivityData()
  },

  loadAuditFilterOptions: async () => {
    const auditFilterOptions = await activityLogService.getAuditFilterOptions()
    set({ auditFilterOptions })
  },

  metricsCache: {},
  successesCache: {},
  errorsCache: {},
  auditsCache: {},
  isLoading: false,
  isBackgroundSyncing: false,
  setIsLoading: (isLoading) => set({ isLoading }),

  /**
   * Loads the KPI cards plus the active tab's table.
   *
   * Only the visible tab is fetched. It used to request all four endpoints on every keystroke,
   * filter change and page turn; now that all three tables filter and count server-side, that
   * was two full scans thrown away per interaction.
   */
  loadActivityData: async (forceShowSkeleton = false) => {
    const state = get()
    const { activityTimeFilter, activeTab, metricsCache } = state

    const loginKey = loginCacheKey(state, activeTab === 'successes' ? 'successes' : 'errors')
    const auditKey = auditCacheKey(state)

    const tabCache = activeTab === 'audits' ? state.auditsCache[auditKey]
      : activeTab === 'successes' ? state.successesCache[loginKey]
      : state.errorsCache[loginKey]

    if (!metricsCache[activityTimeFilter] || !tabCache || forceShowSkeleton) {
      set({ isLoading: true })
    } else {
      set({ isBackgroundSyncing: true })
    }

    try {
      const metricsRequest = activityLogService.getActivityMetrics(activityTimeFilter)

      if (activeTab === 'audits') {
        const [fetchedMetrics, fetchedAudits] = await Promise.all([
          metricsRequest,
          activityLogService.getAuditLogs(
            state.searchQuery, activityTimeFilter, state.auditFilters, state.auditPage, state.auditPageSize
          )
        ])
        set((s) => ({
          metricsCache: { ...s.metricsCache, [activityTimeFilter]: fetchedMetrics },
          auditsCache: { ...s.auditsCache, [auditKey]: fetchedAudits },
          isLoading: false,
          isBackgroundSyncing: false
        }))
        return
      }

      const isSuccesses = activeTab === 'successes'
      const page = state.loginPage[isSuccesses ? 'successes' : 'errors']
      const [fetchedMetrics, fetchedLogins] = await Promise.all([
        metricsRequest,
        isSuccesses
          ? activityLogService.getLoginSuccesses(state.searchQuery, activityTimeFilter, page, state.loginPageSize)
          : activityLogService.getLoginErrors(state.searchQuery, activityTimeFilter, page, state.loginPageSize)
      ])

      set((s) => ({
        metricsCache: { ...s.metricsCache, [activityTimeFilter]: fetchedMetrics },
        successesCache: isSuccesses ? { ...s.successesCache, [loginKey]: fetchedLogins } : s.successesCache,
        errorsCache: isSuccesses ? s.errorsCache : { ...s.errorsCache, [loginKey]: fetchedLogins },
        isLoading: false,
        isBackgroundSyncing: false
      }))
    } catch (err) {
      console.error('Error fetching activity log data:', err)
      set({ isLoading: false, isBackgroundSyncing: false })
    }
  }
}))

/** Rebuilds the audit cache key from current state, so the page can read its own slice. */
export const auditCacheKey = (state: {
  activityTimeFilter: string
  searchQuery: string
  auditFilters: AuditLogFilters
  auditPage: number
  auditPageSize: number
}) => [
  state.activityTimeFilter,
  state.searchQuery,
  state.auditFilters.from ?? '',
  state.auditFilters.to ?? '',
  state.auditFilters.module ?? '',
  state.auditFilters.action ?? '',
  state.auditFilters.userId ?? '',
  state.auditFilters.status ?? '',
  state.auditPage,
  state.auditPageSize
].join('|')

/**
 * Cache key for a sign-in tab.
 *
 * Carries the tab itself plus page and size. The previous key was filter+search only, which was
 * correct while both tabs fetched everything in one shot — with paging it would file every page
 * under the same key and serve page 1 forever.
 */
export const loginCacheKey = (
  state: {
    activityTimeFilter: string
    searchQuery: string
    loginPage: Record<'errors' | 'successes', number>
    loginPageSize: number
  },
  tab: 'errors' | 'successes'
) => [
  tab,
  state.activityTimeFilter,
  state.searchQuery,
  state.loginPage[tab],
  state.loginPageSize
].join('|')

export { emptyAuditPage, emptyLoginPage }


