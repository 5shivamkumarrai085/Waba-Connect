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
