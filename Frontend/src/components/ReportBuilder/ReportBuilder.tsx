import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import {
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  Columns3,
  Download,
  FileSpreadsheet,
  FileText,
  Filter,
  Info,
  MoreVertical,
  Pencil,
  Play,
  RotateCcw,
  Save,
  Trash2,
  X,
  Search,
} from 'lucide-react'
import { Modal } from '../Modal/Modal'
import { StatusBadge } from '../StatusBadge/StatusBadge'
import { MultiSelectChips } from '../MultiSelectChips/MultiSelectChips'
import { Menu, MenuItem, type MenuTriggerProps } from '../Menu/Menu'
import { Skeleton } from '../Skeleton'
import { reportingService } from '../../services/reportingService'
import { getErrorMessage } from '../../utils/errorHelper'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'
import usePermission from '../../hooks/usePermission'
import { isRequestCancelled } from '../../services/apiClient'
import type {
  ReportColumn,
  ReportFilterOptions,
  ReportFilters,
  ReportGroupRow,
  ReportMetadata,
  ReportRow,
  ReportType,
  ReportExportFormat,
  SavedReport,
  ReportSummary,
} from '../../types/reporting'
import { emptyReportFilters } from '../../types/reporting'
import './ReportBuilder.css'
import { ReportKpiCards } from './ReportKpiCards'
import { ReportAnalytics } from './ReportAnalytics'
import { SearchableSelect } from '../SearchableSelect/SearchableSelect'
import { FilterBar } from '../FilterBar/FilterBar'

const PAGE_SIZE_OPTIONS = [10, 25, 50, 100]

/** Rows the saved-report table shows per page. Short list; short pages. */
const SAVED_PAGE_SIZE = 10

/**
 * A response gap, as a duration rather than a decimal.
 *
 * The server reports minutes because that is the unit the measurement is meaningful in, but
 * "0.05 min" is not a legible answer to "how quickly did they reply" — hh:mm:ss is.
 */
const formatResponseTime = (minutes: number | null | undefined): string => {
  if (minutes === null || minutes === undefined) return '—'

  const totalSeconds = Math.max(0, Math.round(minutes * 60))
  const hours = Math.floor(totalSeconds / 3600)
  const mins = Math.floor((totalSeconds % 3600) / 60)
  const secs = totalSeconds % 60
  const pad = (n: number) => String(n).padStart(2, '0')

  return `${pad(hours)}:${pad(mins)}:${pad(secs)}`
}

/** "01 Aug 2026" — the format the header chip and the results subtitle both read in. */
const formatRangeDate = (value: string): string => {
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value

  return parsed.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })
}

/**
 * Reporting & Analytics.
 *
 * <para>
 * One component for the whole page rather than a page shell plus a builder. The header's date
 * range, its Filters toggle and its Export menu all act on the same query the card below builds,
 * and splitting them across two components would mean lifting every piece of that state up to a
 * parent that does nothing else with it.
 * </para>
 * <para>
 * Everything selectable here — report types, the sections and groupings each one allows, the
 * column catalogue, and every filter's options — is fetched from the server. Nothing on this page
 * is a list the client keeps: a report type the UI offers is one the query service knows how to
 * scope, because they are the same list.
 * </para>
 */
