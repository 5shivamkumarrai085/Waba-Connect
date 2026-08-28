import React, { useEffect, useMemo, useState } from 'react'
import { motion, AnimatePresence } from 'framer-motion'
import toast from 'react-hot-toast'
import {
  ChevronDown,
  Download,
  FileSpreadsheet,
  FileText,
  Filter,
  FolderOpen,
  Info,
  Play,
  Save,
  Table2,
  Trash2,
  X
} from 'lucide-react'
import { DataTable } from '../DataTable/DataTable'
import { Pagination } from '../Pagination/Pagination'
import { Modal } from '../Modal/Modal'
import { StatusBadge } from '../StatusBadge/StatusBadge'
import { DateRangePicker } from '../DateRangePicker/DateRangePicker'
import { MultiSelectChips } from '../MultiSelectChips/MultiSelectChips'
import { Menu, MenuItem } from '../Menu/Menu'
import { reportingService } from '../../services/reportingService'
import { getErrorMessage } from '../../utils/errorHelper'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'
import usePermission from '../../hooks/usePermission'
import { isRequestCancelled } from '../../services/apiClient'
import type {
  ReportColumn,
  ReportFilterOptions,
  ReportFilters,
  ReportRow,
  ReportExportFormat,
  SavedReport
} from '../../types/reporting'
import { emptyReportFilters } from '../../types/reporting'
import './ReportBuilder.css'

const PAGE_SIZE_OPTIONS = [10, 25, 50, 100]

/**
 * The report builder: pick columns, narrow with filters, run, export, and optionally save the
 * whole combination for next time.
 *
 * <para>
 * Deliberately has no "report type" or "group by" selector. The backend has one data source —
 * chat messages plus the campaign recipients they came from — and no aggregation step; offering
 * controls for either would be exactly the hardcoded-looking chrome the rest of this rebuild was
 * trying to remove. What is offered here is everything the query actually supports.
 * </para>
 */
