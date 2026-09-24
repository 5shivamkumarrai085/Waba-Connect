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

/**
 * How long a CSV request is allowed to take.
 *
 * apiClient's 60-second default is right for the ordinary request and wrong for these two: a large
 * import spends real time uploading and then real time writing thousands of contacts, and the
 * default was cutting it off mid-write. The work carried on server-side while the user was told
 * the upload had failed — which is worse than a slow import, because it looks like nothing
 * happened when something did.
 */
const CSV_REQUEST_TIMEOUT_MS = 10 * 60_000

/** Turns an axios failure into something worth showing a user. */
const describeCsvError = (err: any, fallback: string): string => {
  if (err?.code === 'ECONNABORTED') {
    return 'The file is taking longer than expected. It may still be importing — check the Campaigns list in a few minutes before trying again.'
  }

  if (err?.response?.status === 413) {
    return 'That file is too large to upload. Split it into smaller files and import them one at a time.'
  }

  if (err?.response?.status === 401 || err?.response?.status === 403) {
    return 'You do not have permission to import campaigns. Ask an administrator to grant Bulk Campaign access.'
  }

  if (!err?.response) {
    return 'Could not reach the server. Check your connection and try again.'
  }

  return err.response?.data?.message || fallback
}

export const campaignUploadService = {
  /**
   * Validates an uploaded CSV and returns the preview the operator confirms.
   *
   * @param channel 'WhatsApp' (the default) or 'Email'. The server applies the channel's column
   * rules — an email campaign's file needs a usable email address in every row — so the preview
   * counts exactly what the campaign will end up sending to. Omitting it validates as WhatsApp,
   * which is what every call written before the email channel meant.
   */
  validateCsv: async (
    file: File,
    channel?: string
  ): Promise<{ success: boolean; data?: CsvValidationData; message: string }> => {
    const formData = new FormData()
    formData.append('file', file)
    if (channel) formData.append('channel', channel)

    try {
      const res = await apiClient.post('/Campaigns/csv-validate', formData, {
        headers: {
          // Explicitly undefined, not omitted. apiClient sets a default Content-Type of
          // application/json on every request, so leaving this out sends the file as JSON and the
          // server answers 415. Naming the type by hand does not work either -- a multipart body
          // is unparseable without the `boundary` parameter, and that value is generated per
          // request by the browser. Deleting the header is the only way to let it supply
          // `multipart/form-data; boundary=...` itself.
          'Content-Type': undefined
        },
        timeout: CSV_REQUEST_TIMEOUT_MS
      })
      return {
        success: res.data?.success ?? true,
        data: res.data?.data,
        message: res.data?.message || 'CSV validated successfully.'
      }
    } catch (err: any) {
      return {
        success: false,
        message: describeCsvError(err, 'The file could not be read as CSV. Download the sample file to see the expected format.')
      }
    }
  },

  /**
   * Downloads the sample CSV.
   *
   * Fetched through apiClient rather than opened with `window.open`. The endpoint requires a
   * permission, and this app authenticates with a bearer token held in localStorage -- a new tab
   * carries no such header, so the browser was navigating to a 401 and the user saw a blank tab or
   * an error page instead of a file. Pulling the bytes here means the request is authenticated
   * like every other, and the download is handed to the browser from memory.
   */
  downloadCsvSample: async (): Promise<void> => {
    const res = await apiClient.get('/Campaigns/csv-sample', { responseType: 'blob' })

    const url = URL.createObjectURL(new Blob([res.data], { type: 'text/csv;charset=utf-8' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'bulk_campaign_sample.csv'
    document.body.appendChild(link)
    link.click()
    link.remove()
    // Released on the next tick: revoking synchronously can cancel the download in some browsers.
    setTimeout(() => URL.revokeObjectURL(url), 0)
  },

  createCsvCampaign: async (payload: {
    name: string
    csvFileUrl: string
    /** 'WhatsApp' (the default when absent) or 'Email'. */
    channel?: string
    /** The WhatsApp template. Ignored on the email channel. */
    templateId: number
    /** The Setup-section email template. Required on the email channel. */
    emailTemplateId?: number | null
    /** The verified sender to send from. Required on the email channel. */
    senderIdentityId?: number | null
    replyToOverride?: string | null
    subjectOverride?: string | null
    relationType: string
    scheduleType: string
    scheduledAt: string | null
    variables: any[]
    connectionId?: number
  }): Promise<{ success: boolean; message: string; data?: any }> => {
    try {
      const res = await apiClient.post('/Campaigns/csv-create', payload, {
        timeout: CSV_REQUEST_TIMEOUT_MS
      })
      return {
        success: res.data?.success ?? true,
        message: res.data?.message || 'Bulk CSV Campaign created successfully!',
        data: res.data?.data
      }
    } catch (err: any) {
      return {
        success: false,
        message: describeCsvError(err, 'Failed to create bulk campaign.')
      }
    }
  }
}

export default campaignUploadService
