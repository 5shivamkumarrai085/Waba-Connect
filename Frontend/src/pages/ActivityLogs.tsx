import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { useSearchParams } from 'react-router-dom'
import { useActivityLogStore, auditCacheKey, loginCacheKey, emptyAuditPage, emptyLoginPage } from '../store/zustand'
import { Pagination } from '../components/Pagination/Pagination'
import { ColumnSelector } from '../components/ColumnSelector/ColumnSelector'
import { Avatar } from '../components/Avatar/Avatar'
import { AuditEventDetails } from '../components/AuditEventDetails/AuditEventDetails'
import { LoginAttemptDetails } from '../components/AuditEventDetails/LoginAttemptDetails'
import { MetricCard } from '../components/MetricCard/MetricCard'
import { DataTable } from '../components/DataTable/DataTable'
import { StatusBadge } from '../components/StatusBadge/StatusBadge'
import { FilterBar } from '../components/FilterBar/FilterBar'
import { SearchBar } from '../components/SearchBar/SearchBar'
import { SearchableSelect } from '../components/SearchableSelect/SearchableSelect'
import { Tabs } from '../components/Tabs/Tabs'
import type { TabItem } from '../components/Tabs/Tabs'
import type { LoginSuccessModel, LoginErrorModel, AuditLogModel, AuditLogFilters } from '../types/reporting'
import {
  Shield,
  Eye,
  SlidersHorizontal,
  RefreshCw,
  RotateCcw,
  Filter
} from 'lucide-react'
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
    isBackgroundSyncing,
    loadActivityData,
    auditFilters,
    setAuditFilters,
    resetAuditFilters,
    auditPage,
    setAuditPage,
    auditPageSize,
    setAuditPageSize,
    auditFilterOptions,
    loadAuditFilterOptions,
    loginPage,
    setLoginPage,
    loginPageSize,
    setLoginPageSize
  } = useActivityLogStore()

  const [viewingError, setViewingError] = useState<LoginErrorModel | null>(null)
  const [viewingAudit, setViewingAudit] = useState<AuditLogModel | null>(null)
  const [searchParams, setSearchParams] = useSearchParams()
  const [showFilters, setShowFilters] = useState(false)
  const [auditVisibleColumns, setAuditVisibleColumns] = useState<Record<string, boolean>>({})
  // Held locally so the dropdowns are editable without firing a query per keystroke; committed
  // to the store — and therefore to the server — only on Apply.
  const [draftFilters, setDraftFilters] = useState<AuditLogFilters>({})

  useEffect(() => {
    loadActivityData()
    loadAuditFilterOptions()
  }, [])

  // Keeps the draft in step when the filters are reset from elsewhere.
  useEffect(() => {
    setDraftFilters(auditFilters)
  }, [auditFilters])

  /**
   * Keeps the log live without a manual refresh.
   *
   * Gated on tab visibility — a security log left open on a second monitor should not keep
   * querying all night. Same shape as the dashboard's polling, and it reuses the store's
   * stale-while-revalidate path, so a refresh never flashes a skeleton over data already shown.
   */
  useEffect(() => {
    const POLL_MS = 30_000
    let timer: ReturnType<typeof setInterval> | undefined

    const start = () => {
      if (timer) return
      timer = setInterval(() => loadActivityData(), POLL_MS)
    }
    const stop = () => {
      if (!timer) return
      clearInterval(timer)
      timer = undefined
    }

    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        // Catch up immediately on return rather than waiting out the interval.
        loadActivityData()
        start()
      } else {
        stop()
      }
    }

    if (document.visibilityState === 'visible') start()
    document.addEventListener('visibilitychange', onVisibility)
    return () => {
      stop()
      document.removeEventListener('visibilitychange', onVisibility)
    }
  }, [loadActivityData])

  // The active tab lives in the URL so it can be linked to — the dashboard's Recent Activity
  // "View All" opens ?tab=audits directly, and a tab is now bookmarkable and survives a reload.
  // Sync runs both ways: the query string seeds the store on mount, and a tab click writes back.
  useEffect(() => {
    const requested = searchParams.get('tab')
    if (requested && requested !== activeTab && tabsList.some((tab) => tab.id === requested)) {
      setActiveTab(requested as typeof activeTab)
    }
  }, [searchParams])

  const handleTabChange = (tabId: string) => {
    setActiveTab(tabId as typeof activeTab)
    // replace, not push: flipping tabs shouldn't stack history entries the back button has to
    // walk through before leaving the page.
    setSearchParams({ tab: tabId }, { replace: true })
  }

  const metrics = metricsCache[activityTimeFilter] || []

  // Each tab reads its own paged slice, keyed by the same helper the store writes with — the
  // login key now carries page and size, so page 2 is no longer filed under page 1's key.
  const loginKeyState = { activityTimeFilter, searchQuery, loginPage, loginPageSize }
  const successPageData =
    successesCache[loginCacheKey(loginKeyState, 'successes')] || emptyLoginPage<LoginSuccessModel>()
  const errorPageData =
    (errorsCache[loginCacheKey(loginKeyState, 'errors')] || emptyLoginPage<LoginErrorModel>()) as
      { items: LoginErrorModel[]; totalCount: number; page: number; pageSize: number; totalPages: number }

  // The audit slice is keyed on every filter plus the page, so the key is rebuilt by the same
  // helper the store writes with — computing it twice by hand is how the two drift apart.
  const auditPageData = auditsCache[
    auditCacheKey({ activityTimeFilter, searchQuery, auditFilters, auditPage, auditPageSize })
  ] || emptyAuditPage
  const audits = auditPageData.items

  // Shown on the Filters button so an active filter is visible without opening the panel —
  // otherwise a narrowed table looks like missing data.
  const activeFilterCount = Object.values(auditFilters).filter(
    (value) => value !== undefined && value !== ''
  ).length

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
    { key: 'actions', label: '' },
    { key: 'time', label: 'Time' },
    { key: 'email', label: 'Email' },
    { key: 'ipAddress', label: 'IP Address' },
    { key: 'status', label: 'Status' }
  ]

  const successHeaders = [
    { key: 'time', label: 'Time' },
    { key: 'email', label: 'Email' },
    { key: 'ipAddress', label: 'IP Address' },
    { key: 'userAgent', label: 'User Agent' },
    { key: 'status', label: 'Status' }
  ]

  // Audit columns. `id` shows the server-assigned event number, never the database id: a
  // surrogate key leaks the row count and invites enumeration, and this is the number a user
  // quotes back. All are toggleable except ID, Timestamp and the actions cell, which are what
  // make a row identifiable at all.
  const auditColumnCatalogue = [
    { key: 'user', label: 'User' },
    { key: 'module', label: 'Module' },
    { key: 'action', label: 'Action' },
    { key: 'entity', label: 'Entity / Description' },
    { key: 'ipAddress', label: 'IP Address' },
    { key: 'status', label: 'Status' }
  ]

  // Details leads, then ID. Acting on a row should not require scrolling nine columns to the
  // right first, and both of these are pinned in place while the rest of the row scrolls under
  // them — hence the explicit classes rather than a positional CSS rule.
  const auditHeaders = [
    { key: 'actions', label: 'Details', className: 'audit-col-actions' },
    { key: 'id', label: 'ID', className: 'audit-col-id' },
    { key: 'time', label: 'Timestamp' },
    ...auditColumnCatalogue.filter((column) => auditVisibleColumns[column.key] !== false)
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
    if (key === 'id') return <span className="activity-mono audit-id-cell">{row.eventNumber}</span>
    if (key === 'time') return formatAbsoluteDateTime(row.time)
    if (key === 'user') {
      return (
        <div className="audit-user-cell">
          <Avatar name={row.user} size="small" />
          <span>{row.user}</span>
        </div>
      )
    }
    if (key === 'module') return row.module || row.category
    if (key === 'action') {
      // No action recorded at all — render plain text. Passing it to StatusBadge produced a
      // green tick beside a dash, because the badge draws a Check icon for its 'verified' type.
      if (!row.action) return <span className="audit-action-empty">—</span>

      // Coloured from the verb so a Deleted stands out from a Created without a hardcoded map
      // per module — new modules inherit the same treatment automatically. The default is
      // neutral rather than 'verified': an Updated or a Sent is not a success indicator, and
      // treating it as one made the tick meaningless.
      const verb = row.action.toLowerCase()
      const type = verb.includes('delet') || verb.includes('fail') || verb.includes('denied') ? 'error'
        : verb.includes('creat') ? 'success'
        : 'info'
      return <StatusBadge type={type} text={row.action} />
    }
    if (key === 'entity') {
      return (
        <div className="audit-entity-cell">
          <span className="audit-entity-name">
            {row.entityName || row.entityType || row.event}
          </span>
          <span className="audit-entity-desc" title={row.description}>{row.description}</span>
        </div>
      )
    }
    if (key === 'ipAddress') {
      // Background work (scheduler, webhook) genuinely has no originating address; an em dash
      // states that rather than leaving the cell looking like a data-loading failure.
      return <span className="activity-mono">{row.ipAddress || '—'}</span>
    }
    if (key === 'status') {
      const failed = row.status === 'Failed'
      return <StatusBadge type={failed ? 'error' : 'success'} text={failed ? 'Failed' : 'Success'} />
    }
    if (key === 'actions') {
      return (
        <button
          type="button"
          className="activity-view-btn activity-view-btn-icon"
          onClick={() => setViewingAudit(row)}
          title="View event details"
          aria-label={`View details for event ${row.eventNumber}`}
        >
          <Eye size={15} />
        </button>
      )
    }
    return row[key as keyof AuditLogModel] as React.ReactNode
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
            Audit Log
          </h1>
          <p>Who did what and when — every change to campaigns, contacts, templates, chats and settings — plus sign-in history.</p>
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
            placeholder="Search by user, module, action, status, email, IP..."
          />

          <div className="activity-toolbar-actions">
            {/* Column visibility and the filter panel are audit-only: the login tabs have five
                fixed columns and no server-side filters behind them. */}
            {activeTab === 'audits' && (
              <>
                <button
                  type="button"
                  className={`activity-toolbar-btn${showFilters ? ' is-active' : ''}${activeFilterCount > 0 ? ' has-filters' : ''}`}
                  onClick={() => setShowFilters((open) => !open)}
                  aria-expanded={showFilters}
                >
                  <SlidersHorizontal size={15} />
                  <span>Filters</span>
                  {activeFilterCount > 0 && <span className="activity-filter-count">{activeFilterCount}</span>}
                </button>
                <ColumnSelector
                  columns={auditColumnCatalogue}
                  visibleColumns={auditColumnCatalogue.reduce(
                    (acc, column) => ({ ...acc, [column.key]: auditVisibleColumns[column.key] !== false }),
                    {} as Record<string, boolean>
                  )}
                  onToggle={(key) =>
                    setAuditVisibleColumns((current) => ({
                      ...current,
                      [key]: current[key] === false
                    }))
                  }
                />
              </>
            )}
            <button
              type="button"
              className="activity-toolbar-btn"
              onClick={() => loadActivityData(true)}
              title="Refresh"
              aria-label="Refresh activity logs"
            >
              <RefreshCw size={15} className={isBackgroundSyncing ? 'activity-refresh-spin' : undefined} />
            </button>
          </div>

          {/* Tabs header selector */}
          <Tabs
            tabs={tabsList}
            activeTab={activeTab}
            onChange={handleTabChange}
          />
        </div>

        {activeTab === 'audits' && showFilters && (
          <div className="activity-filters-panel fade-in">
            <div className="activity-filter-field">
              <label htmlFor="audit-from">From</label>
              <input
                id="audit-from"
                type="date"
                className="form-control"
                value={draftFilters.from || ''}
                onChange={(e) => setDraftFilters({ ...draftFilters, from: e.target.value })}
              />
            </div>
            <div className="activity-filter-field">
              <label htmlFor="audit-to">To</label>
              <input
                id="audit-to"
                type="date"
                className="form-control"
                value={draftFilters.to || ''}
                onChange={(e) => setDraftFilters({ ...draftFilters, to: e.target.value })}
              />
            </div>

            {/* Every option list comes from /audit-filters — the distinct values actually present
                in the table — so a module added later appears here with no frontend change.

                These were native <select>s. The lists they carry are open-ended — every module the
                app audits, every user who has ever acted — and a browser popup over one of those
                can only be scrolled. SearchableSelect stores exactly what the select stored (the
                raw value, empty string for "all"), so the draft-filter shape, Apply and Reset all
                behave as they did. */}
            <div className="activity-filter-field">
              <label htmlFor="audit-module">Module</label>
              <SearchableSelect
                id="audit-module"
                label="Module"
                placeholder="All Modules"
                value={draftFilters.module || ''}
                options={auditFilterOptions.modules.map((module) => ({ value: module, label: module }))}
                onChange={(value) => setDraftFilters({ ...draftFilters, module: value || undefined })}
                emptyMessage="No modules recorded yet."
              />
            </div>
            <div className="activity-filter-field">
              <label htmlFor="audit-action">Action</label>
              <SearchableSelect
                id="audit-action"
                label="Action"
                placeholder="All Actions"
                value={draftFilters.action || ''}
                options={auditFilterOptions.actions.map((action) => ({ value: action, label: action }))}
                onChange={(value) => setDraftFilters({ ...draftFilters, action: value || undefined })}
                emptyMessage="No actions recorded yet."
              />
            </div>
            <div className="activity-filter-field">
              <label htmlFor="audit-user">User</label>
              <SearchableSelect
                id="audit-user"
                label="User"
                placeholder="All Users"
                value={draftFilters.userId != null ? String(draftFilters.userId) : ''}
                options={auditFilterOptions.users.map((user) => ({
                  value: String(user.id),
                  label: user.name
                }))}
                onChange={(value) =>
                  setDraftFilters({ ...draftFilters, userId: value ? Number(value) : undefined })
                }
                emptyMessage="No users recorded yet."
              />
            </div>
            <div className="activity-filter-field">
              <label htmlFor="audit-status">Status</label>
              <SearchableSelect
                id="audit-status"
                label="Status"
                placeholder="All Status"
                value={draftFilters.status || ''}
                options={auditFilterOptions.statuses.map((status) => ({ value: status, label: status }))}
                onChange={(value) => setDraftFilters({ ...draftFilters, status: value || undefined })}
                emptyMessage="No statuses recorded yet."
              />
            </div>

            <div className="activity-filter-actions">
              <button type="button" className="btn btn-secondary" onClick={resetAuditFilters}>
                <RotateCcw size={14} /> Reset
              </button>
              <button type="button" className="btn btn-primary" onClick={() => setAuditFilters(draftFilters)}>
                <Filter size={14} /> Apply Filters
              </button>
            </div>
          </div>
        )}

        {/* Tab view containers */}
        <div className="activity-tab-content-container">
          {activeTab === 'errors' && (
            <>
              <DataTable
                headers={errorHeaders}
                rows={errorPageData.items}
                renderCell={renderErrorCell}
                emptyMessage="No failed login attempts in this period."
              />
              <Pagination
                page={loginPage.errors}
                pageSize={loginPageSize}
                totalCount={errorPageData.totalCount}
                totalPages={errorPageData.totalPages}
                onPage={(next) => setLoginPage('errors', next)}
                onPageSize={setLoginPageSize}
              />
            </>
          )}

          {activeTab === 'successes' && (
            <>
              <DataTable
                headers={successHeaders}
                rows={successPageData.items}
                renderCell={renderSuccessCell}
                emptyMessage="No successful logins found matching search query."
              />
              <Pagination
                page={loginPage.successes}
                pageSize={loginPageSize}
                totalCount={successPageData.totalCount}
                totalPages={successPageData.totalPages}
                onPage={(next) => setLoginPage('successes', next)}
                onPageSize={setLoginPageSize}
              />
            </>
          )}

          {activeTab === 'audits' && (
            <>
              <DataTable
                headers={auditHeaders}
                rows={audits}
                renderCell={renderAuditCell}
                emptyMessage="No system audit logs found matching search query."
              />
              <Pagination
                page={auditPage}
                pageSize={auditPageSize}
                totalCount={auditPageData.totalCount}
                totalPages={auditPageData.totalPages}
                onPage={setAuditPage}
                onPageSize={setAuditPageSize}
              />
            </>
          )}
        </div>
      </div>

      <AuditEventDetails event={viewingAudit} onClose={() => setViewingAudit(null)} />

      <LoginAttemptDetails attempt={viewingError} onClose={() => setViewingError(null)} />
    </motion.div>
  )
}
