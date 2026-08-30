// Mirrors Backend/Models/DTOs/Setup/OmniSettingsDtos.cs.
//
// The page is rendered from this schema rather than from a layout written here: a field the UI
// draws is one the server declared, and therefore one it knows how to validate and store.

/** Field kinds the client has a renderer for. Anything else is skipped rather than crashing. */
export type OmniSettingsFieldType =
  | 'toggle'
  | 'text'
  | 'password'
  | 'number'
  | 'select'
  | 'multiselect'
  | 'tags'

export interface OmniSettingsOption {
  value: string
  label: string
}

export interface OmniSettingsField {
  key: string
  label: string
  type: OmniSettingsFieldType
  helper?: string | null
  placeholder?: string | null
  /** Suffix rendered inside the control — "Hours", "Days", "seconds". */
  unit?: string | null
  required: boolean
  /** Makes `required` conditional on another field being switched on. */
  requiredWhenKey?: string | null
  optionSource?: string | null
  options: OmniSettingsOption[]
  /** Typed to match the field kind: boolean, number, string, or string[]. Never set for a secret. */
  value: unknown
  /** True for a write-only credential — stored encrypted, never returned. */
  isSecret: boolean
  /** For a secret: whether one is currently stored. */
  hasValue: boolean
  min?: number | null
  max?: number | null
}

export interface OmniSettingsNote {
  tone: 'warning' | 'info'
  text: string
}

export interface OmniSettingsSection {
  key: string
  label: string
  description: string
  /** Lucide icon name, resolved by the client against its own icon set. */
  icon: string
  fields: OmniSettingsField[]
  notes: OmniSettingsNote[]
}

export interface OmniSettingsSchema {
  sections: OmniSettingsSection[]
}

/** Field key to value. Only keys belonging to the section being saved are honoured. */
export type OmniSettingsValues = Record<string, unknown>
