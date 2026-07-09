import React, { useEffect, useState } from 'react'
import { useFilterStore, useActivityLogStore } from '../store/zustand'
import { activityLogService } from '../services/activityLogService'
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
import './ActivityLogs.css'

export const ActivityLogs: React.FC = () => {
  const { activityTimeFilter, setActivityTimeFilter } = useFilterStore()
  const { activeTab, setActiveTab, searchQuery, setSearchQuery } = useActivityLogStore()

  const [metrics, setMetrics] = useState<any[]>([])
  const [successes, setSuccesses] = useState<LoginSuccessModel[]>([])
  const [audits, setAudits] = useState<AuditLogModel[]>([])
  const [isLoading, setIsLoading] = useState<boolean>(true)

  useEffect(() => {
    const fetchData = async () => {
      setIsLoading(true)
      try {
        const [
          fetchedMetrics,
          fetchedSuccesses,
          , // skipped unused fetchedErrors
          fetchedAudits
        ] = await Promise.all([
          activityLogService.getActivityMetrics(activityTimeFilter),
          activityLogService.getLoginSuccesses(searchQuery),
          activityLogService.getLoginErrors(searchQuery),
          activityLogService.getAuditLogs(searchQuery)
        ])

        setMetrics(fetchedMetrics)
        setSuccesses(fetchedSuccesses)
        setAudits(fetchedAudits)
      } catch (err) {
        console.error('Error fetching activity log data:', err)
      } finally {
        setIsLoading(false)
      }
    }

    fetchData()
  }, [activityTimeFilter, searchQuery])

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
      <div className="fade-in page-loader">
        <p className="page-loader-text">Loading activity logs...</p>
      </div>
    )
  }

  return (
    <div className="fade-in">
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
    </div>
  )
}
