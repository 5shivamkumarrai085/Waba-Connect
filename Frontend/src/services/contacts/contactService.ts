import { fetchAllPages, readPaged, type PagedResult } from '../pagination'
import { apiClient } from '../apiClient'
import { getErrorMessage } from '../../utils/errorHelper'
import type { CsvRowErrorItem } from '../../components/CsvRowErrors/CsvRowErrors'
import type {
  Contact,
  ContactGroup,
  ContactSource,
  ContactStatus,
  ContactLanguage,
  AssignedUser,
  ContactType,
  ContactFormModel,
  ContactDetails
} from '../../types/contacts'

/** Mirrors the backend's CsvImportResponse (Models/DTOs/Common/CsvDtos.cs). */
export interface ContactImportSummary {
  totalRecords: number
  importedCount: number
  skippedDuplicates: number
  invalidCount: number
  errors: CsvRowErrorItem[]
}

export interface CsvContactColumn {
  header: string
  label: string
  required: boolean
  example: string
}

export interface CsvContactLayout {
  columns: CsvContactColumn[]
  optionalColumns: string[]
}

export interface ContactImportResult {
  /** The file was processed. Rows may still have been rejected — check `result`. */
  success: boolean
  message: string
  /** Null when the upload failed outright (bad file, missing columns, server error). */
  result: ContactImportSummary | null
}

/** Server-side contact list query (filters, sort and page all run in the database). */
export interface ContactListQuery {
  page: number
  pageSize: number
  search?: string
  type?: string
  status?: string
  source?: string
  assignedTo?: string
  group?: string
  tag?: string
  startDate?: string
  endDate?: string
  sortBy?: string
  sortDescending?: boolean
}

const toContactParams = (q: Partial<ContactListQuery>) => {
  const pick = (v?: string) => (v && v !== 'All' ? v : undefined)
  return {
    page: q.page,
    pageSize: q.pageSize,
    search: q.search?.trim() || undefined,
    type: pick(q.type),
    status: pick(q.status),
    source: pick(q.source),
    assignedTo: pick(q.assignedTo),
    group: pick(q.group),
    tag: pick(q.tag),
    startDate: q.startDate || undefined,
    endDate: q.endDate || undefined,
    sortBy: q.sortBy || undefined,
    sortDescending: q.sortDescending ?? true,
  }
}