export const ReportBuilder: React.FC = () => {
  const { has } = usePermission()
  const canManage = has('Reporting.Manage')
  const canExport = has('Reporting.Export')

  const [columns, setColumns] = useState<ReportColumn[]>([])
  const [filterOptions, setFilterOptions] = useState<ReportFilterOptions | null>(null)
  const [loadingMeta, setLoadingMeta] = useState(true)

  const [selectedColumns, setSelectedColumns] = useState<string[]>([])
  const [filters, setFilters] = useState<ReportFilters>(emptyReportFilters())
  const [showFilters, setShowFilters] = useState(false)

  const [rows, setRows] = useState<ReportRow[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [totalPages, setTotalPages] = useState(0)
  const [hasRun, setHasRun] = useState(false)
  const [isRunning, setIsRunning] = useState(false)
  const [exportingFormat, setExportingFormat] = useState<ReportExportFormat | null>(null)
  const [isExportMenuOpen, setIsExportMenuOpen] = useState(false)

  const [savedReports, setSavedReports] = useState<SavedReport[]>([])
  const [loadingSaved, setLoadingSaved] = useState(true)
  const [saveModalOpen, setSaveModalOpen] = useState(false)

  // Metadata first: the column catalogue and filter option lists, so the builder never renders
  // controls for something that doesn't exist in the data.
  useEffect(() => {
    let cancelled = false

    Promise.all([reportingService.getReportColumns(), reportingService.getReportFilterOptions()])
      .then(([cols, options]) => {
        if (cancelled) return
        setColumns(cols)
        setSelectedColumns(cols.filter((c) => c.defaultVisible).map((c) => c.key))
        setFilterOptions(options)
      })
      .catch((err) => {
        if (cancelled || isRequestCancelled(err)) return
        toast.error(getErrorMessage(err, 'Could not load the report builder.'))
      })
      .finally(() => {
        if (!cancelled) setLoadingMeta(false)
      })

    return () => {
      cancelled = true
    }
  }, [])

  const loadSavedReports = () => {
    setLoadingSaved(true)
    reportingService
      .getSavedReports()
      .then(setSavedReports)
      .catch((err) => {
        if (isRequestCancelled(err)) return
        toast.error(getErrorMessage(err, 'Could not load saved reports.'))
      })
      .finally(() => setLoadingSaved(false))
  }

  useEffect(loadSavedReports, [])

  const orderedSelectedColumns = useMemo(
    () => columns.filter((c) => selectedColumns.includes(c.key)),
    [columns, selectedColumns]
  )

  const runReport = async (request: ReportFilters) => {
    setIsRunning(true)
    try {
      const page = await reportingService.runReport(request)
      setRows(page.items)
      setTotalCount(page.totalCount)
      setTotalPages(page.totalPages)
      setHasRun(true)
    } catch (err) {
      if (isRequestCancelled(err)) return
      toast.error(getErrorMessage(err, 'Could not run the report.'))
    } finally {
      setIsRunning(false)
    }
  }

  const handleRun = () => runReport({ ...filters, page: 1 })

  const handlePage = (page: number) => {
    const next = { ...filters, page }
    setFilters(next)
    runReport(next)
  }

  const handlePageSize = (pageSize: number) => {
    const next = { ...filters, page: 1, pageSize }
    setFilters(next)
    runReport(next)
  }

  const handleExport = async (format: ReportExportFormat) => {
    if (selectedColumns.length === 0) {
      toast.error('Choose at least one column before exporting.')
      return
    }

    setExportingFormat(format)
    setIsExportMenuOpen(false)
    try {
      await reportingService.exportReport(filters, selectedColumns, format)
      toast.success(`Report exported as ${format.toUpperCase()}.`)
    } catch (err) {
      if (isRequestCancelled(err)) return
      toast.error(getErrorMessage(err, `Could not export the report as ${format.toUpperCase()}.`))
    } finally {
      setExportingFormat(null)
    }
  }

  const runSavedReport = async (report: SavedReport) => {
    setSelectedColumns(report.columns.length > 0 ? report.columns : columns.filter((c) => c.defaultVisible).map((c) => c.key))
    setFilters({ ...report.filters, page: 1 })
    setShowFilters(true)
    await runReport({ ...report.filters, page: 1 })
    // Best-effort — a failed stamp must never make "run" look like it failed.
    reportingService.touchSavedReport(report.id).catch(() => {})
  }

  const handleDeleteSaved = async (report: SavedReport) => {
    try {
      await reportingService.deleteSavedReport(report.id)
      toast.success(`"${report.name}" deleted.`)
      loadSavedReports()
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not delete that report.'))
    }
  }

  const activeFilterCount = useMemo(() => {
    let count = 0
    if (filters.from || filters.to) count++
    if (filters.campaignIds?.length) count++
    if (filters.connectionIds?.length) count++
    if (filters.templateNames?.length) count++
    if (filters.messageTypes?.length) count++
    if (filters.directions?.length) count++
    if (filters.statuses?.length) count++
    if (filters.failedOnly) count++
    if (filters.search?.trim()) count++
    return count
  }, [filters])

  const tableHeaders = useMemo(
    () => [
      { key: 'actions', label: '', className: 'actions-col' },
      ...orderedSelectedColumns.map((c) => ({ key: c.key, label: c.label }))
    ],
    [orderedSelectedColumns]
  )

  const renderCell = (row: ReportRow, key: string): React.ReactNode => {
    if (key === 'actions') return null

    const value = (row as unknown as Record<string, unknown>)[key]

    switch (key) {
      case 'timestamp':
      case 'sentAt':
      case 'deliveredAt':
      case 'readAt':
        return value ? formatAbsoluteDateTime(value as string) : '—'
      case 'status':
        return value ? <StatusBadge type={String(value)} text={String(value)} /> : '—'
      case 'direction':
        return value ? (
          <span className={`report-direction report-direction-${String(value).toLowerCase()}`}>
            {String(value)}
          </span>
        ) : '—'
      case 'responded':
        return row.direction === 'Outgoing' ? (value ? 'Yes' : 'No') : '—'
      case 'responseMinutes':
        return value != null ? `${value} min` : '—'
      case 'content':
        return value ? (
          <span className="report-content-cell" title={String(value)}>
            {String(value)}
          </span>
        ) : '—'
      default:
        return (value as React.ReactNode) ?? '—'
    }
  }

  return (
    <div className="report-builder">
      {/* ── Build Your Report ─────────────────────────────────────────── */}
      <div className="reporting-section-card">
        <div className="reporting-section-header">
          <h2 className="reporting-section-title">
            <Table2 size={18} />
            <span>Build Your Report</span>
          </h2>
          <p className="reporting-section-subtitle">
            Every send and reply across your connections, filtered however you need it.
          </p>
        </div>

        <div className="report-builder-columns">
          <div className="report-builder-columns-head">
            <span className="report-builder-columns-label">Columns</span>
          </div>

          {/* A checkable grid rather than the MultiSelectChips dropdown here: with up to 16
              columns, showing every option at once is faster to scan than opening a menu, and
              the selection itself is shown as the same removable-chip row for consistency. */}
          <div className="report-column-grid">
            {columns.map((col) => {
              const isChecked = selectedColumns.includes(col.key)
              return (
                <label key={col.key} className="report-column-option">
                  <input
                    type="checkbox"
                    checked={isChecked}
                    onChange={() =>
                      setSelectedColumns((prev) =>
                        isChecked ? prev.filter((k) => k !== col.key) : [...prev, col.key]
                      )
                    }
                  />
                  <span>{col.label}</span>
                  {col.derivedNote && (
                    <span className="report-column-derived" title={col.derivedNote}>
                      <Info size={12} />
                    </span>
                  )}
                </label>
              )
            })}
          </div>

          {selectedColumns.length > 0 && (
            <div className="ms-chips-row report-selected-columns">
              {orderedSelectedColumns.map((col) => (
                <span key={col.key} className="ms-chip">
                  {col.label}
                  <button
                    type="button"
                    className="ms-chip-remove"
                    onClick={() => setSelectedColumns((prev) => prev.filter((k) => k !== col.key))}
                    aria-label={`Remove ${col.label}`}
                  >
                    <X size={11} />
                  </button>
                </span>
              ))}
            </div>
          )}
        </div>

        <div className="report-builder-toolbar">
          <button
            type="button"
            className={`report-toolbar-btn${showFilters ? ' is-active' : ''}`}
            onClick={() => setShowFilters((v) => !v)}
          >
            <Filter size={14} />
            <span>Advanced Filters</span>
            {activeFilterCount > 0 && <span className="activity-filter-count">{activeFilterCount}</span>}
          </button>

          <button
            type="button"
            className="report-run-btn"
            onClick={handleRun}
            disabled={isRunning || loadingMeta || selectedColumns.length === 0}
          >
            <Play size={14} />
            <span>{isRunning ? 'Running…' : 'Run Report'}</span>
          </button>

          <Menu
            open={isExportMenuOpen}
            onOpenChange={setIsExportMenuOpen}
            align="end"
            offset={6}
            ariaLabel="Export format"
            trigger={(props) => (
              <button
                {...props}
                type="button"
                className="report-toolbar-btn"
                disabled={!canExport || !hasRun || exportingFormat !== null}
                title={!canExport ? "You don't have permission to export reports." : undefined}
              >
                <Download size={14} />
                <span>{exportingFormat ? `Exporting ${exportingFormat.toUpperCase()}…` : 'Export'}</span>
                <ChevronDown size={13} />
              </button>
            )}
          >
            <MenuItem onSelect={() => handleExport('csv')}>
              <FileText size={14} /> <span>CSV</span>
            </MenuItem>
            <MenuItem onSelect={() => handleExport('xlsx')}>
              <FileSpreadsheet size={14} /> <span>Excel (.xlsx)</span>
            </MenuItem>
            <MenuItem onSelect={() => handleExport('pdf')}>
              <FileText size={14} /> <span>PDF</span>
            </MenuItem>
          </Menu>

          {canManage && (
            <button
              type="button"
              className="report-toolbar-btn"
              onClick={() => setSaveModalOpen(true)}
              disabled={selectedColumns.length === 0}
            >
              <Save size={14} />
              <span>Save Report</span>
            </button>
          )}
        </div>

        <AnimatePresence initial={false}>
          {showFilters && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              transition={{ duration: 0.18, ease: 'easeOut' }}
              className="report-filters-wrap"
            >
              <div className="activity-filters-panel report-filters-panel">
                <div className="activity-filter-field report-filters-daterange">
                  <label>Date Range</label>
                  <DateRangePicker
                    from={filters.from}
                    to={filters.to}
                    min={filterOptions?.earliestRecord}
                    max={filterOptions?.latestRecord}
                    onChange={({ from, to }) => setFilters((f) => ({ ...f, from, to }))}
                  />
                </div>

                <MultiSelectChips
                  label="Campaigns"
                  options={(filterOptions?.campaigns ?? []).map((c) => ({ value: String(c.id), label: c.name }))}
                  selected={(filters.campaignIds ?? []).map(String)}
                  onChange={(vals) => setFilters((f) => ({ ...f, campaignIds: vals.map(Number) }))}
                  emptyMessage="No campaigns yet."
                />

                <MultiSelectChips
                  label="Connections"
                  options={(filterOptions?.connections ?? []).map((c) => ({ value: String(c.id), label: c.name }))}
                  selected={(filters.connectionIds ?? []).map(String)}
                  onChange={(vals) => setFilters((f) => ({ ...f, connectionIds: vals.map(Number) }))}
                  emptyMessage="No connections yet."
                />

                <MultiSelectChips
                  label="Templates"
                  options={(filterOptions?.templates ?? []).map((t) => ({ value: t, label: t }))}
                  selected={filters.templateNames ?? []}
                  onChange={(vals) => setFilters((f) => ({ ...f, templateNames: vals }))}
                  emptyMessage="No templates used yet."
                />

                <MultiSelectChips
                  label="Message Type"
                  options={(filterOptions?.messageTypes ?? []).map((t) => ({ value: t, label: t }))}
                  selected={filters.messageTypes ?? []}
                  onChange={(vals) => setFilters((f) => ({ ...f, messageTypes: vals }))}
                />

                <MultiSelectChips
                  label="Direction"
                  options={(filterOptions?.directions ?? []).map((d) => ({ value: d, label: d }))}
                  selected={filters.directions ?? []}
                  onChange={(vals) => setFilters((f) => ({ ...f, directions: vals }))}
                />

                <MultiSelectChips
                  label="Status"
                  options={(filterOptions?.statuses ?? []).map((s) => ({ value: s, label: s }))}
                  selected={filters.statuses ?? []}
                  onChange={(vals) => setFilters((f) => ({ ...f, statuses: vals }))}
                />

                <div className="activity-filter-field">
                  <label htmlFor="report-search">Search</label>
                  <input
                    id="report-search"
                    type="text"
                    className="form-control"
                    placeholder="Contact, phone or message text…"
                    value={filters.search ?? ''}
                    onChange={(e) => setFilters((f) => ({ ...f, search: e.target.value || null }))}
                  />
                </div>

                <div className="activity-filter-field report-filters-failed">
                  <label htmlFor="report-failed-only">
                    <input
                      id="report-failed-only"
                      type="checkbox"
                      checked={filters.failedOnly ?? false}
                      onChange={(e) => setFilters((f) => ({ ...f, failedOnly: e.target.checked || null }))}
                    />
                    <span>Failures only</span>
                  </label>
                </div>

                <div className="activity-filter-actions">
                  <button
                    type="button"
                    className="btn-secondary"
                    onClick={() => setFilters(emptyReportFilters())}
                  >
                    Clear Filters
                  </button>
                  <button type="button" className="btn-primary" onClick={handleRun} disabled={isRunning}>
                    Apply &amp; Run
                  </button>
                </div>
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>

      {/* ── Results ────────────────────────────────────────────────────── */}
      {hasRun && (
        <div className="reporting-section-card">
          <div className="reporting-section-header">
            <h2 className="reporting-section-title">Results</h2>
            <p className="reporting-section-subtitle">
              {totalCount.toLocaleString()} row{totalCount === 1 ? '' : 's'} match{totalCount === 1 ? 'es' : ''} the current filters.
            </p>
          </div>

          <DataTable
            headers={tableHeaders}
            rows={rows}
            renderCell={renderCell}
            emptyMessage="No rows match these filters. Try widening the date range."
          />

          <Pagination
            page={filters.page}
            pageSize={filters.pageSize}
            totalCount={totalCount}
            totalPages={totalPages}
            onPage={handlePage}
            onPageSize={handlePageSize}
            pageSizeOptions={PAGE_SIZE_OPTIONS}
          />
        </div>
      )}

      {/* ── Saved Reports ──────────────────────────────────────────────── */}
      <div className="reporting-section-card">
        <div className="reporting-section-header">
          <h2 className="reporting-section-title">
            <FolderOpen size={18} />
            <span>Saved Reports</span>
          </h2>
          <p className="reporting-section-subtitle">
            Your own reports, plus anything a teammate has shared. Saving requires the Reporting.Manage permission.
          </p>
        </div>

        {loadingSaved ? (
          <p className="reporting-section-subtitle">Loading…</p>
        ) : savedReports.length === 0 ? (
          <p className="audit-detail-empty">No saved reports yet — build one above and save it.</p>
        ) : (
          <DataTable
            headers={[
              { key: 'actions', label: '', className: 'actions-col' },
              { key: 'name', label: 'Name' },
              { key: 'owner', label: 'Owner' },
              { key: 'visibility', label: 'Visibility' },
              { key: 'lastRun', label: 'Last Run' }
            ]}
            rows={savedReports}
            renderCell={(report: SavedReport, key: string) => {
              if (key === 'actions') {
                return (
                  <div className="report-saved-actions">
                    <button
                      type="button"
                      className="activity-view-btn"
                      onClick={() => runSavedReport(report)}
                    >
                      <Play size={13} />
                      <span>Run</span>
                    </button>
                    {report.isOwner && (
                      <button
                        type="button"
                        className="activity-view-btn activity-view-btn-icon"
                        onClick={() => handleDeleteSaved(report)}
                        title="Delete"
                        aria-label={`Delete ${report.name}`}
                      >
                        <Trash2 size={13} />
                      </button>
                    )}
                  </div>
                )
              }
              if (key === 'name') {
                return (
                  <div className="audit-entity-cell">
                    <span className="audit-entity-name">{report.name}</span>
                    {report.description && <span className="audit-entity-desc">{report.description}</span>}
                  </div>
                )
              }
              if (key === 'owner') return report.ownerName || '—'
              if (key === 'visibility') {
                return report.isShared ? (
                  <StatusBadge type="success" text="Shared" />
                ) : (
                  <StatusBadge type="warning" text="Private" />
                )
              }
              if (key === 'lastRun') return report.lastRunAt ? formatAbsoluteDateTime(report.lastRunAt) : 'Never run'
              return null
            }}
          />
        )}
      </div>

      {saveModalOpen && (
        <SaveReportModal
          isOpen={saveModalOpen}
          onClose={() => setSaveModalOpen(false)}
          columns={selectedColumns}
          filters={filters}
          onSaved={() => {
            setSaveModalOpen(false)
            loadSavedReports()
          }}
        />
      )}
    </div>
  )
}

// ── Save dialog ────────────────────────────────────────────────────────────

interface SaveReportModalProps {
  isOpen: boolean
  onClose: () => void
  columns: string[]
  filters: ReportFilters
  onSaved: () => void
}

const SaveReportModal: React.FC<SaveReportModalProps> = ({ isOpen, onClose, columns, filters, onSaved }) => {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [isShared, setIsShared] = useState(false)
  const [isSaving, setIsSaving] = useState(false)

  const handleSave = async () => {
    const trimmed = name.trim()
    if (!trimmed) {
      toast.error('Give the report a name.')
      return
    }

    setIsSaving(true)
    try {
      await reportingService.createSavedReport({
        name: trimmed,
        description: description.trim() || null,
        columns,
        filters,
        isShared
      })
      toast.success(`"${trimmed}" saved.`)
      onSaved()
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not save this report.'))
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title="Save Report"
      subtitle="Keep this column and filter combination so you don't have to rebuild it."
      size="sm"
      footer={
        <>
          <button type="button" className="btn-secondary" onClick={onClose} disabled={isSaving}>
            Cancel
          </button>
          <button type="button" className="btn-primary" onClick={handleSave} disabled={isSaving}>
            {isSaving ? 'Saving…' : 'Save Report'}
          </button>
        </>
      }
    >
      <div className="report-save-form">
        <div className="activity-filter-field">
          <label htmlFor="save-report-name">Name</label>
          <input
            id="save-report-name"
            type="text"
            className="form-control"
            value={name}
            onChange={(e) => setName(e.target.value)}
            autoFocus
            placeholder="e.g. Weekly failures — Support"
          />
        </div>
        <div className="activity-filter-field">
          <label htmlFor="save-report-description">Description (optional)</label>
          <textarea
            id="save-report-description"
            className="form-control"
            rows={2}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </div>
        <div className="activity-filter-field report-filters-failed">
          <label htmlFor="save-report-shared">
            <input
              id="save-report-shared"
              type="checkbox"
              checked={isShared}
              onChange={(e) => setIsShared(e.target.checked)}
            />
            <span>Share with everyone who can view reports</span>
          </label>
          {/* Private is the default on purpose: a report's filters can encode something the
              author would not choose to publish — a single agent's failures, one campaign's
              breakdown — so visibility opens on request, not by omission. */}
          <p className="audit-detail-empty" style={{ marginTop: 4 }}>
            Private reports are visible only to you.
          </p>
        </div>
      </div>
    </Modal>
  )
}

export default ReportBuilder
