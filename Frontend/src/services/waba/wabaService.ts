// src/services/waba/wabaService.ts
import axios from 'axios'

const API_BASE_URL = 'http://localhost:5155/api/waba'

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
})

export const wabaService = {
  getDashboard: async (connectionId?: number) => {
    try {
      const response = await api.get('/dashboard', { params: { connectionId } })
      return response.data
    } catch (e: any) {
      return null
    }
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
  }
}
