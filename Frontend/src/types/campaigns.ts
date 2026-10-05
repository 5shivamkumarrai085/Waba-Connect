// src/types/campaigns.ts

import type { MessageChannel } from './channel'

export interface Campaign {
  id: number
  name: string
  templateName: string
  relationType: string
  total: number
  deliveredTo: number
  readBy: number
  failedCount: number
  status: string
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

  // ── Engagement counters (Email campaigns, from CampaignEmailEventProcessor) ──────────────
  // Kept on the root Campaign (not nested in emailStats) so the list and detail views
  // can apply SignalR deltas to a single flat object without deep-merging.
  sentCount?: number
  openedCount?: number
  clickedCount?: number
  repliedCount?: number
  unsubscribedCount?: number
  complainedCount?: number

  /** False for SMTP email campaigns: the provider reports acceptance, not delivery. */
  reportsDelivery?: boolean

  /** Maker-checker state, when the campaign went through approval (details only). */
  approval?: CampaignApproval | null

  /** Recipients excluded by a compliance rule (consent, opt-out, frequency cap). */
  skippedCount?: number
  /** Why the system put the campaign on hold (e.g. an unreadable connection password); null for a manual pause. */
  pausedReason?: string | null
  /** A/B test results (details only). */
  abTest?: CampaignAbTest | null
  followUps?: CampaignFollowUp[]
  parentCampaignId?: number | null
  topic?: string | null
  isTransactional?: boolean
  /** Wall-clock send time for a recipient-local-time campaign. */
  localSendAt?: string | null

  /** Failed recipients a retry would send to again, and how many retries are used/allowed. */
  retryableCount?: number
  retryRuns?: number
  maxRetryRuns?: number
}

/** Maker-checker: who asked, who decided, and whether the current user may decide. */
export interface CampaignApproval {
  state: 'Pending' | 'Approved' | 'Rejected' | string
  requestedBy?: string | null
  requestedAt: string
  decidedBy?: string | null
  decidedAt?: string | null
  reason?: string | null
  /** Holds Campaign.Approve and is not the person who submitted it. */
  canDecide: boolean
}

/**
 * Email outcome counters — driven by normalized EmailEvents on the backend.
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
  replied: number
  unsubscribed: number
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
  email?: string
  message: string
  sentStatus: string // e.g. 'Sent', 'Failed', 'Pending'
  deliveredAt?: string
  readAt?: string
  openedAt?: string
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
  /** Dynamic segments: resolved again when the campaign starts sending. */
  selectedSegmentIds?: number[]
  /** Contact groups: every member at creation time. */
  selectedGroupIds?: number[]
  selectAllContacts: boolean
  sendImmediately: boolean
  scheduledTime?: string
  /** Deliver at scheduledTime in each recipient's own time zone rather than once for everyone. */
  recipientLocalTime?: boolean
  /** Consent topic the campaign is sent under ('marketing' when empty). */
  topic?: string
  /** Service message: exempt from quiet hours and the frequency cap (never from opt-outs). */
  isTransactional?: boolean
  /** Administrator override of a failed pre-flight check (audited on the server). */
  overridePrecheck?: boolean
  /** A/B test settings (variant A is the campaign's own template). */
  abTest?: import('../pages/Campaigns/abTestForm').AbTestForm
  /** Follow-ups sent (or tagged) automatically after the campaign. */
  followUps?: import('../pages/Campaigns/FollowUpEditor').FollowUpForm[]
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


/** A/B test results for one campaign. */
export interface CampaignAbTest {
  metric: string
  testPercent: number
  decideAt?: string | null
  decidedAt?: string | null
  winnerVariantId?: number | null
  /** Server code: best-rate, no-signal or manual (labels in campaign-options.abDecisionReasons). */
  decisionReason?: string | null
  heldRecipients: number
  variants: {
    variantId: number
    label: string
    templateName?: string | null
    subjectOverride?: string | null
    recipients: number
    sent: number
    opened: number
    clicked: number
    replied: number
    read: number
    rate: number
    isWinner: boolean
  }[]
}

/** A follow-up rule of a campaign, and what it did. */
export interface CampaignFollowUp {
  id: number
  condition: string
  delayHours: number
  action: string
  channel: string
  templateName?: string | null
  tag?: string | null
  dueAt: string
  status: 'Pending' | 'Running' | 'Done' | 'Failed' | string
  childCampaignId?: number | null
  matchedCount?: number | null
  note?: string | null
}
