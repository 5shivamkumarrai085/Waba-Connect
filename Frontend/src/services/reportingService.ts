import { apiClient } from './apiClient'
import { downloadFromApi } from '../utils/downloadFile'
import type {
  ReportFilterOptions,
  ReportGroupRow,
  ReportMetadata,
  ReportFilters,
  ReportRow,
  ReportExportFormat,
  SavedReport,
  SaveReportRequest,
  ReportSummary,
} from '../types/reporting'

/** Same shape as the other paged list responses in this app (see AuditLogPage). */
export interface ReportRowPage {
  items: ReportRow[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

/** The same envelope for aggregated rows. */
export interface ReportGroupPage {
  items: ReportGroupRow[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export const reportingService = {
  // ── Report builder ─────────────────────────────────────────────────────

  /**
   * The builder's report types, sections, groupings and column catalogue, in one request.
   *
   * One call rather than four because the four are useless apart — a report type cannot be
   * rendered without the sections it allows — and a page that opens with one request feels like
   * a page rather than a loading sequence.
   */
  getReportMetadata: async (): Promise<ReportMetadata> => {
    const response = await apiClient.get('/Reporting/report/metadata')
    return response.data?.data
  },

  getReportFilterOptions: async (): Promise<ReportFilterOptions> => {
    const response = await apiClient.get('/Reporting/report/filter-options')
    return response.data?.data
  },

  /**
   * POST, not GET: the filter set is a structured object with several array fields, and this
   * mirrors the backend's own reasoning (ReportingController.RunReport) — a query string that
   * long risks truncation by a proxy, and would put contact identifiers in server access logs.
   *
   * Not routed through the patched `apiClient.get` dedupe/cancel-by-path logic; each report run
   * is a deliberate action the caller triggers, not a background list refresh.
   */
  runReport: async (filters: ReportFilters): Promise<ReportRowPage> => {
    const response = await apiClient.post('/Reporting/report/query', filters)
    return response.data?.data
  },

  /**
   * The summary cards and charts for the same filters.
   *
   * Sent as its own request rather than folded into the row query: the table pages, and the
   * headline numbers must describe the whole filtered set rather than the 25 rows on screen.
   */
  getReportSummary: async (filters: ReportFilters): Promise<ReportSummary> => {
    const response = await apiClient.post('/Reporting/report/summary', filters)
    return response.data?.data
  },

  /**
   * The aggregated form of the same query. A separate call rather than a mode flag: the two
   * return genuinely different rows, and one response carrying both — half of it always empty —
   * would make every caller work out which half to read.
   */
  runGroupedReport: async (filters: ReportFilters): Promise<ReportGroupPage> => {
    const response = await apiClient.post('/Reporting/report/grouped', filters)
    return response.data?.data
  },

  exportReport: (
    filters: ReportFilters,
    columns: string[],
    format: ReportExportFormat
  ): Promise<void> =>
    downloadFromApi(`/Reporting/report/export?format=${format}`, {
      data: { filters, columns },
      fallbackFilename: `report.${format}`
    }),

  getSavedReports: async (): Promise<SavedReport[]> => {
    const response = await apiClient.get('/Reporting/report/saved')
    return response.data?.data || []
  },

  createSavedReport: async (request: SaveReportRequest): Promise<SavedReport> => {
    const response = await apiClient.post('/Reporting/report/saved', request)
    return response.data?.data
  },

  updateSavedReport: async (id: number, request: SaveReportRequest): Promise<SavedReport> => {
    const response = await apiClient.put(`/Reporting/report/saved/${id}`, request)
    return response.data?.data
  },

  deleteSavedReport: async (id: number): Promise<void> => {
    await apiClient.delete(`/Reporting/report/saved/${id}`)
  },

  /** Stamps LastRunAt on a saved report. Fire-and-forget from the caller's point of view. */
  touchSavedReport: async (id: number): Promise<void> => {
    await apiClient.post(`/Reporting/report/saved/${id}/run`)
  }
}
