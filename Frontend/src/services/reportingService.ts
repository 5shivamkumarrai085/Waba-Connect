import { apiClient } from './apiClient'
import { downloadFromApi } from '../utils/downloadFile'
import type { MetricCardModel, AccuracyRecord, FreshnessRecord, ExportItemModel } from '../types/reporting'

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
  }
}
