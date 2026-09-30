import { apiClient } from '../apiClient'
import { fetchAllPages, readPaged, type PagedResult } from '../pagination'
import { toApiChannel } from '../../types/channel'
import type { Campaign, CampaignStatistics, CampaignRecipient, CampaignWizardForm } from '../../types/campaigns'

const mapCampaign = (c: any): Campaign => ({
  id: c.id,
  name: c.name,
  templateName: c.templateName,
  // Defaulted rather than left undefined, so the campaigns list can render a channel column for
  // rows created before the API carried this field.
  channel: c.channel ?? 'WhatsApp',
  emailStats: c.emailStats ?? undefined,
  relationType: c.relationType,
  total: c.totalRecipients || 0,
  deliveredTo: c.deliveredCount || 0,
  readBy: c.readCount || 0,
  failedCount: c.failedCount || 0,
  sentCount: c.sentCount ?? c.emailStats?.sent ?? 0,
  openedCount: c.openedCount ?? c.emailStats?.opened ?? 0,
  clickedCount: c.clickedCount ?? c.emailStats?.clicked ?? 0,
  repliedCount: c.repliedCount ?? c.emailStats?.replied ?? 0,
  unsubscribedCount: c.unsubscribedCount ?? c.emailStats?.unsubscribed ?? 0,
  complainedCount: c.complainedCount ?? c.emailStats?.complained ?? 0,
  status: c.status,
  createdAt: c.createdAt,
  scheduledAt: c.scheduledAt,
  isDeleted: c.isDeleted,
  isBulkCampaign: c.isBulkCampaign || false,
  deletedAt: c.deletedAt,
  deletedBy: c.deletedBy,
  connectionId: c.connectionId,
  connectionName: c.connectionName,
  connectionNickname: c.connectionNickname,
  reportsDelivery: c.reportsDelivery ?? true,
  approval: c.approval ?? null,
  retryableCount: c.retryableCount ?? 0,
  skippedCount: c.skippedCount ?? 0,
  pausedReason: c.pausedReason ?? null,
  abTest: c.abTest ?? null,
  followUps: c.followUps ?? [],
  parentCampaignId: c.parentCampaignId ?? null,
  topic: c.topic ?? null,
  isTransactional: c.isTransactional ?? false,
  localSendAt: c.localSendAt ?? null,
  retryRuns: c.retryRuns ?? 0,
  maxRetryRuns: c.maxRetryRuns ?? 0
})

const mapRecipient = (r: any): CampaignRecipient => ({
  id: r.id,
  contactId: r.contactId,
  phone: r.phone || 'Unknown',
  email: r.email || '',
  name: r.contactName || 'Unknown',
  message: r.message || '',
  sentStatus: r.status,
  deliveredAt: r.deliveredAt || '-',
  readAt: r.readAt || '-',
  openedAt: r.openedAt || '-',
  failedReason: r.errorMessage || null
})

export interface PreflightItem {
  key: string
  level: 'pass' | 'warn' | 'fail'
  title: string
  detail: string
}

/** Clicks on one link of an email campaign. */
export interface LinkClicks {
  url: string
  totalClicks: number
  uniqueClickers: number
  firstClickAt: string
  lastClickAt: string
}

export interface PrecheckRequest {
  channel: 'Email' | 'WhatsApp'
  emailTemplateId?: number
  senderIdentityId?: number
  subjectOverride: string | null
  templateId?: number
  isTransactional: boolean
  variableNames: string[]
}

const getApiErrorMessage = (error: unknown): string => {
  const err = error as { response?: { data?: { message?: string; errors?: string[] } }; message?: string }
  const data = err.response?.data
  if (Array.isArray(data?.errors) && data.errors.length > 0) return data.errors.join(', ')
  return data?.message || err.message || 'Request failed.'
}

/** Server-side list query: filtering, sorting and paging all happen in the database. */
export interface CampaignListQuery {
  page: number
  pageSize: number
  search?: string
  /** A single status, e.g. 'AwaitingApproval'. Empty means every status. */
  status?: string
  template?: string
  relationType?: string
  createdFrom?: string
  createdTo?: string
  sortBy?: string
  sortDescending?: boolean
}

