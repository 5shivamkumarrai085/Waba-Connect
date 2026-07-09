// src/store/contactStore.ts
import { create } from 'zustand'
import { contactService } from '../services/contacts/contactService'
import type { Contact, ContactFormModel } from '../types/contacts'

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
  importContacts: (fileContent: string) => Promise<{ success: boolean; count: number; message: string }>
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
      const fetched = await contactService.getContacts()
      set({ contacts: fetched, isLoading: false })
    } catch (err) {
      console.error('Error loading contacts:', err)
      set({ isLoading: false })
    }
  },
  
  addContact: async (form) => {
    try {
      const newContact = await contactService.addContact(form)
      set((state) => ({ contacts: [newContact, ...state.contacts] }))
      return newContact
    } catch (err) {
      console.error('Error adding contact:', err)
      throw err
    }
  },
  
  deleteSelected: async () => {
    const { selectedIds, contacts } = get()
    if (selectedIds.length === 0) return
    
    console.log(`[API Calling] DELETE /api/contacts/bulk-delete`, selectedIds)
    set({ isLoading: true })
    
    // Simulate API delay
    await new Promise((resolve) => setTimeout(resolve, 100))
    
    set({
      contacts: contacts.filter(c => !selectedIds.includes(c.id)),
      selectedIds: [],
      isLoading: false
    })
  },
  
  toggleContactActive: async (id) => {
    const { contacts } = get()
    const target = contacts.find(c => c.id === id)
    if (!target) return
    
    console.log(`[API Calling] PUT /api/contacts/${id}/toggle-active`, !target.active)
    
    set({
      contacts: contacts.map(c => c.id === id ? { ...c, active: !c.active } : c)
    })
  },
  
  importContacts: async (fileContent) => {
    set({ isLoading: true })
    try {
      const res = await contactService.importContacts(fileContent)
      // Reload contacts if import was successful
      const fetched = await contactService.getContacts()
      set({ contacts: fetched })
      return res
    } catch (err: any) {
      return { success: false, count: 0, message: err?.message || 'Import failed.' }
    } finally {
      set({ isLoading: false })
    }
  }
}))
export default useContactStore
