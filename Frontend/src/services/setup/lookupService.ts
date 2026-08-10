import { apiClient } from '../apiClient'

export interface ContactStatusLookup {
  id: number
  /** Immutable identifier stored on contacts. Not editable. */
  value: string
  name: string
  color?: string | null
  isActive: boolean
  isSystem: boolean
  sortOrder: number
  /** Contacts currently using this value — surfaced so deletion isn't a surprise. */
  usageCount: number
}

/** Same shape as ContactStatusLookup — the two lookups are deliberately identical. */
export interface ContactTypeLookup {
  id: number
  /** Immutable identifier stored on contacts. Not editable. */
  value: string
  name: string
  color?: string | null
  isActive: boolean
  isSystem: boolean
  sortOrder: number
  usageCount: number
}

export interface ContactSourceLookup {
  id: number
  value: string
  name: string
  color?: string | null
  isActive: boolean
  isSystem: boolean
  sortOrder: number
  usageCount: number
}

export interface LanguageLookup {
  id: number
  code: string
  name: string
  isActive: boolean
  isDefault: boolean
  sortOrder: number
  translationCount: number
}

export interface TranslationEntry {
  id?: number
  key: string
  value: string
}

const unwrap = <T,>(response: any, fallback: T): T => response.data?.data ?? fallback

export const lookupService = {
  // ── Statuses ─────────────────────────────────────────────────────────
  getStatuses: async (): Promise<ContactStatusLookup[]> => {
    const response = await apiClient.get('/setup/statuses')
    return unwrap(response, [])
  },
  createStatus: (payload: { name: string; color?: string; isActive: boolean; sortOrder: number }) =>
    apiClient.post('/setup/statuses', payload),
  updateStatus: (id: number, payload: { name: string; color?: string; isActive: boolean; sortOrder: number }) =>
    apiClient.put(`/setup/statuses/${id}`, payload),
  deleteStatus: (id: number) => apiClient.delete(`/setup/statuses/${id}`),

  // ── Types ────────────────────────────────────────────────────────────
  getTypes: async (): Promise<ContactTypeLookup[]> => {
    const response = await apiClient.get('/setup/types')
    return unwrap(response, [])
  },
  createType: (payload: { name: string; color?: string; isActive: boolean; sortOrder: number }) =>
    apiClient.post('/setup/types', payload),
  updateType: (id: number, payload: { name: string; color?: string; isActive: boolean; sortOrder: number }) =>
    apiClient.put(`/setup/types/${id}`, payload),
  deleteType: (id: number) => apiClient.delete(`/setup/types/${id}`),

  // ── Sources ──────────────────────────────────────────────────────────
  getSources: async (): Promise<ContactSourceLookup[]> => {
    const response = await apiClient.get('/setup/sources')
    return unwrap(response, [])
  },
  createSource: (payload: { name: string; color?: string; isActive: boolean; sortOrder: number }) =>
    apiClient.post('/setup/sources', payload),
  updateSource: (id: number, payload: { name: string; color?: string; isActive: boolean; sortOrder: number }) =>
    apiClient.put(`/setup/sources/${id}`, payload),
  deleteSource: (id: number) => apiClient.delete(`/setup/sources/${id}`),

  // ── Languages ────────────────────────────────────────────────────────
  getLanguages: async (): Promise<LanguageLookup[]> => {
    const response = await apiClient.get('/setup/languages')
    return unwrap(response, [])
  },
  createLanguage: (payload: { code: string; name: string; isActive: boolean; isDefault: boolean; sortOrder: number }) =>
    apiClient.post('/setup/languages', payload),
  updateLanguage: (id: number, payload: { code: string; name: string; isActive: boolean; isDefault: boolean; sortOrder: number }) =>
    apiClient.put(`/setup/languages/${id}`, payload),
  deleteLanguage: (id: number) => apiClient.delete(`/setup/languages/${id}`),

  // ── Translations ─────────────────────────────────────────────────────
  getTranslations: async (languageId: number): Promise<TranslationEntry[]> => {
    const response = await apiClient.get(`/setup/languages/${languageId}/translations`)
    return unwrap(response, [])
  },
  saveTranslations: (languageId: number, entries: TranslationEntry[]) =>
    apiClient.put(`/setup/languages/${languageId}/translations`, { entries })
}

export default lookupService
