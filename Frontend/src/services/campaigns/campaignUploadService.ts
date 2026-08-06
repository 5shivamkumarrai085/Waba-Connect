// src/services/campaigns/campaignUploadService.ts
import { apiClient } from '../apiClient'

export interface CsvRowError {
  rowNumber: number
  column?: string | null
  value: string
  reason: string
}

export interface CsvValidationData {
  fileUrl: string
  fileName: string
  totalRecords: number
  validCount: number
  invalidCount: number
  errors: CsvRowError[]
}

export const campaignUploadService = {
  validateCsv: async (file: File): Promise<{ success: boolean; data?: CsvValidationData; message: string }> => {
    const formData = new FormData()
    formData.append('file', file)

    try {
      const res = await apiClient.post('/Campaigns/csv-validate', formData, {
        headers: {
          'Content-Type': 'multipart/form-data'
        }
      })
      return {
        success: res.data?.success ?? true,
        data: res.data?.data,
        message: res.data?.message || 'CSV validated successfully.'
      }
    } catch (err: any) {
      const msg = err.response?.data?.message || 'cannot upload wrong format csv file'
      return {
        success: false,
        message: msg
      }
    }
  },

  createCsvCampaign: async (payload: {
    name: string
    csvFileUrl: string
    templateId: number
    relationType: string
    scheduleType: string
    scheduledAt: string | null
    variables: any[]
  }): Promise<{ success: boolean; message: string; data?: any }> => {
    try {
      const res = await apiClient.post('/Campaigns/csv-create', payload)
      return {
        success: res.data?.success ?? true,
        message: res.data?.message || 'Bulk CSV Campaign created successfully!',
        data: res.data?.data
      }
    } catch (err: any) {
      const msg = err.response?.data?.message || 'Failed to create bulk campaign.'
      return {
        success: false,
        message: msg
      }
    }
  }
}

export default campaignUploadService
