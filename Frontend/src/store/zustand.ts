import { create } from 'zustand'

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
interface ReportingUIState {
  isLoading: boolean
  setIsLoading: (loading: boolean) => void
}

export const useReportingStore = create<ReportingUIState>((set) => ({
  isLoading: false,
  setIsLoading: (isLoading) => set({ isLoading }),
}))

// Activity Logs UI state store
interface ActivityLogUIState {
  activeTab: 'errors' | 'successes' | 'audits'
  searchQuery: string
  isLoading: boolean
  setActiveTab: (tab: 'errors' | 'successes' | 'audits') => void
  setSearchQuery: (query: string) => void
  setIsLoading: (loading: boolean) => void
}

export const useActivityLogStore = create<ActivityLogUIState>((set) => ({
  activeTab: 'errors', // Default matches screenshot 4 ("Login Errors")
  searchQuery: '',
  isLoading: false,
  setActiveTab: (activeTab) => set({ activeTab }),
  setSearchQuery: (searchQuery) => set({ searchQuery }),
  setIsLoading: (isLoading) => set({ isLoading }),
}))

