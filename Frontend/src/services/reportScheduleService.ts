import { apiClient } from './apiClient'

/** One of the server's catalogue values (GET api/reference/report-schedule-options). */
export type ScheduleFrequency = string
export type ScheduleFormat = string

export interface ReportSchedule {
  id: number
  reportDefinitionId: number
  reportName: string
  frequency: ScheduleFrequency
  timeOfDay: string
  dayOfWeek?: number | null
  dayOfMonth?: number | null
  timeZone: string
  recipients: string[]
  format: ScheduleFormat
  lookbackDays: number
  senderIdentityId: number
  senderAddress?: string | null
  ownerUserId: number
  ownerName?: string | null
  isActive: boolean
  nextRunAt: string
  lastRunAt?: string | null
  lastStatus?: string | null
  lastError?: string | null
}

export interface SaveReportScheduleInput {
  reportDefinitionId: number
  frequency: ScheduleFrequency
  timeOfDay: string
  dayOfWeek?: number | null
  dayOfMonth?: number | null
  timeZone: string
  recipients: string[]
  format: ScheduleFormat
  lookbackDays: number
  senderIdentityId: number
  isActive: boolean
}

export interface ScheduleSender {
  id: number
  emailAddress: string
  displayName: string
}

export const reportScheduleService = {
  list: async (): Promise<ReportSchedule[]> => (await apiClient.get('/report-schedules')).data?.data ?? [],
  senders: async (): Promise<ScheduleSender[]> => (await apiClient.get('/report-schedules/senders')).data?.data ?? [],
  create: async (input: SaveReportScheduleInput): Promise<ReportSchedule> => (await apiClient.post('/report-schedules', input)).data?.data,
  update: async (id: number, input: SaveReportScheduleInput): Promise<ReportSchedule> => (await apiClient.put(`/report-schedules/${id}`, input)).data?.data,
  remove: async (id: number): Promise<void> => { await apiClient.delete(`/report-schedules/${id}`) },
  /** Sends it now. `success` is false when the build or the email failed; `message` says why. */
  runNow: async (id: number): Promise<{ success: boolean; message: string }> => {
    const response = await apiClient.post(`/report-schedules/${id}/run`)
    return { success: !!response.data?.success, message: response.data?.message ?? '' }
  }
}

export default reportScheduleService
