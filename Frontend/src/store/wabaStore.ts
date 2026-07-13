// src/store/wabaStore.ts
import { create } from 'zustand'
import { wabaService } from '../services/waba/wabaService'
import type { AccessTokenInfoModel, PhoneInfoModel, WabaHealthModel } from '../types/waba'

interface WabaStoreState {
  isConnected: boolean
  isLoading: boolean
  isConnecting: boolean
  isSendingMessage: boolean
  isVerifyingWebhook: boolean
  
  facebookAppId: string
  facebookAppSecret: string
  wabaId: string
  accessToken: string
  
  phoneInfo: PhoneInfoModel | null
  tokenInfo: AccessTokenInfoModel | null
  healthInfo: WabaHealthModel | null
  
  webhookUrl: string
  verifyToken: string
  
  setFacebookAppId: (facebookAppId: string) => void
  setFacebookAppSecret: (facebookAppSecret: string) => void
  setWabaId: (wabaId: string) => void
  setAccessToken: (accessToken: string) => void
  
  loadWabaData: () => Promise<void>
  connectApp: (facebookAppId: string, facebookAppSecret: string) => Promise<{ success: boolean; message: string }>
  configureWaba: (wabaId: string, accessToken: string) => Promise<{ success: boolean; message: string }>
  disconnectWaba: () => Promise<{ success: boolean; message: string }>
  sendTestMessage: (toPhoneNumber: string) => Promise<{ success: boolean; message: string }>
  verifyWebhook: () => Promise<{ success: boolean; message: string }>
  refreshHealth: () => Promise<void>
}