export const ReportBuilder: React.FC = () => {
  const { has } = usePermission()
  const canManage = has('Reporting.Manage')
  const canExport = has('Reporting.Export')

  const [metadata, setMetadata] = useState<ReportMetadata | null>(null)
  const [filterOptions, setFilterOptions] = useState<ReportFilterOptions | null>(null)
  const [loadingMeta, setLoadingMeta] = useState(true)

  const [selectedColumns, setSelectedColumns] = useState<string[]>([])
  const [filters, setFilters] = useState<ReportFilters>(emptyReportFilters())
  // The filters the results on screen were actually produced by. Editing a control changes what
  // will be asked for; it must not change the "Showing data for …" line above rows that predate it.
  const [appliedFilters, setAppliedFilters] = useState<ReportFilters>(emptyReportFilters())
  // Collapsed on load. The panel is ten controls tall, and opening the page with all of them
  // expanded pushed the results — the thing the page exists to show — below the fold.
  const [showFilters, setShowFilters] = useState(false)

  const [rows, setRows] = useState<ReportRow[]>([])
  const [groupRows, setGroupRows] = useState<ReportGroupRow[]>([])
  const [totalCount, setTotalCount] = useState(0)

  // The headline cards and the charts beside the table. Fetched alongside every run, from the same
  // filters, so the numbers on the cards always describe the rows underneath them.
  const [summary, setSummary] = useState<ReportSummary | null>(null)
  const [isSummaryLoading, setIsSummaryLoading] = useState(true)
  const [totalPages, setTotalPages] = useState(0)
  const [isRunning, setIsRunning] = useState(false)
  const [hasRun, setHasRun] = useState(false)
  const [exportingFormat, setExportingFormat] = useState<ReportExportFormat | null>(null)

  const [savedReports, setSavedReports] = useState<SavedReport[]>([])
  const [loadingSaved, setLoadingSaved] = useState(true)
  const [savedPage, setSavedPage] = useState(1)
  const [savedMenuId, setSavedMenuId] = useState<number | null>(null)
  const [editingReport, setEditingReport] = useState<SavedReport | null>(null)
  const [saveModalOpen, setSaveModalOpen] = useState(false)

  // The report type currently in force, resolved through the metadata so every dependent control
  // (sections, groupings, the column list) is derived from one place rather than tracked separately.
  const reportType = useMemo<ReportType | null>(() => {
    if (!metadata) return null
    return (
      metadata.reportTypes.find((t) => t.key === filters.reportType) ??
      metadata.reportTypes[0] ??
      null
    )
  }, [metadata, filters.reportType])

  const availableColumns = useMemo<ReportColumn[]>(() => {
    if (!metadata || !reportType) return []
    const allowed = new Set(reportType.columnKeys)
    return metadata.columns.filter((c) => allowed.has(c.key))
  }, [metadata, reportType])

  const groupByOptions = useMemo(() => {
    if (!metadata || !reportType) return []
    const allowed = new Set(reportType.groupByKeys)
    return metadata.groupBys.filter((g) => allowed.has(g.key))
  }, [metadata, reportType])

  const isGrouped = Boolean(filters.groupBy && filters.groupBy !== 'none')
  const wasGrouped = Boolean(appliedFilters.groupBy && appliedFilters.groupBy !== 'none')

  // Selected columns in catalogue order, so removing and re-adding a column does not shuffle the
  // table into the order they happened to be clicked in.
  const orderedSelectedColumns = useMemo(
    () => availableColumns.filter((c) => selectedColumns.includes(c.key)),
    [availableColumns, selectedColumns]
  )

  // ── Loading ──────────────────────────────────────────────────────────────

  const runReport = useCallback(async (request: ReportFilters) => {
    setIsRunning(true)
    try {
      const grouped = Boolean(request.groupBy && request.groupBy !== 'none')

      if (grouped) {
        const page = await reportingService.runGroupedReport(request)
        setGroupRows(page.items)
        setRows([])
        setTotalCount(page.totalCount)
        setTotalPages(page.totalPages)
      } else {
        const page = await reportingService.runReport(request)
        setRows(page.items)
        setGroupRows([])
        setTotalCount(page.totalCount)
        setTotalPages(page.totalPages)
      }

      setAppliedFilters(request)
      setHasRun(true)

      // Not awaited: the rows are already on screen by now, and the cards catch up.
      void loadSummaryRef.current(request)
    } catch (err) {
      if (isRequestCancelled(err)) return
      toast.error(getErrorMessage(err, 'Could not run the report.'))
    } finally {
      setIsRunning(false)
    }
  }, [])

  /**
   * The summary behind the cards and charts.
   *
   * Deliberately separate from the row fetch and never awaited by it: the table is the thing the
   * user asked for, and a slow aggregate must not hold it up. A failure here leaves the previous
   * figures on screen and is not surfaced as a toast — the report itself still worked, and an error
   * about a chart would be noise on top of a page that is functioning.
   */
  const loadSummary = useCallback(async (request: ReportFilters) => {
    setIsSummaryLoading(true)
    try {
      setSummary(await reportingService.getReportSummary(request))
    } catch (err) {
      if (isRequestCancelled(err)) return
      // Swallowed on purpose. See above.
    } finally {
      setIsSummaryLoading(false)
    }
  }, [])

  const loadSummaryRef = useRef(loadSummary)
  loadSummaryRef.current = loadSummary

  // Kept in a ref so the metadata effect can run the opening report without listing runReport as a
  // dependency and re-running the whole load when it changes identity.
  const runReportRef = useRef(runReport)
  runReportRef.current = runReport

  useEffect(() => {
    let cancelled = false

    Promise.all([reportingService.getReportMetadata(), reportingService.getReportFilterOptions()])
      .then(([meta, options]) => {
        if (cancelled || !meta) return

        setMetadata(meta)
        setFilterOptions(options)

        const first = meta.reportTypes[0]
        if (!first) return

        const opening: ReportFilters = {
          ...emptyReportFilters(),
          reportType: first.key,
          dataSection: first.dataSections[0]?.key ?? 'all',
          groupBy: 'none'
        }

        setSelectedColumns(first.defaultColumnKeys)
        setFilters(opening)
        // Opens with results rather than an empty frame and a Run button. The landing state of a
        // reporting page is a report; making the first look require a click adds a step to the
        // one thing every visit starts with.
        void runReportRef.current(opening)
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

  const loadSavedReports = useCallback(() => {
    setLoadingSaved(true)
    reportingService
      .getSavedReports()
      .then(setSavedReports)
      .catch((err) => {
        if (isRequestCancelled(err)) return
        toast.error(getErrorMessage(err, 'Could not load saved reports.'))
      })
      .finally(() => setLoadingSaved(false))
  }, [])

  useEffect(() => {
    loadSavedReports()
  }, [loadSavedReports])

  // ── Configuration changes ────────────────────────────────────────────────

  /**
   * Switching report type resets the section, the grouping and the columns to that type's own
   * defaults.
   *
   * Carrying them over would be worse than it sounds: a section or grouping the new type does not
   * offer is silently discarded by the server, so the controls would show one thing and the rows
   * would be another.
   */
  const handleReportTypeChange = (key: string) => {
    const next = metadata?.reportTypes.find((t) => t.key === key)
    if (!next) return

    const request: ReportFilters = {
      ...filters,
      reportType: next.key,
      dataSection: next.dataSections[0]?.key ?? 'all',
      groupBy: 'none',
      page: 1
    }

    setSelectedColumns(next.defaultColumnKeys)
    setFilters(request)
    void runReport(request)
  }

  const handleDataSectionChange = (key: string) => {
    const request = { ...filters, dataSection: key, page: 1 }
    setFilters(request)
    void runReport(request)
  }

  const handleGroupByChange = (key: string) => {
    const request = { ...filters, groupBy: key, page: 1 }
    setFilters(request)
    void runReport(request)
  }

  /**
   * The four periods the rest of the product offers.
   *
   * Deliberately the same set, in the same order, as the Dashboard's switcher — someone moving
   * between the two pages should not have to learn a second vocabulary for the same idea. Each one
   * resolves to the `from`/`to` the Advanced Filters panel writes, so a period and a hand-picked
   * range are the same thing to the query, the export and a saved report.
   *
   * Weeks and months run to today rather than to their calendar end: a report about "this month"
   * on the 3rd is about the three days so far, not about a month that has not happened.
   */
  const timeFilterRanges = useMemo(() => {
    const iso = (d: Date) => {
      const local = new Date(d.getTime() - d.getTimezoneOffset() * 60_000)
      return local.toISOString().slice(0, 10)
    }

    const today = new Date()
    today.setHours(0, 0, 0, 0)

    // Monday-first: the business week, not the browser locale's.
    const startOfWeek = new Date(today)
    startOfWeek.setDate(today.getDate() - ((today.getDay() + 6) % 7))

    const startOfMonth = new Date(today.getFullYear(), today.getMonth(), 1)

    return {
      today: { from: iso(today), to: iso(today) },
      week: { from: iso(startOfWeek), to: iso(today) },
      month: { from: iso(startOfMonth), to: iso(today) },
      // "All" is the absence of a date filter rather than a very wide one, so the query stays
      // unbounded and the comparison cards correctly show no trend.
      all: { from: null, to: null }
    } as const
  }, [])

  /** Which period the current range corresponds to. A hand-picked range matches none of them. */
  const activeTimeFilter = useMemo(() => {
    const current = { from: filters.from ?? null, to: filters.to ?? null }
    const match = (Object.keys(timeFilterRanges) as (keyof typeof timeFilterRanges)[]).find(
      (key) =>
        (timeFilterRanges[key].from ?? null) === current.from &&
        (timeFilterRanges[key].to ?? null) === current.to
    )
    return match ?? ''
  }, [timeFilterRanges, filters.from, filters.to])

  /**
   * Applies a period and runs the report.
   *
   * Goes through the same setFilters/runReport pair every other control uses, so this is not a
   * second way of fetching a report — it is the existing way with the dates filled in.
   */
  const handleTimeFilter = (key: string) => {
    const range = timeFilterRanges[key as keyof typeof timeFilterRanges]
    if (!range) return

    const next = { ...filters, from: range.from, to: range.to, page: 1 }
    setFilters(next)
    void runReport(next)
  }

  const handleApplyFilters = () => {
    const request = { ...filters, page: 1 }
    setFilters(request)
    void runReport(request)
  }

  const handleClearFilters = () => {
    // The report type, section and grouping are the report's identity, not a filter on it —
    // clearing the filters should narrow nothing, not silently switch which report is on screen.
    const request: ReportFilters = {
      ...emptyReportFilters(),
      reportType: filters.reportType,
      dataSection: filters.dataSection,
      groupBy: filters.groupBy,
      pageSize: filters.pageSize
    }

    setFilters(request)
    void runReport(request)
  }

  const handlePage = (page: number) => {
    const request = { ...appliedFilters, page }
    setFilters((f) => ({ ...f, page }))
    void runReport(request)
  }

  const handlePageSize = (pageSize: number) => {
    const request = { ...appliedFilters, page: 1, pageSize }
    setFilters((f) => ({ ...f, page: 1, pageSize }))
    void runReport(request)
  }

  const toggleColumn = (key: string) => {
    setSelectedColumns((prev) =>
      prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key]
    )
  }

  const handleExport = async (format: ReportExportFormat) => {
    if (!isGrouped && selectedColumns.length === 0) {
      toast.error('Choose at least one column before exporting.')
      return
    }

    setExportingFormat(format)
    try {
      // The applied filters, not the edited ones: the file has to be the report on screen, or it
      // is a different document going out under the name of the one someone was looking at.
      await reportingService.exportReport(appliedFilters, selectedColumns, format)
      toast.success(`Report exported as ${format.toUpperCase()}.`)
    } catch (err) {
      if (isRequestCancelled(err)) return
      toast.error(getErrorMessage(err, `Could not export the report as ${format.toUpperCase()}.`))
    } finally {
      setExportingFormat(null)
    }
  }

  // ── Saved reports ────────────────────────────────────────────────────────

  const runSavedReport = async (report: SavedReport) => {
    const type = metadata?.reportTypes.find((t) => t.key === report.filters.reportType)

    const request: ReportFilters = { ...report.filters, page: 1 }

    setSelectedColumns(
      report.columns.length > 0 ? report.columns : type?.defaultColumnKeys ?? selectedColumns
    )
    setFilters(request)
    setShowFilters(true)

    await runReport(request)
    // Best-effort — a failed stamp must never make a successful run look like a failure.
    reportingService.touchSavedReport(report.id).catch(() => {})
  }

  const editSavedReport = (report: SavedReport) => {
    setEditingReport(report)
    setSaveModalOpen(true)
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

  // ── Derived display ──────────────────────────────────────────────────────

  const activeFilterCount = useMemo(() => {
    let count = 0
    if (filters.from || filters.to) count++
    if (filters.campaignIds?.length) count++
    if (filters.connectionIds?.length) count++
    if (filters.templateNames?.length) count++
    if (filters.messageTypes?.length) count++
    if (filters.directions?.length) count++
    if (filters.statuses?.length) count++
    if (filters.contactIds?.length) count++
    if (filters.failureReasons?.length) count++
    if (filters.agents?.length) count++
    if (filters.failedOnly) count++
    if (filters.search?.trim()) count++
    return count
  }, [filters])

  /**
   * The period the rows on screen actually cover.
   *
   * Read from the applied filters rather than the draft ones, so the subtitle describes the report
   * that ran and not the range someone is part-way through choosing.
   */
  const appliedRangeLabel = useMemo(() => {
    const { from, to } = appliedFilters
    if (from && to) return `${formatRangeDate(from)} - ${formatRangeDate(to)}`
    if (from) return `from ${formatRangeDate(from)}`
    if (to) return `up to ${formatRangeDate(to)}`
    return 'all time'
  }, [appliedFilters.from, appliedFilters.to])

  const resultColumns = wasGrouped ? metadata?.groupColumns ?? [] : orderedSelectedColumns

  const savedTotalPages = Math.max(1, Math.ceil(savedReports.length / SAVED_PAGE_SIZE))
  const pagedSavedReports = savedReports.slice(
    (savedPage - 1) * SAVED_PAGE_SIZE,
    savedPage * SAVED_PAGE_SIZE
  )

  // ── Cells ────────────────────────────────────────────────────────────────

  const renderRowCell = (row: ReportRow, key: string): React.ReactNode => {
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
        ) : (
          '—'
        )
      case 'responded':
        // Only outgoing messages can be answered. "No" against an incoming row would read as a
        // finding rather than as a question that does not apply.
        return row.direction === 'Outgoing' ? (value ? 'Yes' : 'No') : '—'
      case 'responseMinutes':
        return formatResponseTime(row.responseMinutes)
      case 'content':
      case 'templateOrContent':
      case 'failureReason':
        return value ? (
          <span className="report-content-cell" title={String(value)}>
            {String(value)}
          </span>
        ) : (
          '—'
        )
      default:
        return (value as React.ReactNode) ?? '—'
    }
  }

  const renderGroupCell = (row: ReportGroupRow, key: string): React.ReactNode => {
    switch (key) {
      case 'label':
        return <span className="report-group-label">{row.label}</span>
      case 'responseRate':
        return row.responseRate === null || row.responseRate === undefined
          ? '—'
          : `${row.responseRate}%`
      case 'firstAt':
      case 'lastAt': {
        const value = row[key]
        return value ? formatAbsoluteDateTime(value) : '—'
      }
      default: {
        const value = (row as unknown as Record<string, unknown>)[key]
        return typeof value === 'number' ? value.toLocaleString() : (value as React.ReactNode) ?? '—'
      }
    }
  }

  /**
   * Filters the column list as you type.
   *
   * Declared with the rest of the state, above the loading early-return — a hook placed after it
   * runs only on some renders, which React rejects outright.
   */
  const [columnSearch, setColumnSearch] = useState('')

  const visibleColumns = useMemo(() => {
    const term = columnSearch.trim().toLowerCase()
    if (!term) return availableColumns
    return availableColumns.filter((col) => col.label.toLowerCase().includes(term))
  }, [availableColumns, columnSearch])

  // ── Render ───────────────────────────────────────────────────────────────

  if (loadingMeta) {
    return (
      <div className="reporting-page">
        <div className="reporting-header">
          <div className="reporting-title-area">
            <Skeleton variant="title" width={300} height={32} />
            <Skeleton variant="text" width={480} style={{ marginTop: 8 }} />
          </div>
        </div>
        <div className="reporting-section-card">
          <Skeleton variant="text" width={220} />
          <div style={{ marginTop: 16 }}>
            <Skeleton variant="table" />
          </div>
        </div>
      </div>
    )
  }

  const columnsMenu = (
    trigger: (props: MenuTriggerProps) => React.ReactNode,
    open: boolean,
    onOpenChange: (open: boolean) => void
  ) => (
    <Menu
      open={open}
      onOpenChange={onOpenChange}
      align="start"
      offset={6}
      // Picking several columns in a row is the whole point of the control, so a selection must
      // not dismiss it — the same reasoning as ColumnSelector and MultiSelectChips.
      closeOnSelect={false}
      className="report-columns-dropdown"
      ariaLabel="Report columns"
      trigger={trigger}
    >
      {/* Same search-then-scroll shape as every other multi-select on the page. Pinned above the
          list so it stays reachable however far down the columns you are. */}
      <div className="report-columns-search">
        <Search size={13} className="report-columns-search-icon" />
        <input
          type="text"
          className="report-columns-search-input"
          placeholder="Search columns..."
          value={columnSearch}
          aria-label="Search columns"
          onChange={(e) => setColumnSearch(e.target.value)}
          // Menu maps Home/End onto its item list; inside a text box those keys belong to the
          // caret. Arrow Up/Down still travel on, so the list is reachable from here.
          onKeyDown={(e) => {
            if (e.key === 'Home' || e.key === 'End') e.stopPropagation()
          }}
        />
        {columnSearch && (
          <button
            type="button"
            className="report-columns-search-clear"
            aria-label="Clear search"
            onClick={() => setColumnSearch('')}
          >
            <X size={12} />
          </button>
        )}
      </div>

      <div className="report-columns-list">
        {availableColumns.length === 0 ? (
          <div className="report-columns-empty">This report type has no columns to choose from.</div>
        ) : visibleColumns.length === 0 ? (
          <div className="report-columns-empty">No columns match &ldquo;{columnSearch.trim()}&rdquo;.</div>
        ) : (
          visibleColumns.map((col) => {
            const isChecked = selectedColumns.includes(col.key)
            return (
              <MenuItem
                key={col.key}
                className="report-columns-item"
                aria-checked={isChecked}
                onSelect={() => toggleColumn(col.key)}
              >
                <span className={`report-columns-check${isChecked ? ' is-checked' : ''}`}>
                  {isChecked && <Check size={11} strokeWidth={3} />}
                </span>
                <span className="report-columns-name">{col.label}</span>
                {col.derivedNote && (
                  <span className="report-columns-derived" title={col.derivedNote}>
                    <Info size={12} />
                  </span>
                )}
              </MenuItem>
            )
          })
        )}
      </div>
    </Menu>
  )

  return (
    <div className="reporting-page">
      {/* ── Page header ──────────────────────────────────────────────────── */}
      <div className="reporting-header">
        <div className="reporting-title-area">
          <h1>Reporting &amp; Analytics</h1>
          <p>
            Generate and download detailed reports for messaging, campaigns, templates,
            conversations and more.
          </p>
        </div>

        <div className="reporting-header-actions">
          {/* The same switcher, in the same place, as the Dashboard's. The date range it sets is
              the one every other control on this page reads, so nothing else had to change. */}
          <FilterBar
            options={['today', 'week', 'month', 'all']}
            activeOption={activeTimeFilter}
            onChange={handleTimeFilter}
          />

          <ExportMenu
            canExport={canExport}
            disabled={!hasRun}
            exportingFormat={exportingFormat}
            onExport={handleExport}
          />
        </div>
      </div>

      {/* ── Summary cards ────────────────────────────────────────────────────
          Built from whatever measures the server returns for the current filters. Nothing on
          these cards is computed from the rows on screen, which are only one page of the set. */}
      <ReportKpiCards summary={summary} isLoading={isSummaryLoading} />

      {/* ── Report + analytics ───────────────────────────────────────────────
          The builder and its results take the main column; the charts sit alongside on a wide
          screen and fall underneath on a narrow one. */}
      <div className="report-main-grid">
        <div className="report-main-col">

      {/* ── Build Your Report ────────────────────────────────────────────── */}
      <div className="reporting-section-card">
        <div className="reporting-section-header">
          <h2 className="reporting-section-title">Build Your Report</h2>
          <p className="reporting-section-subtitle">
            Select report type, configure filters, columns and grouping to generate your report.
          </p>
        </div>

        <div className="report-config-row">
          <div className="report-config-field">
            <label htmlFor="report-type">Report Type</label>
            <SearchableSelect
              id="report-type"
              label="Report Type"
              placeholder="Select a report type"
              hideAllOption
              value={filters.reportType ?? ''}
              options={(metadata?.reportTypes ?? []).map((type) => ({
                value: type.key,
                label: type.label
              }))}
              onChange={handleReportTypeChange}
            />
          </div>

          <div className="report-config-field">
            <label htmlFor="report-section">Data Section</label>
            <SearchableSelect
              id="report-section"
              label="Data Section"
              placeholder="Select a data section"
              hideAllOption
              value={filters.dataSection ?? ''}
              options={(reportType?.dataSections ?? []).map((section) => ({
                value: section.key,
                label: section.label
              }))}
              onChange={handleDataSectionChange}
            />
          </div>

          <div className="report-config-field">
            <label htmlFor="report-groupby">Group By</label>
            <SearchableSelect
              id="report-groupby"
              label="Group By"
              placeholder="None"
              hideAllOption
              value={filters.groupBy ?? 'none'}
              options={groupByOptions.map((option) => ({
                value: option.key,
                label: option.label
              }))}
              onChange={handleGroupByChange}
            />
          </div>

          <ColumnsField
            count={selectedColumns.length}
            disabled={isGrouped}
            renderMenu={columnsMenu}
          />

          {/* The two actions on the configuration, stacked at the end of the field row: running
              the report is the primary one, saving the definition the secondary. Both sit beside
              the fields they act on rather than at the far end of the card. */}
          <div className="report-config-actions">
            <button
              type="button"
              className="report-run-btn"
              onClick={handleApplyFilters}
              disabled={isRunning}
            >
              {isRunning ? (
                <>
                  <span className="report-run-spinner" aria-hidden="true" />
                  <span>Running…</span>
                </>
              ) : (
                <>
                  <Play size={14} />
                  <span>Run Report</span>
                </>
              )}
            </button>

            {canManage && (
              <button
                type="button"
                className="report-toolbar-btn report-save-btn"
                onClick={() => {
                  setEditingReport(null)
                  setSaveModalOpen(true)
                }}
              >
                <Save size={14} />
                <span>Save Report</span>
              </button>
            )}
          </div>
        </div>

        {/* Grouped reports are aggregates, so the column selection has nothing to act on — said
            out loud rather than leaving a control that silently does nothing. */}
        {isGrouped && (
          <p className="report-grouped-note">
            Grouped by{' '}
            <strong>
              {groupByOptions.find((g) => g.key === filters.groupBy)?.label ?? filters.groupBy}
            </strong>
            . The results table shows one aggregated row per group; column selection applies to the
            ungrouped listing.
          </p>
        )}

        {/* ── Advanced filters ───────────────────────────────────────────────
            The date periods now live in the page header beside Export, so all that remains here
            is the disclosure for everything a date cannot express. */}
        <div className="report-quick-filters">
          <button
            type="button"
            className={`report-filters-head${showFilters ? ' is-open' : ''}`}
            onClick={() => setShowFilters((v) => !v)}
            aria-expanded={showFilters}
            aria-controls="report-advanced-filters"
          >
            <Filter size={14} />
            <span>Advanced Filters</span>
            {/* Shown while collapsed too: a hidden panel that is silently narrowing the results
                is worth saying out loud. */}
            {activeFilterCount > 0 && (
              <span className="activity-filter-count">{activeFilterCount}</span>
            )}
            <ChevronDown size={15} className="report-filters-chevron" />
          </button>
        </div>

        <div className="report-filters-panel">

          {/* Rendered outright rather than through AnimatePresence.
              A height:0 → auto transition here would not settle — it stayed pinned at 0px with the
              content measured at 243px behind it — and the exiting node was never unmounted, which
              left eleven invisible filters in the keyboard tab order. A disclosure that reliably
              shows and hides its contents is worth more than one that animates and does neither. */}
          {showFilters && (
              <div
                id="report-advanced-filters"
                className="report-filters-wrap"
              >
                <div className="report-filters-grid">
                  <div className="report-filter-field report-filter-daterange">
                    <label htmlFor="report-from">Date &amp; Time</label>
                    <div className="report-daterange-inputs">
                      <input
                        id="report-from"
                        type="date"
                        className="form-control"
                        value={filters.from ?? ''}
                        min={filterOptions?.earliestRecord?.slice(0, 10)}
                        max={filters.to ?? filterOptions?.latestRecord?.slice(0, 10)}
                        onChange={(e) =>
                          setFilters((f) => ({ ...f, from: e.target.value || null }))
                        }
                      />
                      <span className="report-daterange-sep">–</span>
                      <input
                        id="report-to"
                        type="date"
                        className="form-control"
                        value={filters.to ?? ''}
                        min={filters.from ?? filterOptions?.earliestRecord?.slice(0, 10)}
                        max={filterOptions?.latestRecord?.slice(0, 10)}
                        onChange={(e) => setFilters((f) => ({ ...f, to: e.target.value || null }))}
                      />
                    </div>
                  </div>

                  <MultiSelectChips
                    label="Campaign"
                    placeholder="All Campaigns"
                    options={(filterOptions?.campaigns ?? []).map((c) => ({
                      value: String(c.id),
                      label: c.name
                    }))}
                    selected={(filters.campaignIds ?? []).map(String)}
                    onChange={(vals) =>
                      setFilters((f) => ({ ...f, campaignIds: vals.map(Number) }))
                    }
                    emptyMessage="No campaigns yet."
                  />

                  <MultiSelectChips
                    label="Template"
                    placeholder="All Templates"
                    options={(filterOptions?.templates ?? []).map((t) => ({ value: t, label: t }))}
                    selected={filters.templateNames ?? []}
                    onChange={(vals) => setFilters((f) => ({ ...f, templateNames: vals }))}
                    emptyMessage="No templates used yet."
                  />

                  <MultiSelectChips
                    label="WABA / Connection"
                    placeholder="All Connections"
                    options={(filterOptions?.connections ?? []).map((c) => ({
                      value: String(c.id),
                      label: c.name
                    }))}
                    selected={(filters.connectionIds ?? []).map(String)}
                    onChange={(vals) =>
                      setFilters((f) => ({ ...f, connectionIds: vals.map(Number) }))
                    }
                    emptyMessage="No connections yet."
                  />

                  <MultiSelectChips
                    label="Message Type"
                    placeholder="All"
                    options={(filterOptions?.messageTypes ?? []).map((t) => ({
                      value: t,
                      label: t
                    }))}
                    selected={filters.messageTypes ?? []}
                    onChange={(vals) => setFilters((f) => ({ ...f, messageTypes: vals }))}
                  />

                  <MultiSelectChips
                    label="Direction"
                    placeholder="All"
                    options={(filterOptions?.directions ?? []).map((d) => ({ value: d, label: d }))}
                    selected={filters.directions ?? []}
                    onChange={(vals) => setFilters((f) => ({ ...f, directions: vals }))}
                  />

                  <MultiSelectChips
                    label="Status"
                    placeholder="All"
                    options={(filterOptions?.statuses ?? []).map((s) => ({ value: s, label: s }))}
                    selected={filters.statuses ?? []}
                    onChange={(vals) => setFilters((f) => ({ ...f, statuses: vals }))}
                  />

                  <MultiSelectChips
                    label="Sender / Contact"
                    placeholder="All"
                    options={(filterOptions?.contacts ?? []).map((c) => ({
                      value: String(c.id),
                      label: c.name
                    }))}
                    selected={(filters.contactIds ?? []).map(String)}
                    onChange={(vals) => setFilters((f) => ({ ...f, contactIds: vals.map(Number) }))}
                    emptyMessage="No contacts have been messaged yet."
                  />

                  <MultiSelectChips
                    label="Failure Reason"
                    placeholder="All"
                    options={(filterOptions?.failureReasons ?? []).map((r) => ({
                      value: r,
                      label: r
                    }))}
                    selected={filters.failureReasons ?? []}
                    onChange={(vals) => setFilters((f) => ({ ...f, failureReasons: vals }))}
                    emptyMessage="Nothing has failed yet."
                  />

                  <MultiSelectChips
                    label="Agent"
                    placeholder="All"
                    options={(filterOptions?.agents ?? []).map((a) => ({ value: a, label: a }))}
                    selected={filters.agents ?? []}
                    onChange={(vals) => setFilters((f) => ({ ...f, agents: vals }))}
                    emptyMessage="No contacts are assigned yet."
                  />

                  <div className="report-filter-field">
                    <label htmlFor="report-search">Search</label>
                    <input
                      id="report-search"
                      type="text"
                      className="form-control"
                      placeholder="Contact, phone or message text…"
                      value={filters.search ?? ''}
                      onChange={(e) =>
                        setFilters((f) => ({ ...f, search: e.target.value || null }))
                      }
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') handleApplyFilters()
                      }}
                    />
                  </div>
                </div>

                <div className="report-filters-actions">
                  <button type="button" className="report-clear-btn" onClick={handleClearFilters}>
                    <RotateCcw size={13} />
                    <span>Clear Filters</span>
                  </button>
                  <button
                    type="button"
                    className="report-apply-btn"
                    onClick={handleApplyFilters}
                    disabled={isRunning}
                  >
                    {isRunning ? 'Running…' : 'Apply Filters'}
                  </button>
                </div>
              </div>
          )}
        </div>
      </div>

      {/* ── Report Results ───────────────────────────────────────────────── */}
      <div className="reporting-section-card">
        <div className="report-results-head">
          <div className="reporting-section-header">
            <h2 className="reporting-section-title">Report Results</h2>
            <p className="reporting-section-subtitle">Showing data for {appliedRangeLabel}</p>
          </div>

          <div className="report-results-controls">
            <label className="report-pagesize">
              <span>Records Per Page</span>
              <select
                className="report-select report-select-sm"
                value={filters.pageSize}
                onChange={(e) => handlePageSize(Number(e.target.value))}
              >
                {PAGE_SIZE_OPTIONS.map((size) => (
                  <option key={size} value={size}>
                    {size}
                  </option>
                ))}
              </select>
            </label>

            {!wasGrouped && (
              <ResultsColumnsButton renderMenu={columnsMenu} />
            )}
          </div>
        </div>

        <div className="report-table-scroll">
          <table className="report-table">
            <thead>
              <tr>
                {resultColumns.map((col) => (
                  <th key={col.key}>{col.label}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {isRunning ? (
                <tr>
                  <td colSpan={Math.max(1, resultColumns.length)} className="report-table-empty">
                    Running report…
                  </td>
                </tr>
              ) : wasGrouped ? (
                groupRows.length === 0 ? (
                  <tr>
                    <td colSpan={Math.max(1, resultColumns.length)} className="report-table-empty">
                      No data matches these filters.
                    </td>
                  </tr>
                ) : (
                  groupRows.map((row, index) => (
                    <motion.tr
                      key={row.key}
                      initial={{ opacity: 0, y: 6 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{
                        delay: Math.min(index, 12) * 0.025,
                        duration: 0.18,
                        ease: 'easeOut'
                      }}
                    >
                      {resultColumns.map((col) => (
                        <td key={col.key}>{renderGroupCell(row, col.key)}</td>
                      ))}
                    </motion.tr>
                  ))
                )
              ) : rows.length === 0 ? (
                <tr>
                  <td colSpan={Math.max(1, resultColumns.length)} className="report-table-empty">
                    No records found. Try widening the date range or clearing a filter.
                  </td>
                </tr>
              ) : (
                rows.map((row, index) => (
                  <motion.tr
                    key={row.id}
                    initial={{ opacity: 0, y: 6 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{
                      delay: Math.min(index, 12) * 0.025,
                      duration: 0.18,
                      ease: 'easeOut'
                    }}
                  >
                    {resultColumns.map((col) => (
                      <td key={col.key}>{renderRowCell(row, col.key)}</td>
                    ))}
                  </motion.tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        <ReportPager
          page={appliedFilters.page}
          pageSize={appliedFilters.pageSize}
          totalCount={totalCount}
          totalPages={totalPages}
          onPage={handlePage}
        />
      </div>

        </div>

        <ReportAnalytics summary={summary} isLoading={isSummaryLoading} />
      </div>

      {/* ── Saved Reports ────────────────────────────────────────────────── */}
      <div className="reporting-section-card">
        <div className="reporting-section-header">
          <h2 className="reporting-section-title">Saved Reports</h2>
          <p className="reporting-section-subtitle">View, manage and run your saved reports.</p>
        </div>

        <div className="report-table-scroll">
          <table className="report-table">
            <thead>
              <tr>
                {/* Actions lead the row, as on every other list in the app — reaching a control
                    should not mean scrolling past six columns of description first. */}
                <th className="actions-col">Actions</th>
                <th>Report Name</th>
                <th>Report Type</th>
                <th>Data Section</th>
                <th>Group By</th>
                <th>Last Run</th>
                <th>Created By</th>
              </tr>
            </thead>
            <tbody>
              {loadingSaved ? (
                <tr>
                  <td colSpan={7} className="report-table-empty">
                    Loading…
                  </td>
                </tr>
              ) : pagedSavedReports.length === 0 ? (
                <tr>
                  <td colSpan={7} className="report-table-empty">
                    No saved reports yet — build one above and save it.
                  </td>
                </tr>
              ) : (
                pagedSavedReports.map((report) => (
                  <tr key={report.id}>
                    <td className="actions-col">
                      <div className="report-row-actions">
                        <button
                          type="button"
                          className="report-icon-btn"
                          onClick={() => runSavedReport(report)}
                          title="Run this report"
                          aria-label={`Run ${report.name}`}
                        >
                          <Play size={13} />
                        </button>
                        {report.isOwner && (
                          <button
                            type="button"
                            className="report-icon-btn"
                            onClick={() => editSavedReport(report)}
                            title="Edit"
                            aria-label={`Edit ${report.name}`}
                          >
                            <Pencil size={13} />
                          </button>
                        )}
                        <Menu
                          open={savedMenuId === report.id}
                          onOpenChange={(open) => setSavedMenuId(open ? report.id : null)}
                          align="start"
                          offset={4}
                          ariaLabel="Saved report actions"
                          trigger={(props) => (
                            <button
                              {...props}
                              type="button"
                              className="report-icon-btn"
                              aria-label={`More actions for ${report.name}`}
                            >
                              <MoreVertical size={14} />
                            </button>
                          )}
                        >
                          <MenuItem onSelect={() => runSavedReport(report)}>
                            <Play size={13} /> <span>Run</span>
                          </MenuItem>
                          {report.isOwner && (
                            <MenuItem onSelect={() => editSavedReport(report)}>
                              <Pencil size={13} /> <span>Edit</span>
                            </MenuItem>
                          )}
                          {report.isOwner && (
                            <MenuItem onSelect={() => handleDeleteSaved(report)}>
                              <Trash2 size={13} /> <span>Delete</span>
                            </MenuItem>
                          )}
                        </Menu>
                      </div>
                    </td>
                    <td>
                      <div className="report-name-cell">
                        <span className="report-name">{report.name}</span>
                        {report.description && (
                          <span className="report-name-desc">{report.description}</span>
                        )}
                      </div>
                    </td>
                    <td>{report.reportTypeLabel}</td>
                    <td>{report.dataSectionLabel}</td>
                    <td>{report.groupByLabel}</td>
                    <td>{report.lastRunAt ? formatAbsoluteDateTime(report.lastRunAt) : 'Never run'}</td>
                    <td>{report.ownerName || '—'}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        <ReportPager
          page={savedPage}
          pageSize={SAVED_PAGE_SIZE}
          totalCount={savedReports.length}
          totalPages={savedTotalPages}
          onPage={setSavedPage}
        />
      </div>

      {saveModalOpen && (
        <SaveReportModal
          isOpen={saveModalOpen}
          existing={editingReport}
          columns={selectedColumns}
          filters={filters}
          onClose={() => {
            setSaveModalOpen(false)
            setEditingReport(null)
          }}
          onSaved={() => {
            setSaveModalOpen(false)
            setEditingReport(null)
            loadSavedReports()
          }}
        />
      )}
    </div>
  )
}

// ── Header export menu ──────────────────────────────────────────────────────

interface ExportMenuProps {
  canExport: boolean
  disabled: boolean
  exportingFormat: ReportExportFormat | null
  onExport: (format: ReportExportFormat) => void
}

/**
 * The header's Export control.
 *
 * The whole button opens the menu rather than the left half exporting in some remembered format:
 * a control that writes a file should say which file it is about to write before it writes it.
 */
const ExportMenu: React.FC<ExportMenuProps> = ({
  canExport,
  disabled,
  exportingFormat,
  onExport
}) => {
  const [isOpen, setIsOpen] = useState(false)

  return (
    <Menu
      open={isOpen}
      onOpenChange={setIsOpen}
      align="end"
      offset={6}
      className="report-export-dropdown"
      ariaLabel="Export format"
      trigger={(props) => (
        <button
          {...props}
          type="button"
          className="report-export-btn"
          disabled={!canExport || disabled || exportingFormat !== null}
          title={!canExport ? "You don't have permission to export reports." : undefined}
        >
          <Download size={14} />
          <span>
            {exportingFormat ? `Exporting ${exportingFormat.toUpperCase()}…` : 'Export'}
          </span>
          <span className="report-export-divider" aria-hidden="true" />
          <ChevronDown size={14} />
        </button>
      )}
    >
      <MenuItem onSelect={() => onExport('pdf')}>
        <FileText size={14} className="report-export-icon is-pdf" />
        <span>Export as PDF</span>
      </MenuItem>
      <MenuItem onSelect={() => onExport('xlsx')}>
        <FileSpreadsheet size={14} className="report-export-icon is-excel" />
        <span>Export as Excel</span>
      </MenuItem>
      <MenuItem onSelect={() => onExport('csv')}>
        <FileSpreadsheet size={14} className="report-export-icon is-csv" />
        <span>Export as CSV</span>
      </MenuItem>
    </Menu>
  )
}

// ── Column pickers ──────────────────────────────────────────────────────────

type ColumnsMenuRenderer = (
  trigger: (props: MenuTriggerProps) => React.ReactNode,
  open: boolean,
  onOpenChange: (open: boolean) => void
) => React.ReactNode

/** The "N Selected" field in the configuration row. */
const ColumnsField: React.FC<{
  count: number
  disabled: boolean
  renderMenu: ColumnsMenuRenderer
}> = ({ count, disabled, renderMenu }) => {
  const [isOpen, setIsOpen] = useState(false)

  return (
    <div className="report-config-field">
      <label id="report-columns-label">Columns</label>
      {renderMenu(
        (props) => (
          <button
            {...props}
            type="button"
            className={`report-select report-select-trigger${isOpen ? ' is-open' : ''}`}
            disabled={disabled}
            aria-labelledby="report-columns-label"
          >
            <span>{count} Selected</span>
            <ChevronDown size={14} />
          </button>
        ),
        isOpen && !disabled,
        setIsOpen
      )}
    </div>
  )
}

/** The "+ Add Column" button beside the selected-column chips. */

/** The "Columns" button in the results header. */
const ResultsColumnsButton: React.FC<{ renderMenu: ColumnsMenuRenderer }> = ({ renderMenu }) => {
  const [isOpen, setIsOpen] = useState(false)

  return renderMenu(
    (props) => (
      <button {...props} type="button" className={`report-toolbar-btn${isOpen ? ' is-active' : ''}`}>
        <Columns3 size={14} />
        <span>Columns</span>
      </button>
    ),
    isOpen,
    setIsOpen
  )
}

// ── Pager ───────────────────────────────────────────────────────────────────

interface ReportPagerProps {
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  onPage: (page: number) => void
}

/**
 * The table footer: an entry count on the left, first/previous/page/next/last on the right.
 *
 * A local pager rather than the shared `Pagination`: this page needs the numbered page button and
 * the jump-to-end controls the reference design calls for, and widening the shared component to
 * cover both shapes would change every other list that uses it.
 */
const ReportPager: React.FC<ReportPagerProps> = ({
  page,
  pageSize,
  totalCount,
  totalPages,
  onPage
}) => {
  if (totalCount === 0) return null

  const firstRow = (page - 1) * pageSize + 1
  const lastRow = Math.min(page * pageSize, totalCount)
  const pages = Math.max(totalPages, 1)

  return (
    <div className="report-pager">
      <span className="report-pager-summary">
        Showing {firstRow.toLocaleString()} to {lastRow.toLocaleString()} of{' '}
        {totalCount.toLocaleString()} entries
      </span>

      <div className="report-pager-controls">
        <button
          type="button"
          className="report-pager-btn"
          onClick={() => onPage(1)}
          disabled={page <= 1}
          aria-label="First page"
        >
          <ChevronsLeft size={14} />
        </button>
        <button
          type="button"
          className="report-pager-btn"
          onClick={() => onPage(page - 1)}
          disabled={page <= 1}
          aria-label="Previous page"
        >
          <ChevronLeft size={14} />
        </button>
        <span className="report-pager-current">{page}</span>
        <button
          type="button"
          className="report-pager-btn"
          onClick={() => onPage(page + 1)}
          disabled={page >= pages}
          aria-label="Next page"
        >
          <ChevronRight size={14} />
        </button>
        <button
          type="button"
          className="report-pager-btn"
          onClick={() => onPage(pages)}
          disabled={page >= pages}
          aria-label="Last page"
        >
          <ChevronsRight size={14} />
        </button>
      </div>
    </div>
  )
}

// ── Save dialog ─────────────────────────────────────────────────────────────

interface SaveReportModalProps {
  isOpen: boolean
  /** The report being edited, or null when saving a new one. */
  existing: SavedReport | null
  columns: string[]
  filters: ReportFilters
  onClose: () => void
  onSaved: () => void
}

const SaveReportModal: React.FC<SaveReportModalProps> = ({
  isOpen,
  existing,
  columns,
  filters,
  onClose,
  onSaved
}) => {
  const [name, setName] = useState(existing?.name ?? '')
  const [description, setDescription] = useState(existing?.description ?? '')
  const [isShared, setIsShared] = useState(existing?.isShared ?? false)
  const [isSaving, setIsSaving] = useState(false)

  const handleSave = async () => {
    const trimmed = name.trim()
    if (!trimmed) {
      toast.error('Give the report a name.')
      return
    }

    setIsSaving(true)
    try {
      const payload = {
        name: trimmed,
        description: description.trim() || null,
        columns,
        filters,
        isShared
      }

      if (existing) {
        await reportingService.updateSavedReport(existing.id, payload)
        toast.success(`"${trimmed}" updated.`)
      } else {
        await reportingService.createSavedReport(payload)
        toast.success(`"${trimmed}" saved.`)
      }

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
      title={existing ? 'Edit Report' : 'Save Report'}
      subtitle={
        existing
          ? 'Updates the saved report to the configuration currently on screen.'
          : "Keep this report type, column and filter combination so you don't have to rebuild it."
      }
      size="sm"
      footer={
        <>
          <button type="button" className="btn-secondary" onClick={onClose} disabled={isSaving}>
            Cancel
          </button>
          <button type="button" className="btn-primary" onClick={handleSave} disabled={isSaving}>
            {isSaving ? 'Saving…' : existing ? 'Update Report' : 'Save Report'}
          </button>
        </>
      }
    >
      <div className="report-save-form">
        <div className="report-filter-field">
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
        <div className="report-filter-field">
          <label htmlFor="save-report-description">Description (optional)</label>
          <textarea
            id="save-report-description"
            className="form-control"
            rows={2}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </div>
        <div className="report-filter-field report-save-shared">
          <label htmlFor="save-report-shared">
            <input
              id="save-report-shared"
              type="checkbox"
              checked={isShared}
              onChange={(e) => setIsShared(e.target.checked)}
            />
            <span>Share with everyone who can view reports</span>
          </label>
          {/* Private by default on purpose: a report's filters can encode something the author
              would not choose to publish — one agent's failures, one campaign's breakdown — so
              visibility opens on request, not by omission. */}
          <p className="report-empty-note">Private reports are visible only to you.</p>
        </div>
      </div>
    </Modal>
  )
}

export default ReportBuilder
