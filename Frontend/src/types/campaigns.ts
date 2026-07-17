// src/types/campaigns.ts

export interface Campaign {
  id: number
  name: string
  templateName: string
  relationType: string // e.g. 'Lead', 'Customer', 'Csv_campaign'
  total: number
  deliveredTo: number
  readBy: number
  failedCount: number
  status: string // e.g. 'Success', 'Paused', 'draft'
  createdAt: string
  scheduledAt?: string
  isDeleted?: boolean
  deletedAt?: string
  deletedBy?: string
}

export interface CampaignStatistics {
  totalLeads: number
  totalLeadsPercent: string
  deliveredCount: number
  deliveredPercent: string
  readCount: number
  readPercent: string
  failedCount: number
  failedPercent: string
}

export interface CampaignRecipient {
  id: number
  contactId: number
  name: string
  phone: string
  message: string
  sentStatus: string // e.g. 'Sent', 'Failed', 'Pending'
  failedReason?: string | null
}

export interface CampaignWizardForm {
  name: string
  relationType: string
  templateName: string
  templateId: number
  recipientsCount: number
  contactsFilterStatus: string
  contactsFilterSource: string
  selectedContactIds: number[]
  selectAllContacts: boolean
  sendImmediately: boolean
  scheduledTime?: string
  variables?: { variableName: string; variableValue: string; mergeField?: string }[]
}
