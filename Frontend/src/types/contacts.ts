// src/types/contacts.ts

export interface AssignedUser {
  id: string
  name: string
  avatarUrl?: string
}

export interface Contact {
  id: number;
  name?: string;
  firstName?: string;
  lastName: string
  company?: string
  type: string // e.g. 'Lead', 'Customer'
  phone: string
  assignedUser?: AssignedUser
  status: string // e.g. 'New', 'In Progress'
  source: string // e.g. 'whatsapp'
  groups: string // e.g. 'Groups not found'
  active: boolean
  createdAt: string
  email?: string
  website?: string
  language?: string
  city?: string
  state?: string
  country?: string
  zipCode?: string
  address?: string
  description?: string
  assignedTo?: string
  isActive?: boolean
}

export interface ContactStatus {
  id: string
  name: string
}

export interface ContactType {
  id: string
  name: string
}

export interface ContactSource {
  id: string
  name: string
}

export interface ContactGroup {
  id: string
  name: string
}

export interface ContactLanguage {
  id: string
  name: string
}

export interface ContactFormModel {
  // Tab 1
  status: string
  source: string
  assigned: string
  firstName: string
  lastName: string
  company: string
  type: string
  email: string
  phone: string
  website: string
  language: string
  groups: string
  // Tab 2
  city: string
  state: string
  country: string
  zipCode: string
  address: string
  description: string
}

export interface UploadFileModel {
  name: string
  size: number
  type: string
  progress: number
  status: 'idle' | 'uploading' | 'success' | 'error'
}
