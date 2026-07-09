import { apiClient } from './apiClient'
import type { MetricCardModel, AccuracyRecord, FreshnessRecord, ExportItemModel } from '../types/reporting'

export const reportingService = {
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
