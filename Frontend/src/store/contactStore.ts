// src/store/contactStore.ts
import { create } from 'zustand'
import { contactService } from '../services/contacts/contactService'
import type { Contact, ContactFormModel } from '../types/contacts'
import { getErrorMessage } from '../utils/errorHelper'
import toast from 'react-hot-toast'
import { useDashboardStore } from './dashboardStore'
import { useChatStore } from './chatStore'
import { isRequestCancelled } from '../services/apiClient'

/**
 * Cross-module edges for contact writes, declared here rather than through a global event bus so
 * that every consequence of a contact change is greppable from the action that causes it.
 *
 * The chat inbox is only refreshed when it already holds conversations — that means the user has
 * been to Chat this session and may be looking at a now-stale sidebar. Refreshing it
 * unconditionally would issue a request on behalf of a screen nobody has opened.
 */
const propagateContactChange = () => {
  useDashboardStore.getState().loadDashboardData(false)
  if (useChatStore.getState().conversations.length > 0) {
    void useChatStore.getState().loadConversations()
  }
}

interface ContactStoreState {
  contacts: Contact[]
  isLoading: boolean
  searchQuery: string
  selectedIds: number[]
  currentPage: number
  pageSize: number
  sortColumn: string
  sortOrder: 'asc' | 'desc'
  visibleColumns: Record<string, boolean>
  groupNotAssignedText: string

  setSearchQuery: (query: string) => void
  setSelectedIds: (ids: number[]) => void
  toggleRowSelection: (id: number) => void
  toggleAllRowSelection: () => void
  toggleColumnVisibility: (col: string) => void
  setCurrentPage: (page: number) => void
  setPageSize: (size: number) => void
  setSort: (column: string, order: 'asc' | 'desc') => void

  loadContacts: () => Promise<void>
  addContact: (form: ContactFormModel) => Promise<Contact>
  deleteSelected: () => Promise<void>
  toggleContactActive: (id: number) => Promise<void>
  importContacts: (file: File) => Promise<{ success: boolean; message: string }>
}

export const useContactStore = create<ContactStoreState>((set, get) => ({
  contacts: [],
  isLoading: false,
  searchQuery: '',
  selectedIds: [],
  currentPage: 1,
  pageSize: 10,
  sortColumn: 'id',
  sortOrder: 'desc',
  groupNotAssignedText: 'Group not assigned',
  visibleColumns: {
    id: true,
    name: true,
    type: true,
    phone: true,
    assigned: true,
    initiateChat: true,
    status: true,
    source: true,
    group: true,
    active: true,
    createdAt: true
  },

  setSearchQuery: (searchQuery) => set({ searchQuery, currentPage: 1 }),
  setSelectedIds: (selectedIds) => set({ selectedIds }),

  toggleRowSelection: (id) => {
    const { selectedIds } = get()
    if (selectedIds.includes(id)) {
      set({ selectedIds: selectedIds.filter(x => x !== id) })
    } else {
      set({ selectedIds: [...selectedIds, id] })
    }
  },

  toggleAllRowSelection: () => {
    const { selectedIds, searchQuery } = get()

    // Filter contacts based on search query first
    const filtered = get().contacts.filter(c => {
      const q = searchQuery.toLowerCase()
      const cName = c.name || `${c.firstName || ''} ${c.lastName || ''}`.trim()
      return (
        cName.toLowerCase().includes(q) ||
        (c.phone || '').includes(q) ||
        (c.type || '').toLowerCase().includes(q)
      )
    })

    const filteredIds = filtered.map(c => c.id)
    const allSelected = filteredIds.every(id => selectedIds.includes(id))

    if (allSelected) {
      // Unselect all filtered rows
      set({ selectedIds: selectedIds.filter(id => !filteredIds.includes(id)) })
    } else {
      // Select all filtered rows
      const newSelected = Array.from(new Set([...selectedIds, ...filteredIds]))
      set({ selectedIds: newSelected })
    }
  },

  toggleColumnVisibility: (col) => {
    const { visibleColumns } = get()
    set({
      visibleColumns: {
        ...visibleColumns,
        [col]: !visibleColumns[col]
      }
    })
  },

  setCurrentPage: (currentPage) => set({ currentPage }),
  setPageSize: (pageSize) => set({ pageSize, currentPage: 1 }),

  setSort: (sortColumn, sortOrder) => set({ sortColumn, sortOrder }),

  loadContacts: async () => {
    const hasCache = get().contacts.length > 0
    if (!hasCache) {
      set({ isLoading: true })
    }
    try {
      const [fetched, settings] = await Promise.all([
        contactService.getContacts(),
        contactService.getSettings()
      ])
      set({
        contacts: fetched,
        groupNotAssignedText: settings?.groupNotAssignedText || 'Group not assigned',
        isLoading: false
      })
    } catch (err) {
      // Superseded by a newer load — that request owns the data and the loading flag now.
      if (isRequestCancelled(err)) return
      console.error('Error loading contacts:', err)
      set({ isLoading: false })
    }
  },

  addContact: async (form) => {
    try {
      const newContact = await contactService.addContact(form)
      set((state) => ({ contacts: [newContact, ...state.contacts] }))
      propagateContactChange()
      return newContact
    } catch (err) {
      console.error('Error adding contact:', err)
      throw err
    }
  },

  deleteSelected: async () => {
    const { selectedIds } = get()
    if (selectedIds.length === 0) return

    set({ isLoading: true })
    try {
      await contactService.bulkDeleteContacts(selectedIds)
      const fetched = await contactService.getContacts()
      set({
        contacts: fetched,
        selectedIds: [],
        isLoading: false
      })
      propagateContactChange()
    } catch (err) {
      console.error(err)
      set({ isLoading: false })
      throw err
    }
  },

  toggleContactActive: async (id) => {
    const { contacts } = get()
    const target = contacts.find(c => c.id === id)
    if (!target) return

    try {
      const updatedContact = await contactService.toggleActive(id)
      const mappedContact = { ...updatedContact, active: updatedContact.isActive ?? updatedContact.active ?? true }

      set({
        contacts: contacts.map(c => c.id === id ? mappedContact : c)
      })
      // Deactivating a contact changes what the chat sidebar shows for it, so this propagates
      // like a create or delete rather than only touching the dashboard counters.
      propagateContactChange()

      if (mappedContact.active) {
        toast.success('user enabled successfully')
      } else {
        toast.success('user disabled successfully')
      }
    } catch (err: any) {
      console.error('Error toggling contact active state:', err)
      toast.error(getErrorMessage(err, 'Failed to update user active status.'))
    }
  },

  importContacts: async (file) => {
    set({ isLoading: true })
    try {
      const res = await contactService.importContacts(file)
      const fetched = await contactService.getContacts()
      set({ contacts: fetched })
      propagateContactChange()
      return res
    } catch (err: any) {
      return { success: false, message: getErrorMessage(err, 'Import failed.') }
    } finally {
      set({ isLoading: false })
    }
  }
}))
export default useContactStore
