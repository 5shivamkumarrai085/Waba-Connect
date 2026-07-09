import { create } from 'zustand'
import { reportingService } from '../services/reportingService'
import { activityLogService } from '../services/activityLogService'

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
  }
}))

// Activity Logs UI state store
interface ActivityLogStoreState {
  activeTab: 'errors' | 'successes' | 'audits'
  searchQuery: string
  activityTimeFilter: 'today' | 'yesterday' | 'week' | 'month' | 'all'
  
  setActiveTab: (tab: 'errors' | 'successes' | 'audits') => void
  setSearchQuery: (query: string) => void
  setActivityTimeFilter: (filter: 'today' | 'yesterday' | 'week' | 'month' | 'all') => void
  
  // Cache fields
  metricsCache: Record<string, any[]>
  successesCache: Record<string, any[]>
  auditsCache: Record<string, any[]>
  
  isLoading: boolean
  isBackgroundSyncing: boolean
  setIsLoading: (loading: boolean) => void
  loadActivityData: (forceShowSkeleton?: boolean) => Promise<void>
}

export const useActivityLogStore = create<ActivityLogStoreState>((set, get) => ({
  activeTab: 'errors', // Default matches screenshot 4 ("Login Errors")
  searchQuery: '',
  activityTimeFilter: 'month',
  setActiveTab: (activeTab) => set({ activeTab }),
  setSearchQuery: (searchQuery) => {
    set({ searchQuery })
    get().loadActivityData()
  },
  setActivityTimeFilter: (activityTimeFilter) => {
    set({ activityTimeFilter })
    get().loadActivityData()
  },
  metricsCache: {},
  successesCache: {},
  auditsCache: {},
  isLoading: false,
  isBackgroundSyncing: false,
  setIsLoading: (isLoading) => set({ isLoading }),
  loadActivityData: async (forceShowSkeleton = false) => {
    const { activityTimeFilter, searchQuery, metricsCache, successesCache, auditsCache } = get()
    
    // We check if we have cache for the current parameters
    const hasCache = metricsCache[activityTimeFilter] && 
                     successesCache[searchQuery] && 
                     auditsCache[searchQuery]
                     
    if (!hasCache || forceShowSkeleton) {
      set({ isLoading: true })
    } else {
      set({ isBackgroundSyncing: true })
    }

    try {
      const [
        fetchedMetrics,
        fetchedSuccesses,
        , // skipped unused fetchedErrors
        fetchedAudits
      ] = await Promise.all([
        activityLogService.getActivityMetrics(activityTimeFilter),
        activityLogService.getLoginSuccesses(searchQuery),
        activityLogService.getLoginErrors(searchQuery),
        activityLogService.getAuditLogs(searchQuery)
      ])

      set((state) => ({
        metricsCache: { ...state.metricsCache, [activityTimeFilter]: fetchedMetrics },
        successesCache: { ...state.successesCache, [searchQuery]: fetchedSuccesses },
        auditsCache: { ...state.auditsCache, [searchQuery]: fetchedAudits },
        isLoading: false,
        isBackgroundSyncing: false
      }))
    } catch (err) {
      console.error('Error fetching activity log data:', err)
      set({ isLoading: false, isBackgroundSyncing: false })
    }
  }
}))