export const contactService = {
  getContactsPage: async (query: ContactListQuery): Promise<PagedResult<Contact>> => {
    const response = await apiClient.get('/Contacts', { params: toContactParams(query) })
    return readPaged(response.data, mapContact)
  },

  /** Active contacts per type, in one grouped query (the campaign wizard's audience summary). */
  getTypeCounts: async (): Promise<Record<string, number>> => {
    const response = await apiClient.get('/Contacts/type-counts')
    const rows = (response.data?.data ?? []) as { type: string; count: number }[]
    return Object.fromEntries(rows.map(r => [r.type, r.count]))
  },

  /** The columns a contacts CSV needs right now (required ones follow Settings › Contacts). */
  getCsvLayout: async (): Promise<CsvContactLayout> => {
    const response = await apiClient.get('/Contacts/csv-layout')
    return response.data?.data as CsvContactLayout
  },

  /** Every contact matching the filters, for exports; bounded. */
  getAllMatching: async (query: Partial<ContactListQuery>, maxItems = 50_000): Promise<Contact[]> => {
    const { page: _page, pageSize: _size, ...filters } = toContactParams(query)
    return fetchAllPages('/Contacts', filters, mapContact, maxItems)
  },

  getContacts: async (): Promise<Contact[]> => {
    try {
      // For pickers that need the whole list. Bounded: the Contacts page itself pages on the
      // server, and "select all" in the campaign wizard is resolved server-side.
      return await fetchAllPages('/Contacts', {}, mapContact, 10_000)
    } catch (error) {
      return []
    }
  },

  getContactGroups: async (): Promise<ContactGroup[]> => {
    try {
      return await fetchAllPages<ContactGroup>('/ContactGroups')
    } catch (error) {
      return []
    }
  },

  getContactSources: async (): Promise<ContactSource[]> => {
    try {
      const response = await apiClient.get('/Contacts/sources')
      return response.data || []
    } catch (error) {
      return []
    }
  },

  getContactStatuses: async (): Promise<ContactStatus[]> => {
    try {
      const response = await apiClient.get('/Contacts/statuses')
      return response.data || []
    } catch (error) {
      return []
    }
  },

  getContactLanguages: async (): Promise<ContactLanguage[]> => {
    try {
      const response = await apiClient.get('/Contacts/languages')
      return response.data || []
    } catch (error) {
      return []
    }
  },

  getAssignedUsers: async (): Promise<AssignedUser[]> => {
    try {
      const response = await apiClient.get('/Contacts/assigned-users')
      return response.data || []
    } catch (error) {
      return []
    }
  },

  getContactTypes: async (): Promise<ContactType[]> => {
    try {
      const response = await apiClient.get('/Contacts/types')
      return response.data || []
    } catch (error) {
      return []
    }
  },

  getContactCountries: async (): Promise<any[]> => {
    try {
      const response = await apiClient.get('/Contacts/countries')
      return response.data || []
    } catch (error) {
      return []
    }
  },

  /** The contact as the server describes it, for read-only views. Throws on failure. */
  getContactDetails: async (id: number): Promise<ContactDetails> => {
    const response = await apiClient.get(`/Contacts/${id}`)
    return response.data?.data as ContactDetails
  },

  getContactById: async (id: number): Promise<Contact> => {
    const response = await apiClient.get(`/Contacts/${id}`)
    return response.data?.data
  },

  addContact: async (form: ContactFormModel): Promise<Contact> => {
    let formattedPhone = form.phone.replace(/[^0-9+]/g, '')
    if (!formattedPhone.startsWith('+')) {
      formattedPhone = `+${formattedPhone}`
    }

    const payload = {
      name: `${form.firstName} ${form.lastName}`.trim(),
      phone: formattedPhone,
      type: form.type,
      status: form.status,
      source: form.source,
      assignedTo: form.assigned || null,
      company: form.company || null,
      email: form.email || null,
      website: form.website || null,
      language: form.language || null,
      city: form.city || null,
      state: form.state || null,
      country: form.country || null,
      timeZone: form.timeZone || null,
      zipCode: form.zipCode || null,
      dateOfBirth: form.dateOfBirth || null,
      address: form.address || null,
      description: form.description || null,
      groupIds: form.groups ? [parseInt(form.groups, 10)].filter(id => !isNaN(id)) : []
    }

    const response = await apiClient.post('/Contacts', payload)
    return mapContact(response.data?.data)
  },

  updateContact: async (id: number, form: ContactFormModel): Promise<Contact> => {
    let formattedPhone = form.phone.replace(/[^0-9+]/g, '')
    if (!formattedPhone.startsWith('+')) {
      formattedPhone = `+${formattedPhone}`
    }

    const payload = {
      name: `${form.firstName} ${form.lastName}`.trim(),
      phone: formattedPhone,
      type: form.type,
      status: form.status,
      source: form.source,
      assignedTo: form.assigned || null,
      company: form.company || null,
      email: form.email || null,
      website: form.website || null,
      language: form.language || null,
      city: form.city || null,
      state: form.state || null,
      country: form.country || null,
      timeZone: form.timeZone || null,
      zipCode: form.zipCode || null,
      dateOfBirth: form.dateOfBirth || null,
      address: form.address || null,
      description: form.description || null,
      groupIds: form.groups ? [parseInt(form.groups, 10)].filter(id => !isNaN(id)) : []
    }

    const response = await apiClient.put(`/Contacts/${id}`, payload)
    return mapContact(response.data?.data)
  },

  deleteContact: async (id: number): Promise<void> => {
    await apiClient.delete(`/Contacts/${id}`)
  },

  bulkDeleteContacts: async (ids: number[]): Promise<void> => {
    await apiClient.post('/Contacts/bulk-delete', { ids })
  },

  toggleActive: async (id: number): Promise<Contact> => {
    const response = await apiClient.patch(`/Contacts/${id}/toggle-active`)
    return mapContact(response.data?.data)
  },

  /**
   * Uploads a contacts CSV.
   *
   * The import is partial: `success` means the file was processed, not that every row was
   * usable. `result` carries the counts and the per-row errors so the page can show which rows
   * failed and why — this used to return only a message, which is why a rejected file gave the
   * user nothing to act on.
   */
  importContacts: async (file: File): Promise<ContactImportResult> => {
    const formData = new FormData()
    formData.append('file', file)
    try {
      const response = await apiClient.post('/Contacts/csv-import', formData, {
        headers: {
          'Content-Type': 'multipart/form-data'
        }
      })
      return {
        success: response.data?.success ?? true,
        message: response.data?.message || 'Contacts imported successfully.',
        result: response.data?.data ?? null
      }
    } catch (err: any) {
      // No invented fallback text. The old default here was the literal string
      // "wrong format csv file", which fabricated a diagnosis the server never gave — a network
      // failure and a genuinely malformed file read identically.
      return {
        success: false,
        message: getErrorMessage(err, 'The contacts could not be imported.'),
        result: err.response?.data?.data ?? null
      }
    }
  },

  getSettings: async (): Promise<{ groupNotAssignedText: string }> => {
    try {
      const response = await apiClient.get('/Contacts/settings')
      return response.data?.data || { groupNotAssignedText: 'Group not assigned' }
    } catch {
      return { groupNotAssignedText: 'Group not assigned' }
    }
  },

  getContactNotes: async (contactId: number): Promise<any[]> => {
    const response = await apiClient.get(`/Contacts/${contactId}/notes`)
    return response.data?.data || []
  },

  addContactNote: async (contactId: number, content: string): Promise<any> => {
    const response = await apiClient.post(`/Contacts/${contactId}/notes`, { content })
    return response.data?.data
  },

  deleteContactNote: async (contactId: number, noteId: number): Promise<void> => {
    await apiClient.delete(`/Contacts/${contactId}/notes/${noteId}`)
  }
}

const mapContact = (c: any): Contact => {
  if (!c) return c
  return {
    ...c,
    active: c.isActive ?? c.active ?? true
  }
}
