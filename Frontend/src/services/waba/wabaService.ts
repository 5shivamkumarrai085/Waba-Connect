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
  getDashboard: async () => {
    try {
      const response = await api.get('/dashboard')
      return response.data
    } catch (e: any) {
      return null
    }
  },

  connectApp: async (data: { facebookAppId: string; facebookAppSecret: string }) => {
    const response = await api.post('/connect-app', data)
    return response.data
  },

  configure: async (data: { wabaId: string; accessToken: string }) => {
    const response = await api.post('/configure', data)
    return response.data
  },

  disconnectWaba: async (): Promise<{ success: boolean; message: string }> => {
    try {
      const response = await api.post('/disconnect')
      return { success: true, message: response.data.message || 'WABA disconnected successfully!' }
    } catch (e: any) {
      return { success: false, message: e.response?.data?.message || 'Failed to disconnect.' }
    }
  },

  sendTestMessage: async (toPhoneNumber: string): Promise<{ success: boolean; message: string }> => {
    try {
      const response = await api.post('/send-message', {
        recipientNumber: toPhoneNumber,
        templateName: 'hello_world', // Default fallback template
        languageCode: 'en_US'
      })
      return { success: true, message: response.data.message || 'Test message sent.' }
    } catch (e: any) {
      return { success: false, message: e.response?.data?.message || 'Failed to send message.' }
    }
  },

  verifyWebhook: async (verifyToken?: string): Promise<{ success: boolean; message: string }> => {
    try {
      const response = await api.post('/verify-webhook', { verifyToken: verifyToken || '' })
      return { success: response.data.verified, message: response.data.message || 'Webhook verified.' }
    } catch (e: any) {
      return { success: false, message: e.response?.data?.message || 'Failed to verify webhook.' }
    }
  },

  refreshHealth: async () => {
    try {
      const response = await api.post('/refresh')
      return response.data
    } catch (e) {
      return null
    }
  }
}
