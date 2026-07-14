import { create } from 'zustand'
import { dashboardService } from '../services/dashboard/dashboardService'
import { templateService } from '../services/templates/templateService'

interface DashboardState {
  summary: any
  metrics: {
    messages: { total: number; today: number }
    contacts: { total: number; active: number }
    campaigns: { total: number; active: number }
    templates: { total: number; approved: number }
  }
  isLoading: boolean
  isBackgroundSyncing: boolean
  dashboardTimeFilter: string
  setDashboardTimeFilter: (filter: string) => void
  loadDashboardData: (forceShowSkeleton?: boolean) => Promise<void>
}

export const useDashboardStore = create<DashboardState>((set, get) => ({
  summary: null,
  metrics: {
    messages: { total: 0, today: 0 },
    contacts: { total: 0, active: 0 },
    campaigns: { total: 0, active: 0 },
    templates: { total: 0, approved: 0 }
  },
  isLoading: false,
  isBackgroundSyncing: false,
  dashboardTimeFilter: 'today',
  setDashboardTimeFilter: (filter) => {
    set({ dashboardTimeFilter: filter })
    get().loadDashboardData(true)
  },
  loadDashboardData: async (forceShowSkeleton = false) => {
    const hasCache = get().summary !== null
    
    if (!hasCache || forceShowSkeleton) {
      set({ isLoading: true })
    } else {
      set({ isBackgroundSyncing: true })
    }

    try {
      const summaryRes = await dashboardService.getSummary(get().dashboardTimeFilter)
      const templatesRes = await templateService.getTemplates()

      const sum = summaryRes?.data || {}
      const templates = templatesRes || []
      
      // Local filtering of templates by time
      const filter = get().dashboardTimeFilter
      let filteredTemplates = templates
      if (filter !== 'all') {
        const now = new Date()
        let cutoff = new Date()
        if (filter === 'today') {
          cutoff = new Date(now.setHours(0,0,0,0))
        } else if (filter === 'week') {
          cutoff = new Date(now.setDate(now.getDate() - 7))
        } else if (filter === 'month') {
          cutoff = new Date(now.setMonth(now.getMonth() - 1))
        }
        filteredTemplates = templates.filter((t: any) => {
          if (!t.createdAt) return false
          return new Date(t.createdAt) >= cutoff
        })
      }
      const approvedTemplates = filteredTemplates.filter((t: any) => t.status === 'APPROVED').length

      set({
        summary: sum,
        metrics: {
          messages: {
            total: sum.messagesSent || 0,
            today: sum.hourlyChartData ? sum.hourlyChartData.reduce((acc: number, val: any) => acc + (val.sent || 0), 0) : 0
          },
          contacts: {
            total: sum.totalContacts || 0,
            active: sum.totalContacts || 0 
          },
          campaigns: {
            total: sum.totalCampaigns || 0,
            active: (sum.recentCampaigns || []).filter((c: any) => c.status === 'Running' || c.status === 'Scheduled' || c.status === 'Sending').length || 0
          },
          templates: {
            total: filteredTemplates.length || 0,
            approved: approvedTemplates
          }
        },
        isLoading: false,
        isBackgroundSyncing: false
      })
    } catch (e) {
      console.error("Failed to load dashboard data", e)
      set({ isLoading: false, isBackgroundSyncing: false })
    }
  }
}))

export default useDashboardStore;
