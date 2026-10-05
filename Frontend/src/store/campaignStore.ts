// src/store/campaignStore.ts
import { create } from 'zustand'
import { campaignService } from '../services/campaigns/campaignService'
import type { Campaign, CampaignStatistics, CampaignRecipient, CampaignWizardForm, EmailCampaignStats } from '../types/campaigns'
import type { CampaignEventPayload } from '../services/campaigns/campaignHubService'
import { useDashboardStore } from './dashboardStore'

/**
 * Cross-module edges for campaign writes, declared here so every consequence of a campaign
 * change is greppable from the action that causes it. The dashboard counters are derived from
 * campaigns. (Reporting reads live from the server on each run, so it has no cache to clear.)
 */
const propagateCampaignChange = () => {
  useDashboardStore.getState().loadDashboardData(false)
}

interface CampaignStoreState {
  campaigns: Campaign[]
  isLoading: boolean
  searchQuery: string
  
  // Filtering values matching Screenshot 1
  templateFilter: string
  relationTypeFilter: string
  /** A single status to show, e.g. 'AwaitingApproval'; empty for all. */
  statusFilter: string
  createdAtFilter: string
  
  currentPage: number
  pageSize: number
  /** Matching campaigns on the server, across all pages. */
  totalCount: number
  sortKey: string
  sortDescending: boolean
  createdFrom: string
  createdTo: string
  
  // Selected campaign for details view
  selectedCampaign: Campaign | null
  selectedStats: CampaignStatistics | null
  selectedRecipients: CampaignRecipient[] | null
  
  // Wizard input fields
  wizardForm: CampaignWizardForm
  activeStep: number
  
  setSearchQuery: (query: string) => void
  setTemplateFilter: (template: string) => void
  setStatusFilter: (status: string) => void
  setRelationTypeFilter: (relation: string) => void
  setCreatedAtFilter: (datePeriod: string) => void
  setCurrentPage: (page: number) => void
  setPageSize: (size: number) => void
  setSort: (key: string) => void
  setCreatedRange: (from: string, to: string) => void
  
  // Wizard actions
  setWizardForm: (form: Partial<CampaignWizardForm>) => void
  setActiveStep: (step: number) => void
  resetWizard: () => void
  
  // API Actions
  loadCampaigns: () => Promise<void>
  loadCampaignDetails: (id: number) => Promise<void>
  /** Re-reads the viewed campaign's figures without the loading state (live re-sync). */
  refreshCampaignDetails: (id: number) => Promise<void>
  createCampaign: () => Promise<Campaign>
  updateCampaign: (id: number) => Promise<Campaign>
  deleteCampaign: (id: number) => Promise<void>
  toggleCampaignPause: (id: number) => Promise<void>

  /**
   * Applies a real-time SignalR counter delta to the currently-viewed campaign.
   * Called by useCampaignEvents — never triggers a network request.
   *
   * The delta values arrive from CampaignEmailEventProcessor and are signed ints:
   * +1 for a new event, 0 when the event was a duplicate (idempotency guard fired).
   */
  applyEventDelta: (event: CampaignEventPayload) => void
}

const initialWizardForm: CampaignWizardForm = {
  name: '',
  relationType: [],
  // WhatsApp by default, so opening the wizard and ignoring the new first step produces exactly
  // the campaign it produced before the email channel existed.
  channel: 'whatsapp',
  connectionIds: [],
  templateName: '',
  templateId: 0,
  emailTemplateId: undefined,
  emailTemplateName: '',
  senderIdentityId: undefined,
  subjectOverride: '',
  replyToOverride: '',
  attachments: [],
  trackOpens: true,
  trackClicks: true,
  recipientsCount: 0,
  contactsFilterStatus: 'All',
  contactsFilterSource: 'All',
  selectedContactIds: [],
  selectAllContacts: false,
  sendImmediately: true,
  scheduledTime: '',
  selectedSegmentIds: [],
  selectedGroupIds: [],
  recipientLocalTime: false,
  topic: '',
  isTransactional: false
}

/**
 * Applies one real-time counter delta to a campaign. Pure: the same event applied to the same
 * campaign always gives the same result, and a zero delta changes nothing.
 *
 * Email: "delivered to" on the list is the Sent (accepted) figure and "read by" is Opened.
 * WhatsApp: delivered and read come from Meta's receipts (deliveredDelta / readDelta).
 */
