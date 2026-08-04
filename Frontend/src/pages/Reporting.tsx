import React, { useEffect } from 'react'
import { motion } from 'framer-motion'
import { useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import { useReportingStore } from '../store/zustand'
import { MetricCard } from '../components/MetricCard/MetricCard'
import { DataTable } from '../components/DataTable/DataTable'
import { StatusBadge } from '../components/StatusBadge/StatusBadge'
import { FilterBar } from '../components/FilterBar/FilterBar'
import { ExportList } from '../components/ExportList/ExportList'
import { apiClient } from '../services/apiClient'
import type { AccuracyRecord, ExportItemModel, FreshnessRecord } from '../types/reporting'
import { Check } from 'lucide-react'
import { Skeleton } from '../components/Skeleton'
import { pageTransitionProps } from '../utils/motion'
import { getRandomLoadingQuote } from '../utils/quotes'
import './Reporting.css'

// Reporting page's own filter values ('today' | 'week' | 'month' | 'all') stay unchanged
// (they're what's sent to the backend) — this only overrides how each is displayed.
const timeFilterLabels: Record<string, string> = {
  today: 'Today',
  week: 'This Week',
  month: 'This Month',
  all: 'All Time'
}

// Same "strip trailing /api, hit the raw endpoint" pattern as ImportContacts' sample-CSV
// download — the API has no auth header to lose, so a plain browser navigation is enough
// to trigger the file download served by the backend's File() result.
const apiOrigin = () => {
  const base = apiClient.defaults.baseURL
  return base?.endsWith('/api') ? base.slice(0, -4) : base
}

export const Reporting: React.FC = () => {
  const navigate = useNavigate()
  const {
    reportingTimeFilter,
    setReportingTimeFilter,
    metricsCache,
    accuracyRecords,
    freshnessRecords,
    exportItems,
    features,
    isLoading,
    loadReportingData
  } = useReportingStore()

  useEffect(() => {
    loadReportingData()
  }, [])

  const metrics = metricsCache[reportingTimeFilter] || []

  // Table Headers
  const accuracyHeaders = [
    { key: 'entity', label: 'Entity' },
    { key: 'expectedCount', label: 'Expected Count' },
    { key: 'verifiedCount', label: 'Verified Count' },
    { key: 'status', label: 'Status' }
  ]

  const freshnessHeaders = [
    { key: 'entity', label: 'Entity' },
    { key: 'latestRecord', label: 'Latest Record' },
    { key: 'freshnessValue', label: 'Freshness' }
  ]

  // Cell rendering callbacks for DataTables
  const renderAccuracyCell = (row: AccuracyRecord, key: string) => {
    if (key === 'status') {
      return <StatusBadge type={row.status} text={row.status === 'verified' ? 'Verified' : 'Unverified'} />
    }
    return row[key as keyof AccuracyRecord]
  }

  const renderFreshnessCell = (row: FreshnessRecord, key: string) => {
    if (key === 'freshnessValue') {
      return <StatusBadge type={row.freshnessType} text={row.freshnessValue} />
    }
    return row[key as keyof FreshnessRecord]
  }

  // Export items: CSV downloads hit the backend directly (real-time query, freshly generated
  // per click); the campaign report is an existing, already-working page, so we just navigate.
  const handleExportAction = (item: ExportItemModel) => {
    if (item.actionType === 'external') {
      navigate('/campaigns/campaign')
      return
    }

    const endpointByItemId: Record<string, string> = {
      'metrics-report': `/Reporting/export/metrics?filter=${reportingTimeFilter}`,
      'contacts-export': '/Reporting/export/contacts',
      'chats-export': '/Reporting/export/chats'
    }

    const path = endpointByItemId[item.id]
    if (!path) {
      toast.error(`No export endpoint configured for "${item.title}".`)
      return
    }

    window.open(`${apiOrigin()}/api${path}`, '_blank')
    toast.success(`Downloading ${item.title}...`)
  }

  const showSkeleton = isLoading && (!metrics.length || !accuracyRecords || !freshnessRecords || !exportItems || !features)

  if (showSkeleton) {
    return (
      <div className="fade-in">
        <div className="reporting-header" style={{ marginBottom: 24 }}>
          <div className="reporting-title-area">
            <Skeleton variant="title" width={300} height={32} />
            <Skeleton variant="text" width={500} style={{ marginTop: 8 }} />
          </div>
        </div>
        <div className="loading-quote-banner">
          <p className="loading-quote-text">{getRandomLoadingQuote()}</p>
        </div>
        <div className="stat-cards-grid" style={{ marginBottom: 24 }}>
          <Skeleton variant="stat-card" count={4} />
        </div>
        <div style={{ marginTop: 24, display: 'flex', flexDirection: 'column', gap: 24 }}>
          <Skeleton variant="table" />
          <Skeleton variant="table" />
        </div>
      </div>
    )
  }

  return (
    <motion.div {...pageTransitionProps}>
      {/* Reporting Page Title & Date Filter Header */}
      <div className="reporting-header">
        <div className="reporting-title-area">
          <h1>Reporting & Analytics</h1>
          <p>System performance metrics, data accuracy verification, and reporting capabilities.</p>
        </div>
        <FilterBar
          options={['today', 'week', 'month', 'all']}
          activeOption={reportingTimeFilter}
          onChange={setReportingTimeFilter}
          labels={timeFilterLabels}
        />
      </div>

      {/* Grid structure of metrics stats */}
      <div className="stat-cards-grid">
        {metrics.map((metric) => (
          <MetricCard key={metric.id} metric={metric} />
        ))}
      </div>

      {/* Data Accuracy Section */}
      <div className="reporting-section-card">
        <div className="reporting-section-header">
          <h2 className="reporting-section-title">Data Accuracy — Actual vs Reported</h2>
          <p className="reporting-section-subtitle">Cross-verification of reported data against actual database records.</p>
        </div>
        <DataTable
          headers={accuracyHeaders}
          rows={accuracyRecords || []}
          renderCell={renderAccuracyCell}
        />
      </div>

      {/* Data Freshness Section */}
      <div className="reporting-section-card">
        <div className="reporting-section-header">
          <h2 className="reporting-section-title">Data Freshness</h2>
          <p className="reporting-section-subtitle">Latest record timestamps per entity — indicates real-time update status.</p>
        </div>
        <DataTable
          headers={freshnessHeaders}
          rows={freshnessRecords || []}
          renderCell={renderFreshnessCell}
        />
      </div>

      {/* Bottom Side-by-Side Panel Section */}
      <div className="reporting-bottom-grid">
        {/* Customisation Options Bullet list */}
        <div className="reporting-section-card customisation-card">
          <div className="reporting-section-header">
            <h2 className="reporting-section-title">Customisation Capability</h2>
            <p className="reporting-section-subtitle">Flexible reporting options available in the system.</p>
          </div>
          <ul className="customisation-feature-list">
            {(features || []).map((feature, idx) => (
              <li key={idx} className="customisation-feature-item">
                <Check className="customisation-feature-check-icon" size={14} strokeWidth={3} />
                <span>{feature}</span>
              </li>
            ))}
          </ul>
        </div>

        {/* Downloadable Exports list */}
        <div className="reporting-section-card">
          <div className="reporting-section-header">
            <h2 className="reporting-section-title">Export Functionality</h2>
            <p className="reporting-section-subtitle">Download report data as CSV files.</p>
          </div>
          <ExportList items={exportItems || []} onActionClick={handleExportAction} />
        </div>
      </div>
    </motion.div>
  )
}
