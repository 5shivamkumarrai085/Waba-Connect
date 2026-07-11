// src/types/chat.ts

export interface Conversation {
  id: number
  contactId: number
  name: string
  status: 'lead' | 'customer' | 'guest' | string
  phone: string
  lastMessage: string
  unreadCount: number
  lastMessageTime: string
  lastMessageAt?: string | null
  avatarUrl?: string
  fromPhoneNumber?: string | null
  fromPhoneNumberId?: string | null
}

export interface Message {
  id: number
  type: 'incoming' | 'outgoing' | 'system'
  text: string
  time: string
  createdAt: string
  status?: 'sending' | 'delivered' | 'read' | 'failed' | 'pending' | string
  isTemplate?: boolean
  errorMessage?: string
  sentAt?: string | null
  deliveredAt?: string | null
  readAt?: string | null
  campaignId?: number | null
  whatsAppMessageId?: string | null
}

export interface ChatAccount {
  id: number
  phoneNumber: string
  phoneNumberId: string
  displayName: string
  verifiedName: string
  quality: string
  status: string
}

export interface CsvUploadModel {
  campaignName: string
  file: File | null
  uploadProgress: number
  status: 'idle' | 'uploading' | 'success' | 'error'
  validationError?: string
}
