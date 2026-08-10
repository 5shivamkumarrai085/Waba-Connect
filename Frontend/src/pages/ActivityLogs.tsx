import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { useActivityLogStore } from '../store/zustand'
import { MetricCard } from '../components/MetricCard/MetricCard'
import { DataTable } from '../components/DataTable/DataTable'
import { StatusBadge } from '../components/StatusBadge/StatusBadge'
import { FilterBar } from '../components/FilterBar/FilterBar'
import { SearchBar } from '../components/SearchBar/SearchBar'
import { Tabs } from '../components/Tabs/Tabs'
import type { TabItem } from '../components/Tabs/Tabs'
import { Modal } from '../components/Modal/Modal'
import type { LoginSuccessModel, LoginErrorModel, AuditLogModel } from '../types/reporting'
import { Shield, Eye } from 'lucide-react'
import { Skeleton } from '../components/Skeleton'
import { pageTransitionProps } from '../utils/motion'
import { formatAbsoluteDateTime } from '../utils/dateHelper'
import './ActivityLogs.css'

export const ActivityLogs: React.FC = () => {
  const {
    activeTab,
    setActiveTab,
    searchQuery,
    setSearchQuery,
    activityTimeFilter,
    setActivityTimeFilter,
    metricsCache,
    successesCache,
    errorsCache,
    auditsCache,
    isLoading,
    loadActivityData
  } = useActivityLogStore()

  const [viewingError, setViewingError] = useState<LoginErrorModel | null>(null)

  useEffect(() => {
    loadActivityData()
  }, [])

  const cacheKey = `${activityTimeFilter}|${searchQuery}`
  const metrics = metricsCache[activityTimeFilter] || []
  const successes = successesCache[cacheKey] || []
  const errors = (errorsCache[cacheKey] || []) as LoginErrorModel[]
  const audits = auditsCache[cacheKey] || []

  // Tabs structure
  const tabsList: TabItem[] = [
    { id: 'errors', label: 'Login Errors', iconName: 'ShieldAlert', type: 'error' },
    { id: 'successes', label: 'Login Successes', iconName: 'ShieldCheck', type: 'success' },
    { id: 'audits', label: 'Audit Events', iconName: 'FileText', type: 'audit' }
  ]

  // Columns configurations.
  //
  // The failure reason is deliberately not a column: it is free text of wildly varying length
  // and was stretching the row. It lives in the View dialog, which is what that button is for.
  const errorHeaders = [
    { key: 'time', label: 'Time' },
    { key: 'email', label: 'Email' },
    { key: 'ipAddress', label: 'IP Address' },
    { key: 'status', label: 'Status' },
    { key: 'actions', label: '' }
  ]

  const successHeaders = [
    { key: 'time', label: 'Time' },
    { key: 'email', label: 'Email' },
    { key: 'ipAddress', label: 'IP Address' },
    { key: 'userAgent', label: 'User Agent' },
    { key: 'status', label: 'Status' }
  ]

  const auditHeaders = [
    { key: 'time', label: 'Time' },
    { key: 'event', label: 'Event' },
    { key: 'category', label: 'Category' },
    { key: 'user', label: 'User' },
    { key: 'ipAddress', label: 'IP Address' },
    { key: 'description', label: 'Description' }
  ]

  // Cell formatters.
  //
  // Every renderer runs `time` through formatAbsoluteDateTime. This page was the only list in
  // the app that didn't, so it fell through to the raw value and rendered the UTC ISO string
  // straight from the API.
  const renderErrorCell = (row: LoginErrorModel, key: string) => {
    if (key === 'time') return formatAbsoluteDateTime(row.time)
    if (key === 'status') return <StatusBadge type="error" text="Failed" />
    if (key === 'ipAddress') return <span className="activity-mono">{row.ipAddress}</span>
    if (key === 'actions') {
      return (
        <button
          type="button"
          className="activity-view-btn"
          onClick={() => setViewingError(row)}
          title="View attempt details"
        >
          <Eye size={14} />
          <span>View</span>
        </button>
      )
    }
    return row[key as keyof LoginErrorModel]
  }

  const renderSuccessCell = (row: LoginSuccessModel, key: string) => {
    if (key === 'time') return formatAbsoluteDateTime(row.time)
    if (key === 'status') {
      return <StatusBadge type="success" text="Success" />
    }
    if (key === 'ipAddress') return <span className="activity-mono">{row.ipAddress}</span>
    if (key === 'userAgent') {
      return <div className="user-agent-cell" title={row.userAgent}>{row.userAgent}</div>
    }
    return row[key as keyof LoginSuccessModel]
  }

  const renderAuditCell = (row: AuditLogModel, key: string) => {
    if (key === 'time') return formatAbsoluteDateTime(row.time)
    if (key === 'category') {
      // Return custom colors for categories
      const type = row.category === 'Auth' ? 'success' : row.category === 'Settings' ? 'warning' : 'verified'
      return <StatusBadge type={type} text={row.category} />
    }
    if (key === 'ipAddress') {
      // Background work (scheduler, webhook) genuinely has no originating address; an em dash
      // states that rather than leaving the cell looking like a data-loading failure.
      return <span className="activity-mono">{row.ipAddress || '—'}</span>
    }
    if (key === 'description') {
      return <div className="audit-description-cell" title={row.description}>{row.description}</div>
    }
    return row[key as keyof AuditLogModel]
  }

  if (isLoading && metrics.length === 0) {
    return (
      <div className="fade-in">
        <div className="activity-header" style={{ marginBottom: 24 }}>
          <div className="activity-title-area">
            <Skeleton variant="title" width={300} height={32} />
            <Skeleton variant="text" width={500} style={{ marginTop: 8 }} />
          </div>
        </div>
        <div className="stat-cards-grid margin-bottom-24" style={{ marginBottom: 24 }}>
          <Skeleton variant="stat-card" count={4} />
        </div>
        <div className="activity-section-card">
          <Skeleton variant="table" />
        </div>
      </div>
    )
  }

  return (
    <motion.div {...pageTransitionProps}>
      {/* Activity Logs page header and date range filters */}
      <div className="activity-header">
        <div className="activity-title-area">
          <h1>
            <Shield size={24} color="var(--primary)" />
            Security & Activity Logs
          </h1>
          <p>Login attempts, authentication history and system audit events.</p>
        </div>
        <FilterBar
          options={['today', 'yesterday', 'week', 'month', 'all']}
          activeOption={activityTimeFilter}
          onChange={setActivityTimeFilter}
        />
      </div>

      {/* Grid of metrics count summary cards */}
      <div className="stat-cards-grid margin-bottom-24">
        {metrics.map((metric) => (
          <MetricCard key={metric.id} metric={metric} />
        ))}
      </div>

      {/* Main card covering logs query, tab selections, and tables */}
      <div className="activity-section-card">
        <div className="activity-filter-section">
          {/* Reusable Search Bar */}
          <SearchBar
            value={searchQuery}
            onChange={setSearchQuery}
            placeholder="Search logs..."
          />
          
          {/* Tabs header selector */}
          <Tabs
            tabs={tabsList}
            activeTab={activeTab}
            onChange={setActiveTab}
          />
        </div>

        {/* Tab view containers */}
        <div className="activity-tab-content-container">
          {activeTab === 'errors' && (
            <DataTable
              headers={errorHeaders}
              rows={errors}
              renderCell={renderErrorCell}
              emptyMessage="No failed login attempts in this period."
            />
          )}

          {activeTab === 'successes' && (
            <DataTable
              headers={successHeaders}
              rows={successes}
              renderCell={renderSuccessCell}
              emptyMessage="No successful logins found matching search query."
            />
          )}

          {activeTab === 'audits' && (
            <DataTable
              headers={auditHeaders}
              rows={audits}
              renderCell={renderAuditCell}
              emptyMessage="No system audit logs found matching search query."
            />
          )}
        </div>
      </div>

      <Modal
        isOpen={viewingError !== null}
        onClose={() => setViewingError(null)}
        title="Failed sign-in attempt"
        size="md"
      >
        {viewingError && (
          <div className="activity-detail">
            <div className="activity-detail-row"><span>Time</span><span>{formatAbsoluteDateTime(viewingError.time)}</span></div>
            <div className="activity-detail-row"><span>Email tried</span><span>{viewingError.email}</span></div>
            <div className="activity-detail-row"><span>IP address</span><span>{viewingError.ipAddress}</span></div>
            <div className="activity-detail-row"><span>User agent</span><span>{viewingError.userAgent}</span></div>
            <div className="activity-detail-error">
              <strong>Why it failed</strong>
              <p>{viewingError.reason}</p>
            </div>
          </div>
        )}
      </Modal>
    </motion.div>
  )
}
