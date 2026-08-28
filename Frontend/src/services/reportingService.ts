import { apiClient } from './apiClient'
import { downloadFromApi } from '../utils/downloadFile'
import type {
  MetricCardModel,
  AccuracyRecord,
  FreshnessRecord,
  ExportItemModel,
  ReportColumn,
  ReportFilterOptions,
  ReportFilters,
  ReportRow,
  ReportExportFormat,
  SavedReport,
  SaveReportRequest
} from '../types/reporting'

/** Same shape as the other paged list responses in this app (see AuditLogPage). */
export interface ReportRowPage {
  items: ReportRow[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export const reportingService = {
  /**
   * Downloads one of the report CSVs.
   *
   * A thin wrapper so the page keeps talking to its service rather than reaching for the HTTP
   * client directly. Rejects with a usable message; the caller decides how to surface it.
   */
  downloadExport: (
    endpoint: string,
    options: { params?: Record<string, string | undefined>; fallbackFilename: string }
  ): Promise<void> => downloadFromApi(endpoint, options),

  getReportingMetrics: async (timeFilter: string): Promise<MetricCardModel[]> => {
    try {
      const response = await apiClient.get(`/Reporting/metrics?filter=${timeFilter}`)
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getAccuracyRecords: async (): Promise<AccuracyRecord[]> => {
    try {
      const response = await apiClient.get('/Reporting/accuracy')
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getFreshnessRecords: async (): Promise<FreshnessRecord[]> => {
    try {
      const response = await apiClient.get('/Reporting/freshness')
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getExportItems: async (): Promise<ExportItemModel[]> => {
    try {
      const response = await apiClient.get('/Reporting/exports')
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  getCustomisationFeatures: async (): Promise<string[]> => {
    try {
      const response = await apiClient.get('/Reporting/customisation')
      return response.data?.data || []
    } catch (error) {
      return []
    }
  },

  // ── Report builder ─────────────────────────────────────────────────────

  getReportColumns: async (): Promise<ReportColumn[]> => {
    const response = await apiClient.get('/Reporting/report/columns')
    return response.data?.data || []
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
