import React, { useEffect, useState } from 'react'
import { useFilterStore } from '../store/zustand'
import { reportingService } from '../services/reportingService'
import { MetricCard } from '../components/MetricCard/MetricCard'
import { DataTable } from '../components/DataTable/DataTable'
import { StatusBadge } from '../components/StatusBadge/StatusBadge'
import { FilterBar } from '../components/FilterBar/FilterBar'
import { ExportList } from '../components/ExportList/ExportList'
import type { MetricCardModel, AccuracyRecord, FreshnessRecord, ExportItemModel } from '../types/reporting'
import { Check } from 'lucide-react'
import './Reporting.css'

export const Reporting: React.FC = () => {
  const { reportingTimeFilter, setReportingTimeFilter } = useFilterStore()
  
  const [metrics, setMetrics] = useState<MetricCardModel[]>([])
  const [accuracyRecords, setAccuracyRecords] = useState<AccuracyRecord[]>([])
  const [freshnessRecords, setFreshnessRecords] = useState<FreshnessRecord[]>([])
  const [exportItems, setExportItems] = useState<ExportItemModel[]>([])
  const [features, setFeatures] = useState<string[]>([])
  const [isLoading, setIsLoading] = useState<boolean>(true)

  useEffect(() => {
    const fetchData = async () => {
      setIsLoading(true)
      try {
        const [
          fetchedMetrics,
          fetchedAccuracy,
          fetchedFreshness,
          fetchedExports,
          fetchedFeatures
        ] = await Promise.all([
          reportingService.getReportingMetrics(reportingTimeFilter),
          reportingService.getAccuracyRecords(),
          reportingService.getFreshnessRecords(),
          reportingService.getExportItems(),
          reportingService.getCustomisationFeatures()
        ])

        setMetrics(fetchedMetrics)
        setAccuracyRecords(fetchedAccuracy)
        setFreshnessRecords(fetchedFreshness)
        setExportItems(fetchedExports)
        setFeatures(fetchedFeatures)
      } catch (err) {
        console.error('Error fetching reporting logs data:', err)
      } finally {
        setIsLoading(false)
      }
    }

    fetchData()
  }, [reportingTimeFilter])

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

  if (isLoading) {
    return (
      <div className="fade-in page-loader">
        <p className="page-loader-text">Loading system metrics...</p>
      </div>
    )
  }

  return (
    <div className="fade-in">
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
          rows={accuracyRecords}
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
          rows={freshnessRecords}
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
            {features.map((feature, idx) => (
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
          <ExportList items={exportItems} />
        </div>
      </div>
    </div>
  )
}