export const campaignService = {
  getCampaignsPage: async (query: CampaignListQuery): Promise<PagedResult<Campaign>> => {
    const response = await apiClient.get('/Campaigns', {
      params: {
        page: query.page,
        pageSize: query.pageSize,
        search: query.search || undefined,
        status: query.status || undefined,
        template: query.template && query.template !== 'All' ? query.template : undefined,
        relationType: query.relationType && query.relationType !== 'All' ? query.relationType : undefined,
        createdFrom: query.createdFrom || undefined,
        createdTo: query.createdTo || undefined,
        sortBy: query.sortBy || undefined,
        sortDescending: query.sortDescending ?? true,
      },
    })
    return readPaged(response.data, mapCampaign)
  },

  /** Every campaign, for screens that need the whole set; bounded. */
  getCampaigns: async (): Promise<Campaign[]> => {
    try {
      return await fetchAllPages('/Campaigns', {}, mapCampaign, 2_000)
    } catch (error) {
      return []
    }
  },

  /** One page of a campaign's recipients, paged on the server. */
  getRecipientsPage: async (id: number, page: number, pageSize: number): Promise<PagedResult<CampaignRecipient>> => {
    const response = await apiClient.get(`/Campaigns/${id}/recipients`, { params: { page, pageSize } })
    return readPaged(response.data, mapRecipient)
  },

  getCampaignDetails: async (id: number): Promise<{ campaign: Campaign; statistics: CampaignStatistics; recipients: CampaignRecipient[]; variables?: any[] }> => {
    try {
      // The first page of recipients only; the table pages through the rest on demand.
      const [detailsRes, recipientsRes] = await Promise.all([
        apiClient.get(`/Campaigns/${id}`),
        apiClient.get(`/Campaigns/${id}/recipients`, { params: { page: 1, pageSize: 25 } })
      ])

      const c = detailsRes.data?.data
      const recs = recipientsRes.data?.data?.items || []

      const campaign: Campaign = mapCampaign(c)

      const total = campaign.total
      const stats: CampaignStatistics = {
        totalLeads: total,
        totalLeadsPercent: campaign.relationType === 'Lead' ? '100% of leads' : '100% of customers',
        deliveredCount: campaign.deliveredTo,
        deliveredPercent: total > 0 ? `${Math.round((campaign.deliveredTo / total) * 100)}%` : '0%',
        readCount: campaign.readBy,
        readPercent: total > 0 ? `${Math.round((campaign.readBy / total) * 100)}%` : '0%',
        failedCount: campaign.failedCount,
        failedPercent: total > 0 ? `${Math.round((campaign.failedCount / total) * 100)}%` : '0%'
      }

      return {
        campaign,
        statistics: stats,
        recipients: recs.map(mapRecipient),
        variables: c.variables || []
      }
    } catch (error) {
      throw error
    }
  },

  createCampaign: async (form: CampaignWizardForm): Promise<Campaign> => {
    const payload = buildCampaignPayload(form)

    const response = await apiClient.post('/Campaigns', payload)
    const c = response.data?.data
    return mapCampaign(c)
  },

  updateCampaign: async (id: number, form: CampaignWizardForm): Promise<Campaign> => {
    try {
      const response = await apiClient.put(`/Campaigns/${id}`, buildCampaignPayload(form))
      return mapCampaign(response.data?.data)
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  deleteCampaign: async (id: number): Promise<void> => {
    await apiClient.delete(`/Campaigns/${id}`)
  },

  pauseCampaign: async (id: number): Promise<Campaign> => {
    try {
      const response = await apiClient.post(`/Campaigns/${id}/pause`)
      return mapCampaign(response.data?.data)
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  /** Stops a scheduled, running, paused or awaiting-approval campaign. */
  cancelCampaign: async (id: number): Promise<Campaign> => {
    try {
      const response = await apiClient.post(`/Campaigns/${id}/cancel`)
      return mapCampaign(response.data?.data)
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  /** A/B tests: pick the winner now (or a given variant) and send it to everyone held back. */
  decideAbTest: async (id: number, variantId?: number): Promise<void> => {
    try {
      await apiClient.post(`/Campaigns/${id}/ab-test/decide`, { variantId: variantId ?? null })
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  /** Re-sends to a finished campaign's failed recipients. Returns how many were queued. */
  retryFailed: async (id: number): Promise<number> => {
    try {
      const response = await apiClient.post(`/Campaigns/${id}/retry`)
      return Number(response.data?.data?.recipientCount ?? 0)
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  getLinkReport: async (campaignId: number): Promise<LinkClicks[]> => {
    try {
      const response = await apiClient.get(`/Campaigns/${campaignId}/links`)
      return (response.data?.data ?? []) as LinkClicks[]
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  /** DNS and content checks for a campaign before it is created. */
  precheck: async (request: PrecheckRequest): Promise<PreflightItem[]> => {
    try {
      const response = await apiClient.post('/Campaigns/precheck', request)
      return (response.data?.data ?? []) as PreflightItem[]
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  /** Sends the rendered email to a few addresses. Returns the server's one-line outcome. */
  sendProof: async (request: { emailTemplateId: number; senderIdentityId: number; subjectOverride: string | null; toAddresses: string[] }): Promise<string> => {
    try {
      const response = await apiClient.post('/Campaigns/proof', request)
      return response.data?.message ?? 'Proof sent.'
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  /** Maker-checker: approve a parked campaign (the approver cannot be its maker). */
  approveCampaign: async (id: number, comment?: string): Promise<Campaign> => {
    try {
      const response = await apiClient.post(`/Campaigns/${id}/approve`, { comment })
      return mapCampaign(response.data?.data)
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  /** Maker-checker: reject a parked campaign. A reason is required. */
  rejectCampaign: async (id: number, comment: string): Promise<Campaign> => {
    try {
      const response = await apiClient.post(`/Campaigns/${id}/reject`, { comment })
      return mapCampaign(response.data?.data)
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  /** How many campaigns are waiting for approval (for the list badge). */
  getPendingApprovalCount: async (): Promise<number> => {
    const response = await apiClient.get('/Campaigns/pending-approval/count')
    return Number(response.data?.data ?? 0)
  },

  resumeCampaign: async (id: number): Promise<Campaign> => {
    try {
      const response = await apiClient.post(`/Campaigns/${id}/resume`)
      return mapCampaign(response.data?.data)
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  uploadFile: async (file: File): Promise<{ url: string; fileName: string }> => {
    const formData = new FormData()
    formData.append('file', file)
    const response = await apiClient.post('/Campaigns/upload', formData, {
      headers: {
        'Content-Type': 'multipart/form-data'
      }
    })
    return response.data?.data
  },

  checkNameExists: async (name: string, excludeId?: number): Promise<boolean> => {
    try {
      const response = await apiClient.get('/Campaigns/exists', {
        params: { name, excludeId }
      })
      return response.data?.data || false
    } catch (error) {
      return false
    }
  }
}
export default campaignService

/**
 * Builds the create/update body.
 *
 * The email fields are only included for an email campaign. Sending them as nulls on a WhatsApp
 * campaign would work, but it would also mean every existing request body changed shape — and the
 * point of defaulting `channel` to WhatsApp is that nothing about the previous behaviour moves.
 */
const buildCampaignPayload = (form: CampaignWizardForm, connectionId?: number) => {
  const isEmail = form.channel === 'email'

  const base = {
    name: form.name,
    // The API spells it 'WhatsApp' / 'Email'; the frontend keys are lowercase.
    channel: toApiChannel(form.channel ?? 'whatsapp'),
    relationType: form.relationType.join(','),
    scheduleType: form.sendImmediately ? 'Immediate' : form.recipientLocalTime ? 'RecipientLocalTime' : 'Scheduled',
    scheduledAt:
      form.sendImmediately || form.recipientLocalTime || !form.scheduledTime ? null : new Date(form.scheduledTime).toISOString(),
    // A wall-clock value with no offset: "09:30 on 3 October" wherever each recipient is.
    localSendAt: !form.sendImmediately && form.recipientLocalTime && form.scheduledTime ? `${form.scheduledTime}:00` : null,
    topic: form.topic || null,
    isTransactional: form.isTransactional ?? false,
    overridePrecheck: form.overridePrecheck ?? false,
    abTest: form.abTest?.enabled
      ? {
          percent: form.abTest.percent,
          metric: form.abTest.metric || null,
          decideAfterHours: form.abTest.decideAfterHours,
          variants: form.abTest.variants.map(v => ({
            templateId: v.templateId ?? null,
            emailTemplateId: v.emailTemplateId ?? null,
            subjectOverride: v.subjectOverride || null,
          })),
        }
      : null,
    followUps: (form.followUps ?? []).map(f => ({
      condition: f.condition,
      delayHours: f.delayHours,
      action: f.action,
      channel: f.channel,
      templateId: f.templateId ?? null,
      emailTemplateId: f.emailTemplateId ?? null,
      senderIdentityId: f.senderIdentityId ?? null,
      connectionId: f.connectionId ?? null,
      subjectOverride: f.subjectOverride || null,
      tag: f.tag || null,
    })),
    // "Select all" is resolved by the server from the filters, never by downloading every
    // contact into the browser.
    contactIds: form.selectAllContacts ? [] : form.selectedContactIds,
    segmentIds: form.selectAllContacts ? [] : form.selectedSegmentIds ?? [],
    groupIds: form.selectAllContacts ? [] : form.selectedGroupIds ?? [],
    selectAllContacts: !!form.selectAllContacts,
    contactStatus: form.selectAllContacts ? form.contactsFilterStatus : undefined,
    contactSource: form.selectAllContacts ? form.contactsFilterSource : undefined,
    variables: form.variables || [],
    connectionId: connectionId ?? form.connectionIds?.[0] ?? null
  }

  if (!isEmail) {
    return { ...base, templateId: form.templateId }
  }

  return {
    ...base,
    // Still sent, because the field is non-nullable on the request DTO — the server ignores it
    // for an email campaign and stores null.
    templateId: 0,
    emailTemplateId: form.emailTemplateId,
    senderIdentityId: form.senderIdentityId,
    subjectOverride: form.subjectOverride?.trim() || undefined,
    replyToOverride: form.replyToOverride?.trim() || undefined,
    attachments: form.attachments?.length ? form.attachments : undefined,
    trackOpens: form.trackOpens ?? true,
    trackClicks: form.trackClicks ?? true
  }
}
