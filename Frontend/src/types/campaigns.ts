// src/types/campaigns.ts

import type { MessageChannel } from './channel'

export interface Campaign {
  id: number
  name: string
  templateName: string
  relationType: string // e.g. 'Lead', 'Customer', 'Csv_campaign'
  total: number
  deliveredTo: number
  readBy: number
  failedCount: number
  status: string // e.g. 'Success', 'Paused', 'draft'
  createdAt: string
  scheduledAt?: string
  isDeleted?: boolean
  isBulkCampaign?: boolean
  deletedAt?: string
  deletedBy?: string
  connectionId?: number
  connectionName?: string
  connectionNickname?: string

  /**
   * 'WhatsApp' or 'Email', as the API spells it. Always populated — pre-existing campaigns read
   * as WhatsApp — so the list can render a channel column without a second lookup.
   */
  channel?: string

  /** Email-only counters, absent on WhatsApp campaigns. */
  emailStats?: EmailCampaignStats
}

/**
 * Email outcome counters.
 *
 * Separate from the shared delivered/read/failed numbers because the vocabularies genuinely
 * differ: WhatsApp has "read", email has opens, clicks, bounces and complaints, and a bounce is
 * not the same kind of failure as a rejected send.
 */
export interface EmailCampaignStats {
  sent: number
  delivered: number
  bounced: number
  complained: number
  suppressed: number
  opened: number
  clicked: number
  pending: number
  failed: number
}

export interface CampaignStatistics {
  totalLeads: number
  totalLeadsPercent: string
  deliveredCount: number
  deliveredPercent: string
  readCount: number
  readPercent: string
  failedCount: number
  failedPercent: string
}

export interface CampaignRecipient {
  id: number
  contactId: number
  name: string
  phone: string
  message: string
  sentStatus: string // e.g. 'Sent', 'Failed', 'Pending'
  failedReason?: string | null
}

export interface CampaignWizardForm {
  name: string
  /** One or more ContactType names (e.g. ['Lead', 'Customer']) — joined with ','
   *  only at the API-call boundary (see campaignService.buildCampaignPayload). */
  relationType: string[]

  /**
   * Which channel this campaign sends on. Defaults to 'whatsapp', so the wizard behaves exactly
   * as it did before the email channel existed until an operator changes it.
   */
  channel: MessageChannel

  /**
   * Sender connections. Previously reached through `(wizardForm as any).connectionIds`, which is
   * why it was easy to miss that the multi-connection fan-out lives in the store rather than
   * here. Typed properly now that the email channel reads it too.
   */
  connectionIds: number[]

  // ── WhatsApp channel ───────────────────────────────────────────────────────────────────────
  templateName: string
  templateId: number

  // ── Email channel ──────────────────────────────────────────────────────────────────────────

  /** The Setup-section email template this campaign renders. */
  emailTemplateId?: number
  emailTemplateName?: string

  /** Which verified sender identity to send as. */
  senderIdentityId?: number

  /** Per-campaign overrides of the template's subject and the sender's reply-to. */
  subjectOverride?: string
  replyToOverride?: string

  attachments?: CampaignAttachment[]

  trackOpens?: boolean
  trackClicks?: boolean

  // ── Shared ─────────────────────────────────────────────────────────────────────────────────
  recipientsCount: number
  contactsFilterStatus: string
  contactsFilterSource: string
  selectedContactIds: number[]
  selectAllContacts: boolean
  sendImmediately: boolean
  scheduledTime?: string
  variables?: { variableName: string; variableValue: string; mergeField?: string }[]
}

/**
 * An email attachment. Several are allowed, unlike the single WhatsApp media header — which is
 * why these do not go through the existing "file" campaign variable.
 */
export interface CampaignAttachment {
  url: string
  fileName: string
  contentType?: string | null
  sizeBytes?: number | null
}
