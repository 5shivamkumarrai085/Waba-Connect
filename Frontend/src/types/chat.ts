// src/types/chat.ts

export interface Conversation {
  id: string
  name: string
  status: 'lead' | 'customer' | 'guest' | string
  phone: string
  lastMessage: string
  unreadCount: number
  lastMessageTime: string
  avatarUrl?: string
}

export interface Message {
  id: number
  type: 'incoming' | 'outgoing' | 'system'
  text: string
  time: string
  status?: 'delivered' | 'read' | 'failed' | 'pending' | string
  isTemplate?: boolean
  errorMessage?: string
}

export interface CsvUploadModel {
  campaignName: string
  file: File | null
  uploadProgress: number
  status: 'idle' | 'uploading' | 'success' | 'error'
  validationError?: string
}
