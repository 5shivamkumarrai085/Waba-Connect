import { fetchAllPages } from '../pagination'
import { apiClient } from '../apiClient'
import type {
  Template,
  TemplateLanguage,
  TemplateCategory,
  TemplateStatus,
  TemplateType,
  CreateTemplateInput
} from '../../types/templates'

const getApiErrorMessage = (error: unknown): string => {
  const err = error as { response?: { data?: { message?: string; errors?: string[] } }; message?: string }
  const data = err.response?.data
  if (Array.isArray(data?.errors) && data.errors.length > 0) return data.errors.join(', ')
  return data?.message || err.message || 'The request failed.'
}

export const templateService = {
  getTemplates: async (): Promise<Template[]> => {
    try {
      return await fetchAllPages<Template>('/Templates')
    } catch (error) {
      return []
    }
  },

  getTemplatesByConnection: async (connectionId: number): Promise<Template[]> => {
    try {
      const response = await apiClient.get(`/Templates/by-connection/${connectionId}`)
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getTemplateLanguages: async (): Promise<TemplateLanguage[]> => {
    try {
      const response = await apiClient.get('/Templates/languages')
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getTemplateCategories: async (): Promise<TemplateCategory[]> => {
    try {
      const response = await apiClient.get('/Templates/categories')
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getTemplateStatuses: async (): Promise<TemplateStatus[]> => {
    try {
      const response = await apiClient.get('/Templates/statuses')
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getTemplateTypes: async (): Promise<TemplateType[]> => {
    try {
      const response = await apiClient.get('/Templates/types')
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  /** Saves a template here (not yet on Meta). */
  createTemplate: async (input: CreateTemplateInput): Promise<Template> => {
    try {
      const response = await apiClient.post('/Templates', { ...input, templateType: 'Text' })
      return response.data?.data
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  updateTemplate: async (id: number, input: CreateTemplateInput): Promise<Template> => {
    try {
      const response = await apiClient.put(`/Templates/${id}`, { ...input, templateType: 'Text' })
      return response.data?.data
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  /** Sends a saved template to Meta for review, on a connection's WhatsApp Business Account. */
  submitTemplate: async (id: number, connectionId: number): Promise<Template> => {
    try {
      const response = await apiClient.post(`/Templates/${id}/submit`, { connectionId })
      return response.data?.data
    } catch (error) {
      throw new Error(getApiErrorMessage(error))
    }
  },

  refreshTemplates: async (): Promise<Template[]> => {
    // Unlike the other read-only methods above, a failure here must be surfaced (not swallowed to []) —
    // this is the one call site (templateStore.refreshTemplates) that needs to tell a real sync
    // failure apart from "zero templates" so the UI doesn't show a false success toast.
    await apiClient.post('/Templates/sync')
    return fetchAllPages<Template>('/Templates')
  }
}
export default templateService
