import { apiClient } from '../apiClient'
import { getErrorMessage } from '../../utils/errorHelper'

export interface ContactConsent {
  channel: string
  topic: string
  status: 'OptedIn' | 'OptedOut' | string
  source: string
  capturedAt: string
  updatedAt: string
}

export interface ConsentEvent {
  channel: string
  topic: string
  status: string
  source: string
  occurredAt: string
  proofJson?: string | null
  ipAddress?: string | null
  actorUserId?: number | null
}

export const consentService = {
  get: async (contactId: number): Promise<{ current: ContactConsent[]; history: ConsentEvent[] }> => {
    const response = await apiClient.get(`/contacts/${contactId}/consents`)
    const data = response.data?.data ?? {}
    return { current: data.current ?? [], history: data.history ?? [] }
  },

  /** Records an opt-in or opt-out on the contact's behalf. The note is kept as the evidence. */
  set: async (contactId: number, payload: { channel: string; topic: string; status: 'OptedIn' | 'OptedOut'; note?: string }): Promise<boolean> => {
    try {
      const response = await apiClient.post(`/contacts/${contactId}/consents`, payload)
      return Boolean(response.data?.data?.changed)
    } catch (error) {
      throw new Error(getErrorMessage(error, 'Could not record the consent.'))
    }
  },
}
