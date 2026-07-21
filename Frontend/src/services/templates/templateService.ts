import { apiClient } from '../apiClient'
import type {
  Template,
  TemplateLanguage,
  TemplateCategory,
  TemplateStatus,
  TemplateType
} from '../../types/templates'

export const templateService = {
  getTemplates: async (): Promise<Template[]> => {
    try {
      const response = await apiClient.get('/Templates', {
        params: { pageSize: 10000 }
      })
      return response.data?.data?.items || []
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

  refreshTemplates: async (): Promise<Template[]> => {
    try {
      await apiClient.post('/Templates/sync')
      // After sync, get latest templates
      const response = await apiClient.get('/Templates', {
        params: { pageSize: 10000 }
      })
      return response.data?.data?.items || []
    } catch (error) {
      return []
    }
  }
}
export default templateService
