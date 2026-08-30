import { apiClient } from './apiClient'
import type { OmniSettingsSchema, OmniSettingsValues } from '../types/omniSettings'

/**
 * The OmniConnect settings surface.
 *
 * Both calls return the whole schema. Sections are not independent — a status added under Setup
 * changes another section's options — so a save answers with everything rather than with the
 * section that was written, and the page stays consistent without a second request.
 */
export const omniSettingsService = {
  getSchema: async (): Promise<OmniSettingsSchema> => {
    const response = await apiClient.get('/OmniSettings/schema')
    return response.data?.data
  },

  saveSection: async (sectionKey: string, values: OmniSettingsValues): Promise<OmniSettingsSchema> => {
    const response = await apiClient.put(`/OmniSettings/${sectionKey}`, { values })
    return response.data?.data
  }
}
