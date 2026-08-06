import { apiClient } from '../apiClient'
import type { Campaign, CampaignStatistics, CampaignRecipient, CampaignWizardForm } from '../../types/campaigns'

const mapCampaign = (c: any): Campaign => ({
  id: c.id,
  name: c.name,
  templateName: c.templateName,
  relationType: c.relationType,
  total: c.totalRecipients || 0,
  deliveredTo: c.deliveredCount || 0,
  readBy: c.readCount || 0,
  failedCount: c.failedCount || 0,
  status: c.status,
  createdAt: c.createdAt,
  scheduledAt: c.scheduledAt,
  isDeleted: c.isDeleted,
  isBulkCampaign: c.isBulkCampaign || false,
  deletedAt: c.deletedAt,
  deletedBy: c.deletedBy,
  connectionId: c.connectionId,
  connectionName: c.connectionName,
  connectionNickname: c.connectionNickname
})

const getApiErrorMessage = (error: unknown): string => {
  const err = error as { response?: { data?: { message?: string; errors?: string[] } }; message?: string }
  const data = err.response?.data
  if (Array.isArray(data?.errors) && data.errors.length > 0) return data.errors.join(', ')
  return data?.message || err.message || 'Request failed.'
}

export const campaignService = {
  getCampaigns: async (): Promise<Campaign[]> => {
    try {
      const response = await apiClient.get('/Campaigns', {
        params: { pageSize: 10000 }
      })
      // Map API response to the format expected by the frontend
      return (response.data?.data?.items || []).map(mapCampaign)
    } catch (error) {
      return []
    }
  },

  getCampaignDetails: async (id: number): Promise<{ campaign: Campaign; statistics: CampaignStatistics; recipients: CampaignRecipient[]; variables?: any[] }> => {
    try {
      const [detailsRes, recipientsRes] = await Promise.all([
        apiClient.get(`/Campaigns/${id}`),
        apiClient.get(`/Campaigns/${id}/recipients`, { params: { pageSize: 10000 } })
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
        recipients: recs.map((r: any) => ({
          id: r.id,
          contactId: r.contactId,
          phone: r.phone || 'Unknown',
          name: r.contactName || 'Unknown',
          message: r.message || '',
          sentStatus: r.status,
          deliveredAt: r.deliveredAt || '-',
          readAt: r.readAt || '-',
          failedReason: r.errorMessage || null
        })),
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

const buildCampaignPayload = (form: CampaignWizardForm, connectionId?: number) => ({
  name: form.name,
  templateId: form.templateId,
  relationType: form.relationType.join(','),
  scheduleType: form.sendImmediately ? 'Immediate' : 'Scheduled',
  scheduledAt: form.sendImmediately || !form.scheduledTime ? null : new Date(form.scheduledTime).toISOString(),
  contactIds: form.selectedContactIds,
  variables: form.variables || [],
  connectionId: connectionId || (form as any).connectionId || null
})