const applyCampaignDelta = (c: Campaign, e: CampaignEventPayload): Campaign => {
  const isEmail = c.channel?.toLowerCase() === 'email'
  const sent = (c.sentCount ?? 0) + e.sentDelta
  const failed = c.failedCount + e.failedDelta
  const opened = (c.openedCount ?? 0) + e.openedDelta
  const bounced = (c.emailStats?.bounced ?? 0) + (e.bouncedDelta ?? 0)

  const emailStats: EmailCampaignStats | undefined = isEmail
    ? {
        ...(c.emailStats ?? {
          sent: 0, delivered: 0, bounced: 0, complained: 0, suppressed: 0,
          opened: 0, clicked: 0, replied: 0, unsubscribed: 0, pending: 0, failed: 0,
        }),
        sent,
        delivered: (c.emailStats?.delivered ?? 0) + e.deliveredDelta,
        bounced,
        opened,
        clicked: (c.emailStats?.clicked ?? 0) + e.clickedDelta,
        replied: (c.emailStats?.replied ?? 0) + e.repliedDelta,
        unsubscribed: (c.emailStats?.unsubscribed ?? 0) + e.unsubscribedDelta,
        complained: (c.emailStats?.complained ?? 0) + e.complainedDelta,
        failed,
        pending: Math.max(0, (c.total || 0) - sent - failed - (c.emailStats?.suppressed ?? 0)),
      }
    : c.emailStats

  return {
    ...c,
    status: e.newCampaignStatus ?? c.status,
    deliveredTo: c.deliveredTo + (isEmail ? e.sentDelta : e.deliveredDelta),
    readBy: c.readBy + (isEmail ? e.openedDelta : e.readDelta ?? 0),
    failedCount: failed,
    sentCount: sent,
    openedCount: opened,
    clickedCount: (c.clickedCount ?? 0) + e.clickedDelta,
    repliedCount: (c.repliedCount ?? 0) + e.repliedDelta,
    unsubscribedCount: (c.unsubscribedCount ?? 0) + e.unsubscribedDelta,
    complainedCount: (c.complainedCount ?? 0) + e.complainedDelta,
    emailStats,
  }
}

