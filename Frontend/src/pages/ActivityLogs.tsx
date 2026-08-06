import React, { useEffect } from 'react'
import { motion } from 'framer-motion'
import { useActivityLogStore } from '../store/zustand'
import { MetricCard } from '../components/MetricCard/MetricCard'
import { DataTable } from '../components/DataTable/DataTable'
import { StatusBadge } from '../components/StatusBadge/StatusBadge'
import { FilterBar } from '../components/FilterBar/FilterBar'
import { SearchBar } from '../components/SearchBar/SearchBar'
import { Tabs } from '../components/Tabs/Tabs'
import type { TabItem } from '../components/Tabs/Tabs'
import { EmptyState } from '../components/EmptyState/EmptyState'
import type { LoginSuccessModel, AuditLogModel } from '../types/reporting'
import { Shield } from 'lucide-react'
import { Skeleton } from '../components/Skeleton'
import { pageTransitionProps } from '../utils/motion'
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
    auditsCache,
    isLoading,
    loadActivityData
  } = useActivityLogStore()

  useEffect(() => {
    loadActivityData()
  }, [])

  const metrics = metricsCache[activityTimeFilter] || []
  const successes = successesCache[searchQuery] || []
  const audits = auditsCache[searchQuery] || []

  // Tabs structure
  const tabsList: TabItem[] = [
    { id: 'errors', label: 'Login Errors', iconName: 'ShieldAlert', type: 'error' },
    { id: 'successes', label: 'Login Successes', iconName: 'ShieldCheck', type: 'success' },
    { id: 'audits', label: 'Audit Events', iconName: 'FileText', type: 'audit' }
  ]

  // Columns configurations
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
    { key: 'description', label: 'Description' }
  ]

  // Cell formatters
  const renderSuccessCell = (row: LoginSuccessModel, key: string) => {
    if (key === 'status') {
      return <StatusBadge type="success" text="Success" />
    }
    if (key === 'userAgent') {
      return <div className="user-agent-cell" title={row.userAgent}>{row.userAgent}</div>
    }
    return row[key as keyof LoginSuccessModel]
  }

  const renderAuditCell = (row: AuditLogModel, key: string) => {
    if (key === 'category') {
      // Return custom colors for categories
      const type = row.category === 'Auth' ? 'success' : row.category === 'Settings' ? 'warning' : 'verified'
      return <StatusBadge type={type} text={row.category} />
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
            <EmptyState
              iconName="ShieldAlert"
              message="No failed login attempts in this period."
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
    </motion.div>
  )
}
