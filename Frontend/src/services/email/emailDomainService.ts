import { apiClient } from '../apiClient'
import type { EmailSendingDomain } from '../../types/email'

/**
 * Sending-domain (SPF / DKIM / DMARC) API.
 *
 * `refresh` exists because DNS propagation is not instant: after the records are published, the
 * only way to know whether verification has completed is to ask the provider again. The setup
 * screen polls this rather than assuming the records took effect on save.
 */
export const emailDomainService = {
  getDomains: async (emailConfigurationId: number): Promise<EmailSendingDomain[]> => {
    try {
      const response = await apiClient.get('/email/domains', { params: { emailConfigurationId } })
      return response.data?.data ?? []
    } catch {
      return []
    }
  },

  getDomain: async (id: number): Promise<EmailSendingDomain | null> => {
    try {
      const response = await apiClient.get(`/email/domains/${id}`)
      return response.data?.data ?? null
    } catch {
      return null
    }
  },

  /** Registers the domain. The response carries the DNS records to publish. */
  provisionDomain: async (
    emailConfigurationId: number,
    domainName: string
  ): Promise<EmailSendingDomain> => {
    const response = await apiClient.post('/email/domains', { emailConfigurationId, domainName })
    return response.data?.data
  },

  /** Re-reads verification state from the provider. */
  refreshDomain: async (id: number): Promise<EmailSendingDomain> => {
    const response = await apiClient.post(`/email/domains/${id}/refresh`)
    return response.data?.data
  },

  deleteDomain: async (id: number): Promise<void> => {
    await apiClient.delete(`/email/domains/${id}`)
  }
}

export default emailDomainService
