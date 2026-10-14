import { apiClient } from '../apiClient'

/** One problem Meta reported when it validated the uploaded Flow JSON. */
export interface FlowValidationError {
  error?: string
  error_type?: string
  message?: string
  line_start?: number
  column_start?: number
}

export interface FlowKeyStatus {
  exists: boolean
  createdAt: string | null
  uploadedAt: string | null
  signatureStatus: string | null
  lastError: string | null
}

export interface FlowRegistration {
  purpose: string
  metaFlowId: string | null
  name: string
  /** Meta's status: DRAFT, PUBLISHED, DEPRECATED, BLOCKED, THROTTLED. */
  status: string | null
  jsonVersion: string | null
  validationErrors: FlowValidationError[] | null
  /** Where Meta should send data-exchange requests, from App:PublicBaseUrl. */
  endpointUrl: string
  /** What Meta currently has on record. */
  endpointUri: string | null
  lastSyncedAt: string | null
  publishedAt: string | null
  lastPingAt: string | null
  lastError: string | null
}

export interface WhatsAppFlowsStatus {
  connectionId: number
  publicBaseUrl: string
  publicAddressProblem: string | null
  allowDraftSend: boolean
  key: FlowKeyStatus
  sendMoney: FlowRegistration | null
}

export interface FlowLaunchResult { sent: boolean; error: string | null; messageId: string | null }

const unwrap = <T,>(response: { data?: { data?: T } }): T => response.data?.data as T
const base = (connectionId: number) => `/connections/${connectionId}/whatsapp-flows`

export const whatsAppFlowsService = {
  status: async (connectionId: number): Promise<WhatsAppFlowsStatus> => unwrap(await apiClient.get(base(connectionId))),
  ensureKey: async (connectionId: number, regenerate = false): Promise<WhatsAppFlowsStatus> =>
    unwrap(await apiClient.post(`${base(connectionId)}/encryption-key`, null, { params: { regenerate } })),
  syncSendMoney: async (connectionId: number, createNew = false): Promise<WhatsAppFlowsStatus> =>
    unwrap(await apiClient.post(`${base(connectionId)}/send-money/sync`, null, { params: { createNew } })),
  /** Uses a Flow built in WhatsApp Manager (with our screens) as this connection's Send Money Flow. */
  linkSendMoney: async (connectionId: number, flowId: string): Promise<WhatsAppFlowsStatus> =>
    unwrap(await apiClient.post(`${base(connectionId)}/send-money/link`, { flowId })),
  publishSendMoney: async (connectionId: number): Promise<WhatsAppFlowsStatus> =>
    unwrap(await apiClient.post(`${base(connectionId)}/send-money/publish`)),
  testSend: async (connectionId: number, phone: string): Promise<{ result: FlowLaunchResult; message: string; success: boolean }> => {
    const response = await apiClient.post(`${base(connectionId)}/send-money/test-send`, { phone })
    return { result: response.data?.data, message: response.data?.message ?? '', success: !!response.data?.success }
  }
}

export default whatsAppFlowsService