export const useWabaStore = create<WabaStoreState>((set, get) => ({
  isConnected: false,
  isLoading: false,
  isConnecting: false,
  isSendingMessage: false,
  isVerifyingWebhook: false,
  
  facebookAppId: '',
  facebookAppSecret: '',
  wabaId: '',
  accessToken: '',
  
  phoneInfo: null,
  tokenInfo: null,
  healthInfo: null,
  
  webhookUrl: '',
  verifyToken: '',
  
  setFacebookAppId: (facebookAppId) => set({ facebookAppId }),
  setFacebookAppSecret: (facebookAppSecret) => set({ facebookAppSecret }),
  setWabaId: (wabaId) => set({ wabaId }),
  setAccessToken: (accessToken) => set({ accessToken }),
  
  loadWabaData: async () => {
    set({ isLoading: true })
    try {
      const dashboard = await wabaService.getDashboard()
      if (dashboard) {
        set({
          isConnected: dashboard.isConnected,
          facebookAppId: dashboard.facebookAppId || '',
          wabaId: dashboard.wabaId || '',
          accessToken: dashboard.accessToken || '',
          webhookUrl: dashboard.webhookUrl || '',
          verifyToken: dashboard.verifyToken || '',
          tokenInfo: dashboard.tokenInfo ? {
            token: dashboard.accessToken,
            permissions: dashboard.tokenInfo.scopes || [],
            issuedAt: dashboard.tokenInfo.issuedAt,
            webhookUrl: dashboard.webhookUrl
          } : null,
          phoneInfo: dashboard.phoneNumbers && dashboard.phoneNumbers.length > 0 ? {
            displayPhoneNumber: dashboard.phoneNumbers[0].phoneNumber,
            verifiedName: dashboard.phoneNumbers[0].verifiedName || dashboard.phoneNumbers[0].displayName,
            numberId: dashboard.phoneNumbers[0].phoneNumberId || 'unknown',
            quality: dashboard.phoneNumbers[0].quality || 'GREEN',
            messagesSent: dashboard.phoneNumbers[0].messagesSent || 0,
            messageLimit: parseInt(dashboard.phoneNumbers[0].messageLimit) || 1000
          } : null,
          healthInfo: (() => {
            const hasLog = !!dashboard.latestHealthLog;
            const logStatus = dashboard.latestHealthLog?.status || 'AVAILABLE';
            const logDesc = dashboard.latestHealthLog?.description || '';

            let appStat = 'AVAILABLE';
            let bizStat = 'AVAILABLE';
            let wabaStat = 'AVAILABLE';

            if (logStatus === 'UNAVAILABLE') {
              appStat = 'UNAVAILABLE';
              bizStat = 'UNAVAILABLE';
              wabaStat = 'UNAVAILABLE';
            } else if (logStatus === 'PARTIAL') {
              if (logDesc.includes('App ID Validation Failed')) appStat = 'UNAVAILABLE';
              if (logDesc.includes('WABA Account Retrieval Failed')) bizStat = 'UNAVAILABLE';
              if (logDesc.includes('Phone Numbers Synchronization Failed')) wabaStat = 'UNAVAILABLE';
            }

            return {
              lastChecked: hasLog ? dashboard.latestHealthLog.checkedAt : new Date().toISOString(),
              wabaId: dashboard.wabaId || '',
              wabaStatus: wabaStat,
              businessId: dashboard.business?.businessId || '',
              businessStatus: bizStat,
              appId: dashboard.facebookAppId || '',
              appStatus: appStat
            };
          })()
        })
      }
    } catch (err) {
      console.error('Error loading WABA settings data:', err)
    } finally {
      set({ isLoading: false })
    }
  },
  
  connectApp: async (facebookAppId: string, facebookAppSecret: string) => {
    set({ isConnecting: true })
    try {
      const res = await wabaService.connectApp({ facebookAppId, facebookAppSecret })
      await get().loadWabaData()
      return { success: true, message: res.message || 'Facebook App connected successfully!' }
    } catch (err: any) {
      return { success: false, message: err?.response?.data?.message || err?.message || 'Error connecting app.' }
    } finally {
      set({ isConnecting: false })
    }
  },
  
  configureWaba: async (wabaId: string, accessToken: string) => {
    set({ isConnecting: true })
    try {
      const res = await wabaService.configure({ wabaId, accessToken })
      await get().loadWabaData()
      return { success: true, message: res.message || 'WABA configured successfully!' }
    } catch (err: any) {
      return { success: false, message: err?.response?.data?.message || err?.message || 'Error configuring WABA.' }
    } finally {
      set({ isConnecting: false })
    }
  },
  
  disconnectWaba: async () => {
    set({ isLoading: true })
    try {
      const res = await wabaService.disconnectWaba()
      if (res.success) {
        set({
          isConnected: false,
          facebookAppId: '',
          facebookAppSecret: '',
          wabaId: '',
          accessToken: '',
          webhookUrl: '',
          verifyToken: '',
          phoneInfo: null,
          tokenInfo: null,
          healthInfo: null
        })
      }
      return res
    } catch (err: any) {
      return { success: false, message: err?.message || 'Error disconnecting account.' }
    } finally {
      set({ isLoading: false })
    }
  },
  
  sendTestMessage: async (toPhoneNumber) => {
    if (!toPhoneNumber) {
      return { success: false, message: 'Recipient phone number is required.' }
    }
    set({ isSendingMessage: true })
    try {
      const res = await wabaService.sendTestMessage(toPhoneNumber)
      return res
    } catch (err: any) {
      return { success: false, message: err?.message || 'Error sending test message.' }
    } finally {
      set({ isSendingMessage: false })
    }
  },
  
  verifyWebhook: async () => {
    set({ isVerifyingWebhook: true })
    try {
      const { verifyToken } = get()
      const res = await wabaService.verifyWebhook(verifyToken)
      return res
    } catch (err: any) {
      return { success: false, message: err?.message || 'Webhook verification failed.' }
    } finally {
      set({ isVerifyingWebhook: false })
    }
  },
  
  refreshHealth: async () => {
    try {
      const dashboard = await wabaService.refreshHealth()
      if (dashboard) {
        set({
          healthInfo: (() => {
            const hasLog = !!dashboard.latestHealthLog;
            const logStatus = dashboard.latestHealthLog?.status || 'AVAILABLE';
            const logDesc = dashboard.latestHealthLog?.description || '';

            let appStat = 'AVAILABLE';
            let bizStat = 'AVAILABLE';
            let wabaStat = 'AVAILABLE';

            if (logStatus === 'UNAVAILABLE') {
              appStat = 'UNAVAILABLE';
              bizStat = 'UNAVAILABLE';
              wabaStat = 'UNAVAILABLE';
            } else if (logStatus === 'PARTIAL') {
              if (logDesc.includes('App ID Validation Failed')) appStat = 'UNAVAILABLE';
              if (logDesc.includes('WABA Account Retrieval Failed')) bizStat = 'UNAVAILABLE';
              if (logDesc.includes('Phone Numbers Synchronization Failed')) wabaStat = 'UNAVAILABLE';
            }

            return {
              lastChecked: hasLog ? dashboard.latestHealthLog.checkedAt : new Date().toISOString(),
              wabaId: dashboard.wabaId || '',
              wabaStatus: wabaStat,
              businessId: dashboard.business?.businessId || '',
              businessStatus: bizStat,
              appId: dashboard.facebookAppId || '',
              appStatus: appStat
            };
          })()
        })
      }
    } catch (err) {
      console.error('Error refreshing WABA health status:', err)
    }
  }
}))
