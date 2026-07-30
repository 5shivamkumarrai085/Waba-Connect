// src/services/waba/wabaService.ts
import axios from 'axios'

const API_BASE_URL = 'http://localhost:5155/api/waba'

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
})

// In-memory cache for connection daily message limits (populated dynamically on load / getDashboard)
const cachedLimitData: Record<string, { messagesSent: number; messageLimit: number; timestamp: number }> = {}

export const wabaService = {
  getDashboard: async (connectionId?: number) => {
    try {
      const response = await api.get('/dashboard', { params: { connectionId } })
      const data = response.data
      if (data && data.phoneNumbers && data.phoneNumbers.length > 0) {
        const key = connectionId ? String(connectionId) : 'default'
        const phone = data.phoneNumbers[0]
        cachedLimitData[key] = {
          messagesSent: phone.messagesSent || 0,
          messageLimit: parseInt(phone.messageLimit || '1000', 10),
          timestamp: Date.now()
        }
      }
      return data
    } catch (e: any) {
      return null
    }
  },

  getCachedLimit: (connectionId?: number) => {
    const key = connectionId ? String(connectionId) : 'default'
    return cachedLimitData[key] || Object.values(cachedLimitData)[0] || null
  },

  checkLimitFast: async (connectionId?: number): Promise<{ limitReached: boolean; message?: string }> => {
    // 1. Check in-memory cache instantly (< 1ms delay)
    const cached = wabaService.getCachedLimit(connectionId)
    if (cached && cached.messagesSent >= cached.messageLimit) {
      return {
        limitReached: true,
        message: `Daily message limit reached (${cached.messagesSent}/${cached.messageLimit}) for this connection.`
      }
    }

    // 2. Fetch fresh metrics from backend DB/Meta dynamically (and update cache for subsequent instant checks)
    const data = await wabaService.getDashboard(connectionId)
    if (data && data.phoneNumbers && data.phoneNumbers.length > 0) {
      const phone = data.phoneNumbers[0]
      const sent = phone.messagesSent || 0
      const limit = parseInt(phone.messageLimit || '1000', 10)
      if (sent >= limit) {
        return {
          limitReached: true,
          message: `Daily message limit reached (${sent}/${limit}) for this connection.`
        }
      }
    }
    return { limitReached: false }
  },

  connectApp: async (data: { connectionId?: number; facebookAppId: string; facebookAppSecret: string }) => {
    const response = await api.post('/connect-app', data)
    return response.data
  },

  configure: async (data: { connectionId?: number; wabaId: string; accessToken: string }) => {
    const response = await api.post('/configure', data)
    return response.data
  },

  disconnectWaba: async (connectionId?: number): Promise<{ success: boolean; message: string }> => {
    try {
      const response = await api.post('/disconnect', null, { params: { connectionId } })
      return { success: true, message: response.data.message || 'WABA disconnected successfully!' }
    } catch (e: any) {
      return { success: false, message: e.response?.data?.message || 'Failed to disconnect.' }
    }
  },

  sendTestMessage: async (toPhoneNumber: string, connectionId?: number): Promise<{ success: boolean; message: string }> => {
    try {
      const response = await api.post('/send-message', {
        connectionId,
        recipientNumber: toPhoneNumber,
        templateName: 'hello_world', // Default fallback template
        languageCode: 'en_US'
      })
      return { success: true, message: response.data.message || 'Test message sent.' }
    } catch (e: any) {
      return { success: false, message: e.response?.data?.message || 'Failed to send message.' }
    }
  },

  verifyWebhook: async (verifyToken?: string, connectionId?: number): Promise<{ success: boolean; message: string }> => {
    try {
      const response = await api.post('/verify-webhook', { connectionId, verifyToken: verifyToken || '' })
      return { success: response.data.verified, message: response.data.message || 'Webhook verified.' }
    } catch (e: any) {
      return { success: false, message: e.response?.data?.message || 'Failed to verify webhook.' }
    }
  },

  refreshHealth: async (connectionId?: number) => {
    try {
      const response = await api.post('/refresh', null, { params: { connectionId } })
      return response.data
    } catch (e) {
      return null
    }
  },

  updateMessageLimit: async (connectionId: number | undefined, messageLimit: number): Promise<{ success: boolean; message: string }> => {
    try {
      const response = await api.post('/message-limit', { connectionId, messageLimit })
      const key = connectionId ? String(connectionId) : 'default'
      if (cachedLimitData[key]) {
        cachedLimitData[key].messageLimit = messageLimit
      }
      return { success: true, message: response.data.message || 'Message limit updated successfully!' }
    } catch (e: any) {
      return { success: false, message: e.response?.data?.message || 'Failed to update message limit.' }
    }
  }
}
