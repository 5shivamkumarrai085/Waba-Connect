/**
 * Email-channel types, mirroring the backend's DTOs in Models/DTOs/Email.
 *
 * Note what is deliberately absent: there is no field anywhere here for a provider secret. The
 * API returns only `hasSmtpPassword` / `hasImapPassword` booleans, and typing it that way
 * means a component cannot render a credential even by mistake.
 */

/** Standard SMTP is the only transport; kept as a type so the API shape stays explicit. */
export type EmailProviderType = 'Smtp'

export type SmtpSecurityMode = 'None' | 'StartTls' | 'SslOnConnect'

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
  /**
   * Whether this sender may actually be used. Computed server-side from the same rule the send
   * path applies, so the wizard can explain why a sender is unselectable instead of letting a
   * campaign fail later in a worker.
   */
  canSend: boolean
}

/** One live DNS check of a sender domain (SPF, DKIM, DMARC, MX). */
export interface DomainCheck {
  key: string
  /** 'pass' | 'warn' | 'fail' */
  level: string
  title: string
  detail: string
}

export interface DomainHealth {
  domain: string
  checks: DomainCheck[]
}

export interface EmailConnection {
  id: number
  connectionId?: number | null
  connectionName: string
  nickname?: string | null
  description?: string | null

  provider: EmailProviderType
  isActive: boolean

  smtpHost?: string | null
  smtpPort?: number | null
  smtpSecurity?: SmtpSecurityMode | null
  smtpUsername?: string | null
  hasSmtpPassword: boolean
  /** False when a stored password cannot be read with this server's key and must be re-entered. */
  credentialsReadable: boolean

  // IMAP — for inbound reply polling
  imapHost?: string | null
  imapPort?: number | null
  imapSecurity?: SmtpSecurityMode | null
  imapUsername?: string | null
  hasImapPassword: boolean
  /** Opt-in to accept an IMAP certificate that fails validation (shared hosting). */
  imapAllowInvalidCertificate?: boolean
  /** The mailbox replies are read from: the saved host, or one derived from the SMTP host. */
  effectiveImapHost?: string | null
  imapHostIsDerived?: boolean
  imapLastPolledAt?: string | null
  imapLastError?: string | null

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
 * Step 2. The password is write-only: leaving it empty keeps whatever is stored, which is how an
 * operator changes the port without re-typing a credential.
 */
export interface SaveEmailProviderPayload {
  provider: EmailProviderType
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
  imapAllowInvalidCertificate?: boolean
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

// ── Queue monitoring ──────────────────────────────────────────────────────────────────────────
