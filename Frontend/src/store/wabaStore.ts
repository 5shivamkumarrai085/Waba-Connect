// src/store/wabaStore.ts
import { create } from 'zustand'
import { wabaService } from '../services/waba/wabaService'
import type { AccessTokenInfoModel, PhoneInfoModel, WabaHealthModel } from '../types/waba'

interface WabaStoreState {
  activeConnectionId: number | null
  connectionName: string
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
  
  setActiveConnectionId: (id: number | null) => void
  setFacebookAppId: (facebookAppId: string) => void
  setFacebookAppSecret: (facebookAppSecret: string) => void
  setWabaId: (wabaId: string) => void
  setAccessToken: (accessToken: string) => void
  
  loadWabaData: (connectionId?: number) => Promise<void>
  connectApp: (facebookAppId: string, facebookAppSecret: string, connectionId?: number) => Promise<{ success: boolean; message: string }>
  configureWaba: (wabaId: string, accessToken: string, connectionId?: number) => Promise<{ success: boolean; message: string }>
  disconnectWaba: (connectionId?: number) => Promise<{ success: boolean; message: string }>
  sendTestMessage: (toPhoneNumber: string, connectionId?: number) => Promise<{ success: boolean; message: string }>
  verifyWebhook: (connectionId?: number) => Promise<{ success: boolean; message: string }>
  refreshHealth: (connectionId?: number) => Promise<void>
  updateMessageLimit: (limit: number, connectionId?: number) => Promise<{ success: boolean; message: string }>
}

export const useWabaStore = create<WabaStoreState>((set, get) => ({
  activeConnectionId: null,
  connectionName: '',
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
  
  setActiveConnectionId: (id) => set({ activeConnectionId: id }),
  setFacebookAppId: (facebookAppId) => set({ facebookAppId }),
  setFacebookAppSecret: (facebookAppSecret) => set({ facebookAppSecret }),
  setWabaId: (wabaId) => set({ wabaId }),
  setAccessToken: (accessToken) => set({ accessToken }),
  
  loadWabaData: async (connectionId) => {
    const targetId = connectionId ?? get().activeConnectionId ?? undefined
    set({
      isLoading: true,
      activeConnectionId: targetId ?? null,
      // Clear old data to prevent stale flash
      isConnected: false,
      facebookAppId: '',
      wabaId: '',
      accessToken: '',
      webhookUrl: '',
      verifyToken: '',
      phoneInfo: null,
      tokenInfo: null,
      healthInfo: null
    })
    try {
      const dashboard = await wabaService.getDashboard(targetId)
      if (dashboard) {
        // Preserve the user-typed facebookAppSecret if backend returns masked or empty
        const currentSecret = get().facebookAppSecret
        const backendSecret = dashboard.facebookAppSecret || ''
        const resolvedSecret = backendSecret && backendSecret !== '••••••••' ? backendSecret : currentSecret

        set({
          activeConnectionId: targetId ?? null,
          connectionName: dashboard.connectionName || 'WABA Connection',
          isConnected: dashboard.isConnected,
          facebookAppId: dashboard.facebookAppId || '',
          facebookAppSecret: resolvedSecret,
          wabaId: dashboard.wabaId || '',
          accessToken: dashboard.accessToken || '',
          webhookUrl: dashboard.webhookUrl || '',
          verifyToken: dashboard.verifyToken || '',
          tokenInfo: dashboard.tokenInfo ? {
            token: dashboard.accessToken,
            permissions: dashboard.tokenInfo.scopes || ['whatsapp_business_management', 'whatsapp_business_messaging', 'public_profile'],
            issuedAt: dashboard.tokenInfo.issuedAt || new Date().toISOString(),
            webhookUrl: dashboard.webhookUrl
          } : null,
          phoneInfo: dashboard.phoneNumbers && dashboard.phoneNumbers.length > 0 ? {
            displayPhoneNumber: dashboard.phoneNumbers[0].phoneNumber,
            verifiedName: dashboard.phoneNumbers[0].verifiedName || dashboard.phoneNumbers[0].displayName || '',
            numberId: dashboard.phoneNumbers[0].phoneNumberId || '',
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
  
  connectApp: async (facebookAppId, facebookAppSecret, connectionId) => {
    const targetId = connectionId ?? get().activeConnectionId ?? undefined
    set({ isConnecting: true })
    try {
      const res = await wabaService.connectApp({ connectionId: targetId, facebookAppId, facebookAppSecret })
      await get().loadWabaData(targetId)
      return { success: true, message: res.message || 'Facebook App connected successfully!' }
    } catch (err: any) {
      return { success: false, message: err?.response?.data?.message || err?.message || 'Error connecting app.' }
    } finally {
      set({ isConnecting: false })
    }
  },
  
  configureWaba: async (wabaId, accessToken, connectionId) => {
    const targetId = connectionId ?? get().activeConnectionId ?? undefined
    set({ isConnecting: true })
    try {
      const res = await wabaService.configure({ connectionId: targetId, wabaId, accessToken })
      await get().loadWabaData(targetId)
      return { success: true, message: res.message || 'WABA configured successfully!' }
    } catch (err: any) {
      return { success: false, message: err?.response?.data?.message || err?.message || 'Error configuring WABA.' }
    } finally {
      set({ isConnecting: false })
    }
  },
  
  disconnectWaba: async (connectionId) => {
    const targetId = connectionId ?? get().activeConnectionId ?? undefined
    set({ isLoading: true })
    try {
      const res = await wabaService.disconnectWaba(targetId)
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
  
  sendTestMessage: async (toPhoneNumber, connectionId) => {
    const targetId = connectionId ?? get().activeConnectionId ?? undefined
    if (!toPhoneNumber) {
      return { success: false, message: 'Recipient phone number is required.' }
    }
    set({ isSendingMessage: true })
    try {
      const res = await wabaService.sendTestMessage(toPhoneNumber, targetId)
      return res
    } catch (err: any) {
      return { success: false, message: err?.message || 'Error sending test message.' }
    } finally {
      set({ isSendingMessage: false })
    }
  },
  
  verifyWebhook: async (connectionId) => {
    const targetId = connectionId ?? get().activeConnectionId ?? undefined
    set({ isVerifyingWebhook: true })
    try {
      const { verifyToken } = get()
      const res = await wabaService.verifyWebhook(verifyToken, targetId)
      return res
    } catch (err: any) {
      return { success: false, message: err?.message || 'Webhook verification failed.' }
    } finally {
      set({ isVerifyingWebhook: false })
    }
  },
  
  refreshHealth: async (connectionId) => {
    const targetId = connectionId ?? get().activeConnectionId ?? undefined
    try {
      const dashboard = await wabaService.refreshHealth(targetId)
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
  },

  updateMessageLimit: async (limit, connectionId) => {
    const targetId = connectionId ?? get().activeConnectionId ?? undefined
    try {
      const res = await wabaService.updateMessageLimit(targetId, limit)
      if (res.success) {
        await get().loadWabaData(targetId)
      }
      return res
    } catch (err: any) {
      return { success: false, message: err?.message || 'Error updating message limit.' }
    }
  }
}))
