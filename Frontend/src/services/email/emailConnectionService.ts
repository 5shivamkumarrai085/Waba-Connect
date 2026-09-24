import { apiClient } from '../apiClient'
import type {
  CreateEmailConnectionPayload,
  EmailConnection,
  EmailProviderTestResult,
  EmailSenderIdentity,
  SaveEmailProviderPayload,
  SaveEmailSenderPayload,
  SaveImapSettingsPayload,
  SendTestEmailPayload,
  TestEmailProviderPayload
} from '../../types/email'

/**
 * Email connections API.
 *
 * Follows the same split as setupService: reads that feed pickers fall back to an empty list so
 * one failed lookup cannot blank a form, while writes let the server's message through — a
 * refused save needs to say *why* ("this sender has been used by a campaign", "access-key mode
 * requires a secret"), and swallowing that leaves the operator with nothing to act on.
 *
 * The test endpoints are the exception in the other direction: they answer 200 with
 * `success: false` for a failed credential, because a failed diagnostic is a successful request.
 * Treating it as an error would route it through the generic error handler and lose the detail
 * that explains what to fix.
 */
export const emailConnectionService = {
  getConnections: async (): Promise<EmailConnection[]> => {
    try {
      const response = await apiClient.get('/email/connections')
      return response.data?.data ?? []
    } catch {
      return []
    }
  },

  getConnection: async (id: number): Promise<EmailConnection | null> => {
    try {
      const response = await apiClient.get(`/email/connections/${id}`)
      return response.data?.data ?? null
    } catch {
      return null
    }
  },

  /** Step 1 of the wizard: name the connection and its first sender. */
  createConnection: async (payload: CreateEmailConnectionPayload): Promise<EmailConnection> => {
    const response = await apiClient.post('/email/connections', payload)
    return response.data?.data
  },

  /** Step 2: store provider credentials. Empty secret fields keep their stored values. */
  saveProvider: async (id: number, payload: SaveEmailProviderPayload): Promise<EmailConnection> => {
    const response = await apiClient.put(`/email/connections/${id}/provider`, payload)
    return response.data?.data
  },

  /** Verifies stored credentials. Sends nothing, so it costs no quota. */
  testConnection: async (id: number): Promise<EmailProviderTestResult> => {
    const response = await apiClient.post(`/email/connections/${id}/test-connection`)
    return (
      response.data?.data ?? {
        success: response.data?.success ?? false,
        message: response.data?.message ?? 'The connection test did not return a result.'
      }
    )
  },

  /**
   * Verifies credentials typed into the form but not yet saved, so Test Connection works before
   * Save — otherwise an operator has to store a possibly-wrong secret to find out it is wrong.
   */
  testUnsavedConnection: async (payload: TestEmailProviderPayload): Promise<EmailProviderTestResult> => {
    const response = await apiClient.post('/email/connections/test-connection', payload)
    return (
      response.data?.data ?? {
        success: response.data?.success ?? false,
        message: response.data?.message ?? 'The connection test did not return a result.'
      }
    )
  },

  /** Sends one real email. Separate from the credential test, which costs nothing. */
  sendTestEmail: async (id: number, payload: SendTestEmailPayload): Promise<EmailProviderTestResult> => {
    const response = await apiClient.post(`/email/connections/${id}/test-email`, payload)
    return (
      response.data?.data ?? {
        success: response.data?.success ?? false,
        message: response.data?.message ?? 'The test email did not return a result.'
      }
    )
  },

  getSenders: async (id: number): Promise<EmailSenderIdentity[]> => {
    try {
      const response = await apiClient.get(`/email/connections/${id}/senders`)
      return response.data?.data ?? []
    } catch {
      return []
    }
  },

  addSender: async (id: number, payload: SaveEmailSenderPayload): Promise<EmailSenderIdentity> => {
    const response = await apiClient.post(`/email/connections/${id}/senders`, payload)
    return response.data?.data
  },

  updateSender: async (
    id: number,
    senderId: number,
    payload: SaveEmailSenderPayload
  ): Promise<EmailSenderIdentity> => {
    const response = await apiClient.put(`/email/connections/${id}/senders/${senderId}`, payload)
    return response.data?.data
  },

  /**
   * Re-reads one sender's verification state from the provider.
   *
   * Returns the connection's full sender list, so a caller's dropdown reflects the new `canSend`
   * without a second round trip. The message explains the outcome when it is not Verified.
   */
  refreshSender: async (
    id: number,
    senderId: number
  ): Promise<{ senders: EmailSenderIdentity[]; message: string }> => {
    try {
      const response = await apiClient.post(`/email/connections/${id}/senders/${senderId}/refresh`)
      return {
        senders: response.data?.data ?? [],
        message: response.data?.message ?? 'Sender status refreshed.'
      }
    } catch (err: any) {
      // An empty list would read as "this connection has no senders", which is a different and
      // much more alarming claim than "the check failed".
      return {
        senders: [],
        message: err?.response?.data?.message ?? 'Could not reach the server to check this sender.'
      }
    }
  },

  deleteSender: async (id: number, senderId: number): Promise<void> => {
    await apiClient.delete(`/email/connections/${id}/senders/${senderId}`)
  },

  /** Deactivates the connection and clears its stored credentials. */
  disconnect: async (id: number): Promise<void> => {
    await apiClient.post(`/email/connections/${id}/disconnect`)
  },

  /** Saves IMAP credentials for inbound reply polling. Empty password keeps the stored value. */
  saveImapSettings: async (id: number, payload: SaveImapSettingsPayload): Promise<EmailConnection> => {
    const response = await apiClient.put(`/email/connections/${id}/imap`, payload)
    return response.data?.data
  },

  /** Opens a live IMAP connection to verify stored credentials. Sends nothing. */
  testImapConnection: async (id: number): Promise<EmailProviderTestResult> => {
    const response = await apiClient.post(`/email/connections/${id}/test-imap`)
    return (
      response.data?.data ?? {
        success: response.data?.success ?? false,
        message: response.data?.message ?? 'The IMAP test did not return a result.'
      }
    )
  },

  deleteConnection: async (id: number): Promise<void> => {
    await apiClient.delete(`/email/connections/${id}`)
  }
}

export default emailConnectionService
