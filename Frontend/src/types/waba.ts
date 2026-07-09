// src/types/waba.ts

export interface WabaConnectionModel {
  facebookAppId: string
  facebookAppSecret: string
  wabaId: string
  accessToken: string
}

export interface AccessTokenInfoModel {
  token: string
  permissions: string[]
  issuedAt: string
  webhookUrl: string
}

export interface PhoneInfoModel {
  displayPhoneNumber: string
  verifiedName: string
  numberId: string
  quality: 'GREEN' | 'YELLOW' | 'RED' | string
  messagesSent: number
  messageLimit: number
}

export interface WabaHealthModel {
  lastChecked: string
  
  wabaId: string
  wabaStatus: 'AVAILABLE' | 'UNAVAILABLE' | string
  
  businessId: string
  businessStatus: 'AVAILABLE' | 'UNAVAILABLE' | string
  
  appId: string
  appStatus: 'AVAILABLE' | 'UNAVAILABLE' | string
}

export interface ConnectionRequirement {
  step: number
  title: string
  description: string
}
