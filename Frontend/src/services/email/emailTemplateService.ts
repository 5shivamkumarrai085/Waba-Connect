import { apiClient } from '../apiClient'
import type { EmailTemplate, EmailTemplatePreview, SaveEmailTemplatePayload } from '../../types/email'

/**
 * Email templates API.
 *
 * Extracted into a service because the Setup page previously called `apiClient` inline. That was
 * fine while this was four seeded rows with an on/off switch; now that the campaign wizard reads
 * the same templates, two screens would otherwise be unwrapping the same envelope in two
 * slightly different ways.
 */
export const emailTemplateService = {
  /**
   * @param enabledOnly true for the campaign template picker, which must not offer a disabled
   * template — selecting one would produce a campaign that fails at dispatch.
   */
  getTemplates: async (enabledOnly = false): Promise<EmailTemplate[]> => {
    try {
      const response = await apiClient.get('/setup/email-templates', {
        params: enabledOnly ? { enabledOnly: true } : undefined
      })
      return response.data?.data ?? []
    } catch {
      return []
    }
  },

  getTemplate: async (id: number): Promise<EmailTemplate | null> => {
    try {
      const response = await apiClient.get(`/setup/email-templates/${id}`)
      return response.data?.data ?? null
    } catch {
      return null
    }
  },

  createTemplate: async (payload: SaveEmailTemplatePayload): Promise<EmailTemplate> => {
    const response = await apiClient.post('/setup/email-templates', payload)
    return response.data?.data
  },

  updateTemplate: async (id: number, payload: SaveEmailTemplatePayload): Promise<EmailTemplate> => {
    const response = await apiClient.put(`/setup/email-templates/${id}`, payload)
    return response.data?.data
  },

  toggleTemplate: async (id: number): Promise<EmailTemplate> => {
    const response = await apiClient.patch(`/setup/email-templates/${id}/toggle`)
    return response.data?.data
  },

  deleteTemplate: async (id: number): Promise<void> => {
    await apiClient.delete(`/setup/email-templates/${id}`)
  },

  /**
   * Renders the template with sample or supplied values.
   *
   * Server-side rather than in the browser, so the preview uses exactly the same renderer the
   * send path does — a client-side approximation would eventually disagree with what actually
   * gets sent, which is the one thing a preview must not do.
   */
  /**
   * Renders a template for a preview pane.
   *
   * @param useSampleData Whether an unfilled variable may be shown as a plausible stand-in.
   * Only the template editor's "Preview with Sample Data" wants this. A campaign preview must
   * not: filling a missing {confirmation_link} with a working-looking example.com URL shows a
   * link that will not be in the delivered mail.
   */
  previewTemplate: async (
    id: number,
    values?: Record<string, string | null>,
    useSampleData = false
  ): Promise<EmailTemplatePreview | null> => {
    try {
      const response = await apiClient.post(`/setup/email-templates/${id}/preview`, {
        values: values ?? null,
        useSampleData
      })
      return response.data?.data ?? null
    } catch {
      return null
    }
  }
}

export default emailTemplateService
