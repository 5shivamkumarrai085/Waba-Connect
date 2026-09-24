/**
 * Email-channel types, mirroring the backend's DTOs in Models/DTOs/Email.
 *
 * Note what is deliberately absent: there is no field anywhere here for a provider secret. The
 * API returns only `hasSecretAccessKey` / `hasSmtpPassword` booleans, and typing it that way
 * means a component cannot render a credential even by mistake.
 */

export type EmailProviderType = 'AmazonSes' | 'Smtp'

export type EmailAuthMode = 'IamRole' | 'AccessKey'

export type SmtpSecurityMode = 'None' | 'StartTls' | 'SslOnConnect'

export type EmailIdentityStatus = 'NotStarted' | 'Pending' | 'Verified' | 'Failed' | 'TemporaryFailure'

/** Server-derived, so the UI does not re-implement the rule. */
export type EmailConnectionStatus = 'Connected' | 'Disconnected' | 'Setup pending' | 'Needs attention'

export interface EmailProviderCapabilities {
  supportsEventWebhooks: boolean
  supportsDkimProvisioning: boolean
  supportsSuppressionApi: boolean
  supportsInboundReceiving: boolean
  maxMessageBytes: number
}

export interface EmailSenderIdentity {
  id: number
  emailConfigurationId: number
  displayName: string
  emailAddress: string
  replyTo?: string | null
  isDefault: boolean
  isActive: boolean
  verificationStatus: EmailIdentityStatus
  sendingDomainId?: number | null
  domainName?: string | null
  /**
   * Whether this sender may actually be used. Computed server-side from the same rule the send
   * path applies, so the wizard can explain why a sender is unselectable instead of letting a
   * campaign fail later in a worker.
   */
  canSend: boolean
}

export interface DnsRecord {
  type: 'CNAME' | 'TXT' | 'MX'
  name: string
  value: string
  /** 'DKIM' | 'SPF' | 'DMARC' | 'MAIL FROM' — what breaks without it. */
  purpose: string
  /** False for records that improve deliverability but are not needed to send. */
  required: boolean
}

export interface EmailSendingDomain {
  id: number
  domainName: string
  verificationStatus: EmailIdentityStatus
  dkimStatus: EmailIdentityStatus
  mailFromDomain?: string | null
  mailFromStatus: EmailIdentityStatus
  lastCheckedAt?: string | null
  lastCheckMessage?: string | null
  requiredDnsRecords: DnsRecord[]
}

export interface EmailConnection {
  id: number
  connectionId?: number | null
  connectionName: string
  nickname?: string | null
  description?: string | null

  provider: EmailProviderType
  isActive: boolean

  region?: string | null
  authMode: EmailAuthMode
  /** The key id is an identifier, not a secret — showing it lets an operator confirm which
   * credential is in use. */
  accessKeyId?: string | null
  /** Whether a secret is stored. Never the value. */
  hasSecretAccessKey: boolean
  configurationSet?: string | null

  smtpHost?: string | null
  smtpPort?: number | null
  smtpSecurity?: SmtpSecurityMode | null
  smtpUsername?: string | null
  hasSmtpPassword: boolean

  // IMAP — for inbound reply polling
  imapHost?: string | null
  imapPort?: number | null
  imapSecurity?: SmtpSecurityMode | null
  imapUsername?: string | null
  hasImapPassword: boolean

  maxSendRatePerSecond?: number | null
  defaultFromName?: string | null
  defaultFromEmail?: string | null
  defaultReplyTo?: string | null

  /** Null until step 2 of the wizard has been completed at least once. */
  configuredAt?: string | null
  lastTestedAt?: string | null
  lastTestSucceeded?: boolean | null
  lastTestMessage?: string | null

  status: EmailConnectionStatus
  capabilities?: EmailProviderCapabilities | null

  senders: EmailSenderIdentity[]
  domains: EmailSendingDomain[]

  createdAt: string
  updatedAt?: string | null
}

// ── Requests ──────────────────────────────────────────────────────────────────────────────────

