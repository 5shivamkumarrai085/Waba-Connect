import { apiClient } from '../apiClient'

export interface ContactGroupRecord {
  id: number
  name: string
  description?: string | null
  /** Hex badge colour. Null on groups created before the colour column existed. */
  color?: string | null
  /** Contacts currently in the group — surfaced so a delete isn't a surprise. */
  memberCount: number
  createdAt: string
}

export interface SaveGroupPayload {
  name: string
  description?: string | null
  color?: string | null
}

/**
 * Setup-side administration of contact groups.
 *
 * Talks to the pre-existing /ContactGroups controller rather than a new one: the CRUD and its
 * ContactGroup.* permission gates already existed and were already correct, only the admin
 * screen was missing.
 */
export const groupService = {
  getAll: async (search?: string): Promise<ContactGroupRecord[]> => {
    const response = await apiClient.request({
      method: 'GET',
      url: '/ContactGroups',
      // The endpoint is paged; the admin list is small enough to show whole, and a page size
      // this size is cheaper than wiring a pager for a handful of rows.
      params: { page: 1, pageSize: 200, search: search || undefined }
    })
    return response.data?.data?.items ?? []
  },

  create: (payload: SaveGroupPayload) => apiClient.post('/ContactGroups', payload),

  update: (id: number, payload: SaveGroupPayload) => apiClient.put(`/ContactGroups/${id}`, payload),

  remove: (id: number) => apiClient.delete(`/ContactGroups/${id}`)
}

export default groupService
