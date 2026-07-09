// src/store/campaignStore.ts
import { create } from 'zustand'
import { campaignService } from '../services/campaigns/campaignService'
import { contactService } from '../services/contacts/contactService'
import type { Campaign, CampaignStatistics, CampaignRecipient, CampaignWizardForm } from '../types/campaigns'

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
}

const initialWizardForm: CampaignWizardForm = {
  name: '',
  relationType: '',
  templateName: '',
  templateId: 0,
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
      console.error('Error loading campaigns:', err)
    } finally {
      set({ isLoading: false })
    }
  },
  
  loadCampaignDetails: async (id) => {
    set({ isLoading: true })
    try {
      const res = await campaignService.getCampaignDetails(id)
      set({
        selectedCampaign: res.campaign,
        selectedStats: res.statistics,
        selectedRecipients: res.recipients
      })
    } catch (err) {
      console.error('Error loading campaign details:', err)
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
          if (wizardForm.relationType && wizardForm.relationType !== 'All') {
            if (c.type?.toLowerCase() !== wizardForm.relationType.toLowerCase()) {
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

      const res = await campaignService.createCampaign(wizardForm)
      // Reload campaigns
      const fetched = await campaignService.getCampaigns()
      set({ campaigns: fetched })
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
    } finally {
      set({ isLoading: false })
    }
  },
  
  toggleCampaignPause: async (id) => {
    const camp = get().campaigns.find(c => c.id === id)
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
          selectedStats: details.statistics
        })
      }
      
      const fetched = await campaignService.getCampaigns()
      set({ campaigns: fetched })
    } finally {
      set({ isLoading: false })
    }
  },
  
  setSelectedTab: (currentDetailsTab) => set({ currentDetailsTab })
}))
export default useCampaignStore
