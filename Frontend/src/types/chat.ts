// src/types/chat.ts

export interface Conversation {
  id: number
  contactId: number
  connectionId?: number | null
  connectionName?: string | null

  /**
   * 'WhatsApp' or 'Email', as the API spells it. Always present — threads that predate the
   * email channel read as WhatsApp — so the inbox can show a per-row icon without a fallback.
   */
  channel?: string

  /** The contact's email address. How an email thread identifies its correspondent. */
  email?: string | null

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
  assignedTo?: string | null
  source?: string | null
  contactCreatedAt?: string
  contactGroups?: string[]
  contactIsActive?: boolean
}

export interface Message {
  /**
   * 'WhatsApp' or 'Email', as the API spells it. Decides how the thread renders this message —
   * a bubble or an email card.
   */
  channel?: string

  // ── Email only ───────────────────────────────────────────────────────────────────────────
  // Null on WhatsApp. Projected from EmailMessageDetails server-side.
  subject?: string | null
  fromAddress?: string | null
  fromName?: string | null
  toAddresses?: string | null
  ccAddresses?: string | null
  /** The body as sent. Sanitised server-side, and rendered in a sandboxed frame regardless. */
  htmlBody?: string | null
  hasAttachments?: boolean

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
  mediaUrl?: string | null
  mediaType?: string | null
  mediaFileName?: string | null
}

export interface ChatAccount {
  id: number
  connectionId?: number | null
  connectionName?: string | null
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
