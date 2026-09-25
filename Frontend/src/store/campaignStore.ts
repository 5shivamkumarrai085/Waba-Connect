// src/store/campaignStore.ts
import { create } from 'zustand'
import { campaignService } from '../services/campaigns/campaignService'
import { contactService } from '../services/contacts/contactService'
import type { Campaign, CampaignStatistics, CampaignRecipient, CampaignWizardForm, EmailCampaignStats } from '../types/campaigns'
import type { CampaignEventPayload } from '../services/campaigns/campaignHubService'
import { useDashboardStore } from './dashboardStore'
import { useReportingStore } from './zustand'

/**
 * Cross-module edges for campaign writes, declared here so every consequence of a campaign
 * change is greppable from the action that causes it. Both the dashboard counters and the
 * Reporting metrics are derived from campaigns, and Reporting caches per time filter — without
 * the invalidate it would keep serving pre-change numbers for the rest of the session.
 */
const propagateCampaignChange = () => {
  useDashboardStore.getState().loadDashboardData(false)
  useReportingStore.getState().invalidate()
}

interface CampaignStoreState {
  campaigns: Campaign[]
  isLoading: boolean
  searchQuery: string
  
  // Filtering values matching Screenshot 1
  templateFilter: string
  relationTypeFilter: string
  createdAtFilter: string
  
  currentPage: number
  pageSize: number
  
  // Selected campaign for details view
  selectedCampaign: Campaign | null
  selectedStats: CampaignStatistics | null
  selectedRecipients: CampaignRecipient[] | null
  currentDetailsTab: 'queue' | 'executed'
  
  // Wizard input fields
  wizardForm: CampaignWizardForm
  activeStep: number
  
  setSearchQuery: (query: string) => void
  setTemplateFilter: (template: string) => void
  setRelationTypeFilter: (relation: string) => void
  setCreatedAtFilter: (datePeriod: string) => void
  setCurrentPage: (page: number) => void
  setPageSize: (size: number) => void
  
  // Wizard actions
  setWizardForm: (form: Partial<CampaignWizardForm>) => void
  setActiveStep: (step: number) => void
  resetWizard: () => void
  
  // API Actions
  loadCampaigns: () => Promise<void>
  loadCampaignDetails: (id: number) => Promise<void>
  createCampaign: () => Promise<Campaign>
  updateCampaign: (id: number) => Promise<Campaign>
  deleteCampaign: (id: number) => Promise<void>
  toggleCampaignPause: (id: number) => Promise<void>
  setSelectedTab: (tab: 'queue' | 'executed') => void

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
  scheduledTime: ''
}

