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
  id: string
  time: string
  event: string
  category: string
  user: string
  /** Null for background callers — the scheduler and webhook have no originating address. */
  ipAddress?: string | null
  description: string
}
