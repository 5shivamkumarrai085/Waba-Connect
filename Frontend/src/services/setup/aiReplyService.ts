import { apiClient } from '../apiClient'

export interface AiPrompt {
  id: number
  name: string
  promptText: string
  description?: string | null
  isActive: boolean
  isDefault: boolean
  createdAt: string
}

export interface CannedReply {
  id: number
  title: string
  description: string
  isPublic: boolean
  isActive: boolean
  /** Whether the signed-in user created it. */
  isMine: boolean
  createdAt: string
}

export const aiReplyService = {
  getPrompts: async (): Promise<AiPrompt[]> => {
    const response = await apiClient.get('/setup/ai-prompts')
    return response.data?.data ?? []
  },
  createPrompt: (payload: Partial<AiPrompt>) => apiClient.post('/setup/ai-prompts', payload),
  updatePrompt: (id: number, payload: Partial<AiPrompt>) => apiClient.put(`/setup/ai-prompts/${id}`, payload),
  deletePrompt: (id: number) => apiClient.delete(`/setup/ai-prompts/${id}`),

  /**
   * @param activeOnly set by the chat composer, which should only offer replies that are
   * switched on. The management list passes false so inactive replies stay editable.
   */
  getCannedReplies: async (activeOnly = false): Promise<CannedReply[]> => {
    try {
      const response = await apiClient.request({
        method: 'GET',
        url: '/setup/canned-replies',
        params: { activeOnly }
      })
      return response.data?.data ?? []
    } catch {
      // The chat composer must keep working even if replies can't be loaded.
      return []
    }
  },
  createCannedReply: (payload: Partial<CannedReply>) => apiClient.post('/setup/canned-replies', payload),
  updateCannedReply: (id: number, payload: Partial<CannedReply>) => apiClient.put(`/setup/canned-replies/${id}`, payload),
  toggleCannedReplyPublic: (id: number) => apiClient.patch(`/setup/canned-replies/${id}/toggle-public`),
  deleteCannedReply: (id: number) => apiClient.delete(`/setup/canned-replies/${id}`)
}

export default aiReplyService
