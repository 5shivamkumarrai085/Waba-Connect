import { apiClient } from '../apiClient'

export interface Webhook {
  id: number
  name: string
  url: string
  eventTypes: string[]
  connectionIds: number[]
  includePersonalData: boolean
  isActive: boolean
  disabledReason?: string | null
  lastStatus?: string | null
  lastDeliveryAt?: string | null
  failureStreak: number
  createdAt: string
  /** Only on create and rotation. */
  secret?: string | null
}

export interface WebhookDelivery {
  id: number
  eventId: string
  eventType: string
  status: 'Pending' | 'Delivered' | 'Failed' | 'Skipped' | string
  attempts: number
  responseCode?: number | null
  durationMs?: number | null
  error?: string | null
  createdAt: string
  lastAttemptAt?: string | null
  payloadJson: string
}

export interface SaveWebhookInput {
  name: string
  url: string
  eventTypes: string[]
  connectionIds: number[]
  includePersonalData: boolean
  isActive: boolean
}

export interface Paged<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

const unwrap = <T,>(response: { data?: { data?: T } }): T => response.data?.data as T

export const webhookService = {
  list: async (): Promise<Webhook[]> => unwrap<Webhook[]>(await apiClient.get('/webhooks')) ?? [],
  create: async (input: SaveWebhookInput): Promise<Webhook> => unwrap<Webhook>(await apiClient.post('/webhooks', input)),
  update: async (id: number, input: SaveWebhookInput): Promise<Webhook> => unwrap<Webhook>(await apiClient.put(`/webhooks/${id}`, input)),
  remove: async (id: number): Promise<void> => { await apiClient.delete(`/webhooks/${id}`) },
  rotateSecret: async (id: number): Promise<Webhook> => unwrap<Webhook>(await apiClient.post(`/webhooks/${id}/rotate-secret`)),
  /** Returns the delivery and the server's one-line outcome. */
  sendTest: async (id: number): Promise<{ delivery: WebhookDelivery; message: string; success: boolean }> => {
    const response = await apiClient.post(`/webhooks/${id}/test`)
    return { delivery: response.data?.data, message: response.data?.message ?? '', success: !!response.data?.success }
  },
  deliveries: async (id: number, page: number, pageSize: number, status?: string): Promise<Paged<WebhookDelivery>> =>
    unwrap<Paged<WebhookDelivery>>(await apiClient.get(`/webhooks/${id}/deliveries`, { params: { page, pageSize, status: status || undefined } })),
  replay: async (deliveryId: number): Promise<WebhookDelivery> => unwrap<WebhookDelivery>(await apiClient.post(`/webhooks/deliveries/${deliveryId}/replay`))
}

export default webhookService
