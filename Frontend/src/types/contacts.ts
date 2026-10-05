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
  /** IANA time zone, e.g. 'Asia/Kolkata'. */
  timeZone?: string | null
  /** ISO date (yyyy-mm-dd). */
  dateOfBirth?: string | null
  /** Whole years today, worked out by the server from the date of birth. */
  age?: number | null
  /** The Click-to-WhatsApp ad that first brought this contact in. */
  adSourceId?: string | null
  adSourceUrl?: string | null
  adHeadline?: string | null
  adAttributedAt?: string | null
  zipCode?: string
  address?: string
  description?: string
  assignedTo?: string
  isActive?: boolean
  tags?: string
}

// Across all four: `id` carries the lookup's immutable Value — what the contact row actually
// stores — `name` the editable label, and `color` the hex chosen in Setup. A null colour falls
// back to the CSS-class treatment in StatusBadge.
export interface ContactStatus {
  id: string
  name: string
  color?: string | null
}

export interface ContactType {
  id: string
  name: string
  color?: string | null
}

export interface ContactSource {
  id: string
  name: string
  color?: string | null
}

export interface ContactGroup {
  id: string
  name: string
  color?: string | null
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
  timeZone?: string
  zipCode: string
  address: string
  description: string
  /** ISO date (yyyy-mm-dd); empty when unknown. */
  dateOfBirth?: string
}

/** One contact as GET /Contacts/{id} returns it — the server's ContactResponse. */
export interface ContactDetails {
  id: number
  name: string
  phone: string
  type: string
  status: string
  source: string
  assignedTo?: string | null
  email?: string | null
  company?: string | null
  website?: string | null
  city?: string | null
  state?: string | null
  country?: string | null
  timeZone?: string | null
  description?: string | null
  /** Comma-separated. */
  tags?: string | null
  isActive: boolean
  createdAt: string
  updatedAt: string
  groups: { id: number; name: string; color?: string | null }[]
}