/** Step 1 of the connect-email wizard. */
export interface CreateEmailConnectionPayload {
  name: string
  displayName: string
  emailAddress: string
  replyToEmail?: string
  description?: string
  nickname?: string
}

/**
 * Step 2. Both secret fields are write-only: leaving one empty keeps whatever is stored, which
 * is how an operator changes a region without re-typing a credential.
 */
export interface SaveEmailProviderPayload {
  provider: EmailProviderType
  region?: string
  authMode?: EmailAuthMode
  accessKeyId?: string
  secretAccessKey?: string
  configurationSet?: string
  smtpHost?: string
  smtpPort?: number
  smtpSecurity?: SmtpSecurityMode
  smtpUsername?: string
  smtpPassword?: string
  maxSendRatePerSecond?: number
  defaultFromName?: string
  defaultFromEmail?: string
  defaultReplyTo?: string
  isActive: boolean
}

/** Tests credentials before they are saved, so Test Connection works on an unsaved form. */
export interface TestEmailProviderPayload extends SaveEmailProviderPayload {
  emailConfigurationId?: number
}

export interface SendTestEmailPayload {
  toAddress: string
  senderIdentityId?: number
}

/** IMAP settings for inbound reply polling. Empty password keeps the stored value. */
export interface SaveImapSettingsPayload {
  imapHost: string
  imapPort?: number
  imapSecurity?: SmtpSecurityMode
  imapUsername?: string
  /** Write-only. Leave blank to keep the stored password. */
  imapPassword?: string
}

export interface SaveEmailSenderPayload {
  displayName: string
  emailAddress: string
  replyTo?: string
  isDefault: boolean
  isActive: boolean
}

export interface EmailProviderTestResult {
  success: boolean
  message: string
  details?: Record<string, string> | null
}

// ── Templates ─────────────────────────────────────────────────────────────────────────────────

/**
 * The email templates managed under Setup. The single source of truth for outgoing email —
 * campaigns render from these same rows, so there is no separate campaign-template type.
 */
export interface EmailTemplate {
  id: number
  key: string
  name: string
  subject: string
  bodyHtml: string
  textBody?: string | null
  preheaderText?: string | null
  language?: string | null
  description?: string | null
  isEnabled: boolean
  /** Seeded templates other features resolve by key. Cannot be deleted. */
  isSystem: boolean
  /** The declared, comma-separated list the editor offers as insertable chips. */
  availableVariables?: string | null
  /**
   * The placeholders the content actually references, extracted server-side. This is what the
   * campaign wizard builds its variable inputs from — the declared list above goes stale as soon
   * as somebody edits the body.
   */
  detectedVariables: string[]
  createdAt: string
  updatedAt?: string | null
}

export interface SaveEmailTemplatePayload {
  name: string
  key?: string
  subject: string
  bodyHtml: string
  textBody?: string
  preheaderText?: string
  language?: string
  description?: string
  availableVariables?: string
  isEnabled: boolean
}

export interface EmailTemplatePreview {
  subject: string
  bodyHtml: string
  textBody?: string | null
  /** Anything still unresolved, surfaced rather than hidden. */
  unresolvedVariables: string[]
}

// ── Suppression ───────────────────────────────────────────────────────────────────────────────

export type SuppressionReason = 'Bounce' | 'Complaint' | 'Unsubscribe' | 'Manual' | 'ListImport'

export interface EmailSuppression {
  id: number
  emailAddressNormalized: string
  scope: 'Global' | 'Connection'
  connectionId?: number | null
  reason: SuppressionReason
  source?: string | null
  detail?: string | null
  expiresAt?: string | null
  suppressedAt: string
}

// ── Queue monitoring ──────────────────────────────────────────────────────────────────────────

export interface EmailQueueStats {
  transport: string
  channelEnabled: boolean
  visibilityTimeoutSeconds: number
  maxAttempts: number
  queues: Array<{
    queue: string
    pending: number
    leased: number
    deadLettered: number
    completed: number
    expiredLeases: number
    oldestPendingAt?: string | null
    /** The number that actually matters — depth alone cannot tell a burst from a stall. */
    oldestPendingMinutes?: number | null
  }>
}
