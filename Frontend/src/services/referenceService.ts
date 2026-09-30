import { apiClient } from './apiClient'

export interface TimeZoneOption {
  value: string
  label: string
}

/** One choice in a picker, as the server's catalogue defines it. */
export interface Option {
  value: string
  label: string
  description?: string | null
}

/** A numeric range with the value a new form starts at. */
export interface NumberRange {
  min: number
  max: number
  default: number
}

export interface CampaignOptions {
  abMetrics: Option[]
  maxAbVariants: number
  abTestPercent: NumberRange
  abDecideAfterHours: NumberRange
  followUpConditions: Option[]
  followUpActions: Option[]
  maxFollowUps: number
  followUpDelayHours: NumberRange
  maxRetryRuns: number
  maxProofAddresses: number
}

export interface ReportScheduleOptions {
  frequencies: Option[]
  formats: Option[]
  lookbackDays: NumberRange
  maxRecipients: number
  maxDayOfMonth: number
  defaultTimeOfDay: string
  defaultDayOfWeek: number
}

export interface TemplateButtonTypeOption extends Option {
  maxCount: number
}

export interface TemplateOptions {
  /** Categories a template can be written in here (Authentication ones come from WhatsApp Manager). */
  categories: Option[]
  buttonTypes: TemplateButtonTypeOption[]
  maxButtons: number
  maxButtonLabelLength: number
  maxBodyLength: number
  maxHeaderLength: number
  maxFooterLength: number
  maxNameLength: number
  namePattern: string
}

export interface ChatOptions {
  conversationStatuses: Option[]
  stateFilters: Option[]
  assigneeFilters: Option[]
  maxReplyButtons: number
  maxReplyButtonLength: number
  maxInteractiveBodyLength: number
}

export interface WebhookEventTypeOption {
  key: string
  group: string
  description: string
}

export interface WebhookOptions {
  eventTypes: WebhookEventTypeOption[]
  deliveryStatuses: Option[]
}

export interface ConsentOptions {
  channels: Option[]
  statuses: Option[]
  sources: Option[]
}

export type SegmentFieldKind = 'text' | 'tag' | 'group' | 'date' | 'consent' | 'activity' | 'number'

export interface SegmentFieldOption {
  value: string
  label: string
  kind: SegmentFieldKind
  /** For fields with a managed list: statuses, types or sources. */
  lookup?: 'statuses' | 'types' | 'sources' | null
}

export interface SegmentFieldOptions {
  fields: SegmentFieldOption[]
  operators: Record<SegmentFieldKind, Option[]>
  days: NumberRange
  /** Accepted ages for "age" rules. */
  ages: NumberRange
}

export interface EmailPortPreset {
  port: number
  /** The security mode this port requires. */
  security: string
  label: string
}

export interface EmailOptions {
  securityModes: Option[]
  smtpPorts: EmailPortPreset[]
  imapPorts: EmailPortPreset[]
  sendRate: { min: number; max: number; default: number }
  /** Port assumed when replies are read from a mailbox derived from the SMTP host. */
  defaultImapPort: number
  deriveImapFromSmtp: boolean
}

export type ContactFieldKind = 'text' | 'phone' | 'email' | 'url' | 'date' | 'lookup'

export interface ContactFieldOption {
  key: string
  label: string
  kind: ContactFieldKind
  maxLength?: number | null
  alwaysRequired: boolean
  /** Required right now: always-required, or chosen under Settings › Contacts. */
  required: boolean
}

export interface ContactFieldOptions {
  fields: ContactFieldOption[]
  ages: NumberRange
  nameMinLength: number
  phonePattern: string
}

/**
 * Catalogues change only with a deploy, so each is fetched once per page load and shared.
 * A failed request is not cached, so the next caller retries.
 */
const cache = new Map<string, Promise<unknown>>()

const cached = <T,>(key: string, load: () => Promise<T>): Promise<T> => {
  const existing = cache.get(key) as Promise<T> | undefined
  if (existing) return existing
  const pending = load().catch((error) => {
    cache.delete(key)
    throw error
  })
  cache.set(key, pending)
  return pending
}

const get = <T,>(path: string) => () => apiClient.get(path).then(r => r.data?.data as T)

/** Read-only lists and limits for pickers and form rules, from the same catalogue the server validates against. */
export const referenceService = {
  getTimeZones: (): Promise<TimeZoneOption[]> =>
    cached('time-zones', () => get<TimeZoneOption[]>('/reference/time-zones')().then(z => z ?? [])),

  getTimeZoneAliases: (): Promise<Record<string, string>> =>
    cached('time-zone-aliases', () => get<Record<string, string>>('/reference/time-zone-aliases')().then(a => a ?? {})),

  /**
   * The viewer's own time zone under its current IANA name, if the server knows it. Browsers still
   * report some zones by their old names ("Asia/Calcutta"); the server holds the mapping.
   */
  resolveBrowserTimeZone: async (): Promise<string | null> => {
    let zone: string
    try { zone = Intl.DateTimeFormat().resolvedOptions().timeZone } catch { return null }
    if (!zone) return null
    const [zones, aliases] = await Promise.all([referenceService.getTimeZones(), referenceService.getTimeZoneAliases()])
    const current = aliases[zone] ?? zone
    return zones.some(z => z.value === current) ? current : null
  },

  /** The consent topics configured under Settings › Compliance. Not cached: an admin may change them. */
  getConsentTopics: async (): Promise<string[]> => {
    const response = await apiClient.get('/reference/consent-topics')
    return (response.data?.data ?? []) as string[]
  },

  getCampaignOptions: (channel: 'Email' | 'WhatsApp'): Promise<CampaignOptions> =>
    cached(`campaign-options:${channel}`, get<CampaignOptions>(`/reference/campaign-options?channel=${channel}`)),
  getReportScheduleOptions: (): Promise<ReportScheduleOptions> =>
    cached('report-schedule-options', get<ReportScheduleOptions>('/reference/report-schedule-options')),
  getTemplateOptions: (): Promise<TemplateOptions> =>
    cached('template-options', get<TemplateOptions>('/reference/template-options')),
  getChatOptions: (): Promise<ChatOptions> =>
    cached('chat-options', get<ChatOptions>('/reference/chat-options')),
  getWebhookOptions: (): Promise<WebhookOptions> =>
    cached('webhook-options', get<WebhookOptions>('/reference/webhook-options')),
  getConsentOptions: (): Promise<ConsentOptions> =>
    cached('consent-options', get<ConsentOptions>('/reference/consent-options')),
  getSegmentFields: (): Promise<SegmentFieldOptions> =>
    cached('segment-fields', get<SegmentFieldOptions>('/reference/segment-fields')),
  getEmailOptions: (): Promise<EmailOptions> =>
    cached('email-options', get<EmailOptions>('/reference/email-options')),
  /** Not cached: which fields are required is an administrator setting that can change at any time. */
  getContactFields: (): Promise<ContactFieldOptions> => get<ContactFieldOptions>('/reference/contact-fields')(),
}

/** The label for a stored value, falling back to the value itself for anything the catalogue lacks. */
export const labelOf = (options: Option[] | undefined, value: string | null | undefined): string =>
  options?.find(o => o.value === value)?.label ?? value ?? ''
