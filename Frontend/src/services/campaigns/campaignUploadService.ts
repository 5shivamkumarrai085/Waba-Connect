// src/services/campaigns/campaignUploadService.ts
export const campaignUploadService = {
  uploadCsv: (campaignName: string, file: File): Promise<{ success: boolean; message: string }> => {
    console.log('[API Calling] POST /api/campaigns/csv-upload', { campaignName, fileName: file.name })
    return new Promise((resolve) => {
      setTimeout(() => {
        resolve({
          success: true,
          message: 'Bulk CSV Campaign created and scheduled successfully!'
        })
      }, 300) // simulation delay
    })
  },

  validateCsv: (file: File): Promise<{ valid: boolean; errors: string[] }> => {
    console.log('[API Calling] POST /api/campaigns/csv-validate', { fileName: file.name })
    return new Promise((resolve) => {
      setTimeout(() => {
        if (!file.name.endsWith('.csv')) {
          resolve({
            valid: false,
            errors: ['Invalid file format. Only CSV files are allowed.']
          })
        } else {
          resolve({
            valid: true,
            errors: []
          })
        }
      }, 100)
    })
  }
}
export default campaignUploadService
