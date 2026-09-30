import { apiClient } from '../apiClient'
import { getErrorMessage } from '../../utils/errorHelper'

export interface SegmentRule {
  field: string
  op: string
  value?: string
  values?: string[]
  days?: number
}

export interface SegmentRuleGroup {
  match: 'all' | 'any'
  rules: SegmentRule[]
  groups: SegmentRuleGroup[]
}

export interface Segment {
  id: number
  name: string
  description?: string | null
  rules: SegmentRuleGroup
  cachedCount?: number | null
  countedAt?: string | null
  createdAt: string
  updatedAt?: string | null
}

export interface SegmentPreview {
  count: number
  sample: { id: number; name: string; phone?: string | null; email?: string | null }[]
}

const fail = (error: unknown, fallback: string): never => {
  throw new Error(getErrorMessage(error, fallback))
}

export const segmentService = {
  list: async (params: { page?: number; pageSize?: number; search?: string } = {}): Promise<{ items: Segment[]; totalCount: number }> => {
    const response = await apiClient.get('/segments', { params: { page: params.page ?? 1, pageSize: params.pageSize ?? 50, search: params.search || undefined } })
    const data = response.data?.data ?? {}
    return { items: data.items ?? [], totalCount: data.totalCount ?? 0 }
  },

  get: async (id: number): Promise<Segment> => {
    const response = await apiClient.get(`/segments/${id}`)
    return response.data?.data
  },

  preview: async (rules: SegmentRuleGroup, signal?: AbortSignal): Promise<SegmentPreview> => {
    try {
      const response = await apiClient.post('/segments/preview', rules, { signal })
      return response.data?.data
    } catch (error) {
      return fail(error, 'Could not count the segment.')
    }
  },

  save: async (id: number | null, payload: { name: string; description?: string; rules: SegmentRuleGroup }): Promise<Segment> => {
    try {
      const response = id
        ? await apiClient.put(`/segments/${id}`, payload)
        : await apiClient.post('/segments', payload)
      return response.data?.data
    } catch (error) {
      return fail(error, 'Could not save the segment.')
    }
  },

  remove: async (id: number): Promise<void> => {
    try {
      await apiClient.delete(`/segments/${id}`)
    } catch (error) {
      fail(error, 'Could not delete the segment.')
    }
  },
}
