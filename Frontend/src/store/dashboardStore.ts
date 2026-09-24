import { create } from 'zustand'
import { dashboardService } from '../services/dashboard/dashboardService'
import type { ChannelBreakdownRow } from '../components/ChannelInsights/ChannelInsightsCard'

interface StatMetric {
  total: number
  bottom: number
  changePercent: number
}

interface RateBreakdown {
  delivered?: number
  failed?: number
  pending?: number
  read?: number
  unread?: number
  notDelivered?: number
  deliveredPercent?: number
  failedPercent?: number
  pendingPercent?: number
  readPercent?: number
  unreadPercent?: number
  notDeliveredPercent?: number
}

interface TopCampaign {
  id: number
  name: string
  createdAt: string
  status: string
  messages: number
  delivered: number
  deliveryRate: number
  readRate: number
}

interface ActivityItem {
  type: 'campaign' | 'contact' | 'template' | 'botflow'
  title: string
  subtitle: string | null
  timestamp: string
}

interface DashboardState {
  summary: any
  metrics: {
    messages: StatMetric
    contacts: StatMetric
    campaigns: StatMetric
    templates: StatMetric
  }
  deliveryBreakdown: RateBreakdown
  readBreakdown: RateBreakdown
  topCampaigns: TopCampaign[]
  recentActivity: ActivityItem[]
  channelBreakdown: ChannelBreakdownRow[]
  businessName: string | null
  isLoading: boolean
  isBackgroundSyncing: boolean
  dashboardTimeFilter: string
  setDashboardTimeFilter: (filter: string) => void
  loadDashboardData: (forceShowSkeleton?: boolean) => Promise<void>
  startPolling: () => () => void
}

const emptyMetric: StatMetric = { total: 0, bottom: 0, changePercent: 0 }

// Poll roughly in step with the backend's own cache TTL per filter, so a refresh always has fresh data waiting.
// This is only a safety net for changes made outside the current tab (webhook status updates, another admin) —
// same-tab mutations (contacts/campaigns/templates/bots) trigger an immediate silent refresh of their own.
const POLL_INTERVAL_MS: Record<string, number> = {
  today: 12000,
  week: 22000,
  month: 47000,
  all: 47000
}

export const useDashboardStore = create<DashboardState>((set, get) => ({
  summary: null,
  metrics: {
    messages: emptyMetric,
    contacts: emptyMetric,
    campaigns: emptyMetric,
    templates: emptyMetric
  },
  deliveryBreakdown: {},
  readBreakdown: {},
  topCampaigns: [],
  channelBreakdown: [],
  recentActivity: [],
  businessName: null,
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
      const sum = summaryRes?.data || {}
      const prev = sum.previousPeriod || {}

      set({
        summary: sum,
        metrics: {
          messages: {
            total: sum.messagesSent || 0,
            bottom: sum.messagesDelivered || 0,
            changePercent: prev.messagesChangePercent ?? 0,
          },
          contacts: {
            total: sum.totalContacts || 0,
            bottom: sum.contactsActive || 0,
            changePercent: prev.contactsChangePercent ?? 0,
          },
          campaigns: {
            total: sum.totalCampaigns || 0,
            bottom: sum.campaignsActive || 0,
            changePercent: prev.campaignsChangePercent ?? 0,
          },
          templates: {
            total: sum.templatesTotal || 0,
            bottom: sum.templatesApproved || 0,
            changePercent: prev.templatesChangePercent ?? 0,
          }
        },
        deliveryBreakdown: sum.deliveryBreakdown || {},
        readBreakdown: sum.readBreakdown || {},
        topCampaigns: sum.topCampaigns || [],
        recentActivity: sum.recentActivity || [],
        channelBreakdown: sum.channelBreakdown || [],
        businessName: sum.businessName || null,
        isLoading: false,
        isBackgroundSyncing: false
      })
    } catch (e) {
      console.error("Failed to load dashboard data", e)
      set({ isLoading: false, isBackgroundSyncing: false })
    }
  },
  startPolling: () => {
    const filter = get().dashboardTimeFilter
    const intervalMs = POLL_INTERVAL_MS[filter] ?? POLL_INTERVAL_MS.all
    const interval = window.setInterval(() => {
      get().loadDashboardData(false)
    }, intervalMs)
    return () => window.clearInterval(interval)
  }
}))

export default useDashboardStore;
