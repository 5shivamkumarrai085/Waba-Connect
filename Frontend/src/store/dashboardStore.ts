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
  channel?: string
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
  /** Why the last load failed, or null. Shown with a retry instead of a dashboard of zeros. */
  loadError: string | null
  /** When the numbers on screen were fetched. */
  lastUpdatedAt: number | null
  dashboardTimeFilter: string
  setDashboardTimeFilter: (filter: string) => void
  loadDashboardData: (forceShowSkeleton?: boolean) => Promise<void>
  /** Polls as a safety net; `live` (a working real-time connection) slows it right down. */
  startPolling: (live?: boolean) => () => void
}

const emptyMetric: StatMetric = { total: 0, bottom: 0, changePercent: 0 }

// Poll roughly in step with the backend's own cache TTL per filter, so a refresh always has fresh data waiting.
// Only a safety net: while the real-time connection is up, the server's "dashboardChanged" signal
// refreshes the page within seconds of any change, and polling drops to LIVE_POLL_INTERVAL_MS.
const POLL_INTERVAL_MS: Record<string, number> = {
  today: 12000,
  week: 22000,
  month: 47000,
  all: 47000
}
const LIVE_POLL_INTERVAL_MS = 120000

// One request at a time: a burst of signals, polls and same-tab refreshes collapses into the
// request already in flight plus at most one follow-up.
let inFlight: Promise<void> | null = null
let pendingRefresh = false

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
  loadError: null,
  lastUpdatedAt: null,
  dashboardTimeFilter: 'today',
  setDashboardTimeFilter: (filter) => {
    set({ dashboardTimeFilter: filter })
    get().loadDashboardData(true)
  },
  loadDashboardData: async (forceShowSkeleton = false) => {
    if (inFlight && !forceShowSkeleton) {
      pendingRefresh = true
      return inFlight
    }

    const hasCache = get().summary !== null

    if (!hasCache || forceShowSkeleton) {
      set({ isLoading: true })
    } else {
      set({ isBackgroundSyncing: true })
    }

    const run = (async () => {
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
        isBackgroundSyncing: false,
        loadError: null,
        lastUpdatedAt: Date.now()
      })
    } catch (e) {
      const message = e instanceof Error ? e.message : 'The dashboard could not be loaded.'
      // Numbers already on screen stay; the error is shown next to them rather than replacing them.
      set({ isLoading: false, isBackgroundSyncing: false, loadError: message })
    }
    })()

    inFlight = run
    try {
      await run
    } finally {
      inFlight = null
      if (pendingRefresh) {
        pendingRefresh = false
        void get().loadDashboardData(false)
      }
    }
  },
  startPolling: (live = false) => {
    const filter = get().dashboardTimeFilter
    const intervalMs = live ? LIVE_POLL_INTERVAL_MS : (POLL_INTERVAL_MS[filter] ?? POLL_INTERVAL_MS.all)
    const interval = window.setInterval(() => {
      // A hidden tab has nobody looking at it; it catches up when it becomes visible again.
      if (document.visibilityState === 'visible') void get().loadDashboardData(false)
    }, intervalMs)
    return () => window.clearInterval(interval)
  }
}))

export default useDashboardStore;