export const useCampaignStore = create<CampaignStoreState>((set, get) => ({
  campaigns: [],
  isLoading: false,
  searchQuery: '',
  
  templateFilter: 'All',
  relationTypeFilter: 'All',
  statusFilter: '',
  createdAtFilter: '',
  
  currentPage: 1,
  pageSize: 10,
  totalCount: 0,
  sortKey: '',
  sortDescending: true,
  createdFrom: '',
  createdTo: '',
  
  selectedCampaign: null,
  selectedStats: null,
  selectedRecipients: null,
  
  wizardForm: initialWizardForm,
  activeStep: 0,
  
  setSearchQuery: (searchQuery) => set({ searchQuery, currentPage: 1 }),
  setTemplateFilter: (templateFilter) => set({ templateFilter, currentPage: 1 }),
  setStatusFilter: (statusFilter) => set({ statusFilter, currentPage: 1 }),
  setRelationTypeFilter: (relationTypeFilter) => set({ relationTypeFilter, currentPage: 1 }),
  setCreatedAtFilter: (createdAtFilter) => set({ createdAtFilter, currentPage: 1 }),
  setCurrentPage: (currentPage) => set({ currentPage }),
  setPageSize: (pageSize) => set({ pageSize, currentPage: 1 }),
  setSort: (key) => set((state) => ({
    sortKey: key,
    sortDescending: state.sortKey === key ? !state.sortDescending : false,
    currentPage: 1,
  })),
  setCreatedRange: (createdFrom, createdTo) => set({ createdFrom, createdTo, currentPage: 1 }),
  
  setWizardForm: (form) => set((state) => ({
    wizardForm: { ...state.wizardForm, ...form }
  })),
  
  setActiveStep: (activeStep) => set({ activeStep }),
  
  resetWizard: () => set({ wizardForm: initialWizardForm, activeStep: 0 }),
  
  loadCampaigns: async () => {
    const s = get()
    const hasCache = s.campaigns.length > 0
    if (!hasCache) {
      set({ isLoading: true })
    }
    try {
      const page = await campaignService.getCampaignsPage({
        page: s.currentPage,
        pageSize: s.pageSize,
        search: s.searchQuery.trim(),
        status: s.statusFilter,
        template: s.templateFilter,
        relationType: s.relationTypeFilter,
        createdFrom: s.createdFrom,
        createdTo: s.createdTo,
        sortBy: s.sortKey,
        sortDescending: s.sortDescending,
      })
      set({ campaigns: page.items, totalCount: page.totalCount })
    } catch {
      // The list keeps what it had; the page shows the error state from isLoading/empty data.
    } finally {
      set({ isLoading: false })
    }
  },
  
  loadCampaignDetails: async (id) => {
    set({
      isLoading: true,
      selectedCampaign: null,
      selectedStats: null,
      selectedRecipients: null
    })
    try {
      const res = await campaignService.getCampaignDetails(id)
      set({
        selectedCampaign: res.campaign,
        selectedStats: res.statistics,
        selectedRecipients: res.recipients
      })
    } catch (err) {
      // Error handled by loading state
    } finally {
      set({ isLoading: false })
    }
  },
  
  refreshCampaignDetails: async (id) => {
    try {
      const res = await campaignService.getCampaignDetails(id)
      if (get().selectedCampaign?.id === id) {
        set({ selectedCampaign: res.campaign, selectedStats: res.statistics })
      }
    } catch {
      // A failed background refresh keeps the figures already on screen.
    }
  },

  createCampaign: async () => {
    const { wizardForm } = get()
    set({ isLoading: true })
    try {
      // Handle multi-connection: create one campaign per connection
      const connectionIds: number[] = wizardForm.connectionIds ?? []
      if (connectionIds.length > 1) {
        let lastRes: any = null
        for (const connId of connectionIds) {
          const form = {
            ...wizardForm,
            connectionId: connId,
            name: `${wizardForm.name} (${connId})`
          }
          lastRes = await campaignService.createCampaign(form)
        }
        await get().loadCampaigns()
        propagateCampaignChange()
        return lastRes
      }

      // Single connection or default
      const singleForm = {
        ...wizardForm,
        connectionId: connectionIds.length === 1 ? connectionIds[0] : undefined
      }
      const res = await campaignService.createCampaign(singleForm)
      await get().loadCampaigns()
      propagateCampaignChange()
      return res
    } finally {
      set({ isLoading: false })
    }
  },
  
  updateCampaign: async (id) => {
    const { wizardForm } = get()
    set({ isLoading: true })
    try {
      const res = await campaignService.updateCampaign(id, wizardForm)
      // Reload campaigns
      await get().loadCampaigns()
      propagateCampaignChange()
      return res
    } finally {
      set({ isLoading: false })
    }
  },
  
  deleteCampaign: async (id) => {
    set({ isLoading: true })
    try {
      await campaignService.deleteCampaign(id)
      await get().loadCampaigns()
      propagateCampaignChange()
    } finally {
      set({ isLoading: false })
    }
  },
  
  toggleCampaignPause: async (id) => {
    const camp = get().campaigns.find(c => c.id === id) || get().selectedCampaign
    if (!camp) return
    
    set({ isLoading: true })
    try {
      if (camp.status === 'Paused') {
        await campaignService.resumeCampaign(id)
      } else {
        await campaignService.pauseCampaign(id)
      }
      
      // Update selected campaign details if currently viewed
      const viewed = get().selectedCampaign
      if (viewed && viewed.id === id) {
        const details = await campaignService.getCampaignDetails(id)
        set({
          selectedCampaign: details.campaign,
          selectedStats: details.statistics,
          selectedRecipients: details.recipients
        })
      }
      
      await get().loadCampaigns()
      propagateCampaignChange()
    } finally {
      set({ isLoading: false })
    }
  },


  applyEventDelta: (event) => set((state) => {
    const updatedList = state.campaigns.map((cam) => (cam.id === event.campaignId ? applyCampaignDelta(cam, event) : cam))

    const selected = state.selectedCampaign
    if (!selected || selected.id !== event.campaignId) {
      return { campaigns: updatedList }
    }

    const updatedSelected = applyCampaignDelta(selected, event)
    const stats = state.selectedStats
    const total = stats?.totalLeads || updatedSelected.total || 1
    const percent = (n: number) => `${Math.round((n / total) * 100)}%`

    return {
      campaigns: updatedList,
      selectedCampaign: updatedSelected,
      selectedStats: stats
        ? {
            ...stats,
            deliveredCount: updatedSelected.deliveredTo,
            deliveredPercent: percent(updatedSelected.deliveredTo),
            readCount: updatedSelected.readBy,
            readPercent: percent(updatedSelected.readBy),
            failedCount: updatedSelected.failedCount,
            failedPercent: percent(updatedSelected.failedCount),
          }
        : stats,
    }
  }),
}))
export default useCampaignStore
