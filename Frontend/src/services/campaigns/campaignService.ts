import { apiClient } from '../apiClient'
import type { Campaign, CampaignStatistics, CampaignRecipient, CampaignWizardForm } from '../../types/campaigns'

export const campaignService = {
  getCampaigns: async (): Promise<Campaign[]> => {
    try {
      const response = await apiClient.get('/Campaigns', {
        params: { pageSize: 10000 }
      })
      // Map API response to the format expected by the frontend
      return (response.data?.data?.items || []).map((c: any) => ({
        id: c.id,
        name: c.name,
        templateName: c.templateName,
        relationType: c.relationType,
        total: c.totalRecipients || 0,
        deliveredTo: c.deliveredCount || 0,
        readBy: c.readCount || 0,
        status: c.status,
        createdAt: c.createdAt,
        scheduledAt: c.scheduledAt
      }))
    } catch (error) {
      console.error('Failed to get campaigns:', error)
      return []
    }
  },

  getCampaignDetails: async (id: number): Promise<{ campaign: Campaign; statistics: CampaignStatistics; recipients: CampaignRecipient[] }> => {
    try {
      const [detailsRes, recipientsRes] = await Promise.all([
        apiClient.get(`/Campaigns/${id}`),
        apiClient.get(`/Campaigns/${id}/recipients`, { params: { pageSize: 10000 } })
      ])

      const c = detailsRes.data?.data
      const recs = recipientsRes.data?.data?.items || []

      const campaign: Campaign = {
        id: c.id,
        name: c.name,
        templateName: c.templateName,
        relationType: c.relationType,
        total: c.totalRecipients || 0,
        deliveredTo: c.deliveredCount || 0,
        readBy: c.readCount || 0,
        status: c.status,
        createdAt: c.createdAt,
        scheduledAt: c.scheduledAt
      }

      const total = campaign.total
      const stats: CampaignStatistics = {
        totalLeads: total,
        totalLeadsPercent: campaign.relationType === 'Lead' ? '100% of leads' : '100% of customers',
        deliveredCount: campaign.deliveredTo,
        deliveredPercent: total > 0 ? `${Math.round((campaign.deliveredTo / total) * 100)}%` : '0%',
        readCount: campaign.readBy,
        readPercent: total > 0 ? `${Math.round((campaign.readBy / total) * 100)}%` : '0%',
        failedCount: total - campaign.deliveredTo,
        failedPercent: total > 0 ? `${Math.round(((total - campaign.deliveredTo) / total) * 100)}%` : '0%'
      }

      return {
        campaign,
        statistics: stats,
        recipients: recs.map((r: any) => ({
          id: r.id,
          phone: r.contact?.phone || 'Unknown',
          name: r.contact ? `${r.contact.firstName} ${r.contact.lastName}` : 'Unknown',
          status: r.status,
          deliveredAt: r.deliveredAt || '-',
          readAt: r.readAt || '-',
          failedReason: r.errorDetails || null
        }))
      }
    } catch (error) {
      console.error(`Failed to get campaign details for ${id}:`, error)
      throw error
    }
  },

  createCampaign: async (form: CampaignWizardForm): Promise<Campaign> => {
    const payload = {
      name: form.name,
      templateId: form.templateId,
      relationType: form.relationType,
      scheduleType: form.sendImmediately ? 'Immediate' : 'Scheduled',
      scheduledAt: form.sendImmediately ? null : form.scheduledTime,
      contactIds: form.selectedContactIds
    }

    const response = await apiClient.post('/Campaigns', payload)
    const c = response.data?.data
    return {
      id: c.id,
      name: c.name,
      templateName: c.templateName,
      relationType: c.relationType,
      total: c.totalRecipients || 0,
      deliveredTo: c.deliveredCount || 0,
      readBy: c.readCount || 0,
      status: c.status,
      createdAt: c.createdAt,
      scheduledAt: c.scheduledAt
    }
  },

  updateCampaign: async (_id: number, _form: CampaignWizardForm): Promise<Campaign> => {
    // Backend doesn't support updating campaigns in the provided controller.
    // Throw an error or return dummy. We will throw an error since it shouldn't be called if not supported.
    throw new Error('Update campaign is not implemented in the backend.')
  },

  deleteCampaign: async (id: number): Promise<void> => {
    await apiClient.delete(`/Campaigns/${id}`)
  },

  pauseCampaign: async (id: number): Promise<void> => {
    await apiClient.post(`/Campaigns/${id}/cancel`)
  },

  resumeCampaign: async (_id: number): Promise<void> => {
    throw new Error('Resume campaign is not implemented in the backend.')
  }
}
export default campaignService