export const useCampaignStore = create<CampaignStoreState>((set, get) => ({
  campaigns: [],
  isLoading: false,
  searchQuery: '',
  
  templateFilter: 'All',
  relationTypeFilter: 'All',
  createdAtFilter: '',
  
  currentPage: 1,
  pageSize: 10,
  
  selectedCampaign: null,
  selectedStats: null,
  selectedRecipients: null,
  currentDetailsTab: 'queue',
  
  wizardForm: initialWizardForm,
  activeStep: 0,
  
  setSearchQuery: (searchQuery) => set({ searchQuery, currentPage: 1 }),
  setTemplateFilter: (templateFilter) => set({ templateFilter, currentPage: 1 }),
  setRelationTypeFilter: (relationTypeFilter) => set({ relationTypeFilter, currentPage: 1 }),
  setCreatedAtFilter: (createdAtFilter) => set({ createdAtFilter, currentPage: 1 }),
  setCurrentPage: (currentPage) => set({ currentPage }),
  setPageSize: (pageSize) => set({ pageSize, currentPage: 1 }),
  
  setWizardForm: (form) => set((state) => ({
    wizardForm: { ...state.wizardForm, ...form }
  })),
  
  setActiveStep: (activeStep) => set({ activeStep }),
  
  resetWizard: () => set({ wizardForm: initialWizardForm, activeStep: 0 }),
  
  loadCampaigns: async () => {
    set({ isLoading: true })
    try {
      const fetched = await campaignService.getCampaigns()
      set({ campaigns: fetched })
    } catch (err) {
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
  
  createCampaign: async () => {
    let { wizardForm } = get()
    set({ isLoading: true })
    try {
      if (wizardForm.selectAllContacts) {
        // Fetch all contacts dynamically from backend
        const allContacts = await contactService.getContacts()
        
        // Filter based on wizard fields
        const filtered = allContacts.filter(c => {
          // Relation Type Filter
          if (wizardForm.relationType && wizardForm.relationType.length > 0) {
            const selectedTypesLower = wizardForm.relationType.map(rt => rt.toLowerCase())
            if (!selectedTypesLower.includes((c.type || '').toLowerCase())) {
              return false
            }
          }
          
          // Status Filter
          if (wizardForm.contactsFilterStatus && wizardForm.contactsFilterStatus !== 'All') {
            if (c.status?.toLowerCase() !== wizardForm.contactsFilterStatus.toLowerCase()) {
              return false
            }
          }
          
          // Source Filter
          if (wizardForm.contactsFilterSource && wizardForm.contactsFilterSource !== 'All') {
            if (c.source?.toLowerCase() !== wizardForm.contactsFilterSource.toLowerCase()) {
              return false
            }
          }
          
          return true
        })
        
        const contactIds = filtered.map(c => c.id)
        wizardForm = {
          ...wizardForm,
          selectedContactIds: contactIds
        }
      }

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
        const fetched = await campaignService.getCampaigns()
        set({ campaigns: fetched })
        propagateCampaignChange()
        return lastRes
      }

      // Single connection or default
      const singleForm = {
        ...wizardForm,
        connectionId: connectionIds.length === 1 ? connectionIds[0] : undefined
      }
      const res = await campaignService.createCampaign(singleForm)
      const fetched = await campaignService.getCampaigns()
      set({ campaigns: fetched })
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
      const fetched = await campaignService.getCampaigns()
      set({ campaigns: fetched })
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
      const fetched = await campaignService.getCampaigns()
      set({ campaigns: fetched })
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
      
      const fetched = await campaignService.getCampaigns()
      set({ campaigns: fetched })
      propagateCampaignChange()
    } finally {
      set({ isLoading: false })
    }
  },

  setSelectedTab: (currentDetailsTab) => set({ currentDetailsTab }),

  applyEventDelta: (event) => set((state) => {
    // 1. Update the campaign in the main list
    const updatedList = state.campaigns.map((cam) => {
      if (cam.id !== event.campaignId) return cam

      const isEmail = cam.channel?.toLowerCase() === 'email'
      const newDelivered = cam.deliveredTo + (isEmail ? event.sentDelta : event.deliveredDelta)
      const newRead = cam.readBy + (isEmail ? event.openedDelta : 0)
      const newFailed = cam.failedCount + event.failedDelta
      const newSent = (cam.sentCount ?? 0) + event.sentDelta
      const newOpened = (cam.openedCount ?? 0) + event.openedDelta
      const newClicked = (cam.clickedCount ?? 0) + event.clickedDelta
      const newReplied = (cam.repliedCount ?? 0) + event.repliedDelta
      const newUnsubscribed = (cam.unsubscribedCount ?? 0) + event.unsubscribedDelta
      const newComplained = (cam.complainedCount ?? 0) + event.complainedDelta

      const newEmailStats: EmailCampaignStats | undefined = cam.emailStats ? {
        ...cam.emailStats,
        sent: (cam.emailStats.sent ?? 0) + event.sentDelta,
        bounced: (cam.emailStats.bounced ?? 0) + event.bouncedDelta,
        opened: (cam.emailStats.opened ?? 0) + event.openedDelta,
        clicked: (cam.emailStats.clicked ?? 0) + event.clickedDelta,
        replied: (cam.emailStats.replied ?? 0) + event.repliedDelta,
        unsubscribed: (cam.emailStats.unsubscribed ?? 0) + event.unsubscribedDelta,
        complained: (cam.emailStats.complained ?? 0) + event.complainedDelta,
        failed: (cam.emailStats.failed ?? 0) + event.failedDelta,
        pending: Math.max(0, (cam.total || 0) - newSent - newFailed),
      } : (isEmail ? {
        sent: newSent,
        delivered: newDelivered,
        bounced: 0,
        complained: newComplained,
        suppressed: 0,
        opened: newOpened,
        clicked: newClicked,
        replied: newReplied,
        unsubscribed: newUnsubscribed,
        pending: Math.max(0, (cam.total || 0) - newSent - newFailed),
        failed: newFailed,
      } : undefined)

      return {
        ...cam,
        status: event.newCampaignStatus ?? cam.status,
        deliveredTo: newDelivered,
        readBy: newRead,
        failedCount: newFailed,
        sentCount: newSent,
        openedCount: newOpened,
        clickedCount: newClicked,
        repliedCount: newReplied,
        unsubscribedCount: newUnsubscribed,
        complainedCount: newComplained,
        emailStats: newEmailStats,
      }
    })

    // 2. If the currently selected campaign matches, update it too
    let updatedSelected = state.selectedCampaign
    let updatedStats = state.selectedStats

    if (state.selectedCampaign && state.selectedCampaign.id === event.campaignId) {
      const c = state.selectedCampaign
      const isEmail = c.channel?.toLowerCase() === 'email'
      const newDelivered = c.deliveredTo + (isEmail ? event.sentDelta : event.deliveredDelta)
      const newRead = c.readBy + (isEmail ? event.openedDelta : 0)
      const newFailed = c.failedCount + event.failedDelta
      const newSent = (c.sentCount ?? 0) + event.sentDelta
      const newOpened = (c.openedCount ?? 0) + event.openedDelta
      const newClicked = (c.clickedCount ?? 0) + event.clickedDelta
      const newReplied = (c.repliedCount ?? 0) + event.repliedDelta
      const newUnsubscribed = (c.unsubscribedCount ?? 0) + event.unsubscribedDelta
      const newComplained = (c.complainedCount ?? 0) + event.complainedDelta

      const newEmailStats: EmailCampaignStats | undefined = c.emailStats ? {
        ...c.emailStats,
        sent: (c.emailStats.sent ?? 0) + event.sentDelta,
        bounced: (c.emailStats.bounced ?? 0) + event.bouncedDelta,
        opened: (c.emailStats.opened ?? 0) + event.openedDelta,
        clicked: (c.emailStats.clicked ?? 0) + event.clickedDelta,
        replied: (c.emailStats.replied ?? 0) + event.repliedDelta,
        unsubscribed: (c.emailStats.unsubscribed ?? 0) + event.unsubscribedDelta,
        complained: (c.emailStats.complained ?? 0) + event.complainedDelta,
        failed: (c.emailStats.failed ?? 0) + event.failedDelta,
        pending: Math.max(0, (c.total || 0) - newSent - newFailed),
      } : (isEmail ? {
        sent: newSent,
        delivered: newDelivered,
        bounced: 0,
        complained: newComplained,
        suppressed: 0,
        opened: newOpened,
        clicked: newClicked,
        replied: newReplied,
        unsubscribed: newUnsubscribed,
        pending: Math.max(0, (c.total || 0) - newSent - newFailed),
        failed: newFailed,
      } : undefined)

      updatedSelected = {
        ...c,
        status: event.newCampaignStatus ?? c.status,
        deliveredTo: newDelivered,
        readBy: newRead,
        failedCount: newFailed,
        sentCount: newSent,
        openedCount: newOpened,
        clickedCount: newClicked,
        repliedCount: newReplied,
        unsubscribedCount: newUnsubscribed,
        complainedCount: newComplained,
        emailStats: newEmailStats,
      }

      if (updatedStats) {
        const total = updatedStats.totalLeads || c.total || 1
        updatedStats = {
          ...updatedStats,
          deliveredCount: newDelivered,
          deliveredPercent: `${((newDelivered / total) * 100).toFixed(0)}%`,
          readCount: newRead,
          readPercent: `${((newRead / total) * 100).toFixed(0)}%`,
          failedCount: newFailed,
          failedPercent: `${((newFailed / total) * 100).toFixed(0)}%`,
        }
      }
    }

    return {
      campaigns: updatedList,
      selectedCampaign: updatedSelected,
      selectedStats: updatedStats,
    }
  }),
}))
export default useCampaignStore
