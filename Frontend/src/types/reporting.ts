// src/types/reporting.ts

export interface MetricCardModel {
  id: string
  title: string
  description: string
  value: string | number
  badgeText?: string
  badgeType?: 'excellent' | 'fast' | 'needs-review' | 'stale' | 'low' | 'high' | 'available'
  iconName: string // maps to lucide icons
}

export interface AccuracyRecord {
  id: string
  entity: string
  expectedCount: number
  verifiedCount: number
  status: 'verified' | 'unverified'
}

export interface FreshnessRecord {
  id: string
  entity: string
  latestRecord: string
  freshnessValue: string
  freshnessType: 'fresh' | 'warning' | 'stale'
}

export interface ExportItemModel {
  id: string
  title: string
  description: string
  iconName: string
  actionType: 'download' | 'external'
  /** API path to download from, supplied by the server. Empty for 'external' items. */
  endpoint?: string
  /** True when the endpoint honours the page's time filter (metrics only). */
  acceptsFilter?: boolean
}

// ── Report builder ───────────────────────────────────────────────────────────
// Mirrors Backend/Models/DTOs/Reporting/ReportQueryDtos.cs.

/** A column the builder can offer. Served by the server so the two can never disagree. */
export interface ReportColumn {
  key: string
  label: string
  defaultVisible: boolean
  /** Set when the value is inferred rather than stored — shown as a tooltip in the UI. */
  derivedNote?: string | null
}

/** The filter set the builder sends with every query, export and saved report. */
export interface ReportFilters {
  from?: string | null
  to?: string | null
  campaignIds?: number[] | null
  connectionIds?: number[] | null
  contactIds?: number[] | null
  messageTypes?: string[] | null
  directions?: string[] | null
  statuses?: string[] | null
  templateNames?: string[] | null
  search?: string | null
  failedOnly?: boolean | null
  page: number
  pageSize: number
}

export const emptyReportFilters = (): ReportFilters => ({ page: 1, pageSize: 25 })

/** One row of the report — a chat message, or a campaign recipient that never produced one. */
export interface ReportRow {
  id: number
  timestamp: string
  contactName?: string | null
  contactPhone?: string | null
  campaignName?: string | null
  templateName?: string | null
  connectionName?: string | null
  direction?: string | null
  messageType?: string | null
  status?: string | null
  content?: string | null
  sentAt?: string | null
  deliveredAt?: string | null
  readAt?: string | null
  failureReason?: string | null
  responded?: boolean | null
  responseMinutes?: number | null
}

interface ReportOption {
  id: number
  name: string
}

/** Filter option lists built from values actually present in the data — never hardcoded. */
export interface ReportFilterOptions {
  campaigns: ReportOption[]
  connections: ReportOption[]
  templates: string[]
  messageTypes: string[]
  directions: string[]
  statuses: string[]
  earliestRecord?: string | null
  latestRecord?: string | null
}

/** A saved report definition, as the list and the editor see it. */
export interface SavedReport {
  id: number
  name: string
  description?: string | null
  columns: string[]
  filters: ReportFilters
  isShared: boolean
  /** True only when the signed-in user owns it — the only case edit/delete are allowed. */
  isOwner: boolean
  ownerName?: string | null
  lastRunAt?: string | null
  createdAt: string
  updatedAt: string
}

export interface SaveReportRequest {
  name: string
  description?: string | null
  columns: string[]
  filters: ReportFilters
  isShared: boolean
}

export type ReportExportFormat = 'csv' | 'xlsx' | 'pdf'

export interface LoginSuccessModel {
  id: string
  time: string
  email: string
  ipAddress: string
  userAgent: string
  status: 'Success'
}

/**
 * A failed sign-in. Same shape as a success plus `reason` — which the backend has always
 * returned; the page just never rendered it.
 */
export interface LoginErrorModel {
  id: string
  time: string
  email: string
  ipAddress: string
  userAgent: string
  status: 'Failed'
  reason: string
}

export interface AuditLogModel {
  /** Database id. React key only — never displayed; the UI shows eventNumber. */
  id: number
  /** The user-facing event number, from its own sequence rather than the primary key. */
  eventNumber: number
  time: string
  event: string
  category: string
  /** Derived from the event's prefix, e.g. "Contact.Updated" → "Contact". */
  module?: string | null
  /** Derived from the event's suffix, e.g. "Contact.Updated" → "Updated". */
  action?: string | null
  status?: string | null
  user: string
  userId?: number | null
  /** Null for background callers — the scheduler and webhook have no originating address. */
  ipAddress?: string | null
  userAgent?: string | null
  description: string
  entityType?: string | null
  entityId?: string | null
  entityName?: string | null
  /** JSON array of per-entity change sets; parsed by the details panel. */
  changesJson?: string | null
  /**
   * JSON envelope of event-specific detail a field diff cannot express — currently the messages a
   * chat delete removed. Null on almost every event. Parsed by the details panel.
   */
  metadataJson?: string | null
}

/** Filters currently applied to the Audit Events tab. */
export interface AuditLogFilters {
  from?: string
  to?: string
  module?: string
  action?: string
  userId?: number
  status?: string
}

/** Distinct values present in the audit table, for the filter dropdowns. */
export interface AuditFilterOptions {
  modules: string[]
  actions: string[]
  statuses: string[]
  users: { id: number; name: string }[]
}

export interface AuditLogPage {
  items: AuditLogModel[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

/** A page of sign-in attempts. Same envelope as AuditLogPage, different row type. */
export interface LoginAttemptPage<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}
