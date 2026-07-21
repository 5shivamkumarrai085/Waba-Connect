import { apiClient } from '../apiClient'
import type {
  Contact,
  ContactGroup,
  ContactSource,
  ContactStatus,
  ContactLanguage,
  AssignedUser,
  ContactType,
  ContactFormModel
} from '../../types/contacts'

export const contactService = {
  getContacts: async (): Promise<Contact[]> => {
    try {
      // Use large page size to fetch all contacts
      const response = await apiClient.get('/Contacts', {
        params: { pageSize: 10000 }
      })
      const items = response.data?.data?.items || []
      return items.map(mapContact)
    } catch (error) {
      return []
    }
  },

  getContactGroups: async (): Promise<ContactGroup[]> => {
    try {
      const response = await apiClient.get('/ContactGroups', {
        params: { pageSize: 10000 }
      })
      if (response.data?.data?.items) {
        return response.data.data.items
      }
      return []
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
      type: form.type || 'Lead',
      status: form.status || 'New',
      source: form.source || 'whatsapp',
      assignedTo: form.assigned || null,
      email: form.email || null,
      website: form.website || null,
      language: form.language || null,
      city: form.city || null,
      state: form.state || null,
      country: form.country || null,
      zipCode: form.zipCode || null,
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
      type: form.type || 'Lead',
      status: form.status || 'New',
      source: form.source || 'whatsapp',
      assignedTo: form.assigned || null,
      email: form.email || null,
      website: form.website || null,
      language: form.language || null,
      city: form.city || null,
      state: form.state || null,
      country: form.country || null,
      zipCode: form.zipCode || null,
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

  importContacts: async (file: File): Promise<{ success: boolean; message: string }> => {
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
        message: response.data?.message || 'Contacts imported successfully.'
      }
    } catch (err: any) {
      const msg = err.response?.data?.message || 'wrong format csv file'
      return {
        success: false,
        message: msg
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
  }
}

const mapContact = (c: any): Contact => {
  if (!c) return c
  return {
    ...c,
    active: c.isActive ?? c.active ?? true
  }
}
