// src/types/templates.ts

export interface TemplateVariable {
  position: number
  sampleValue?: string
  description?: string
}

export type TemplateButtonType = 'QUICK_REPLY' | 'URL' | 'PHONE_NUMBER' | 'COPY_CODE'

/** One template button, as Meta defines it. */
export interface TemplateButton {
  type: TemplateButtonType
  text: string
  /** URL buttons; may end in {{1}} for a per-recipient suffix. */
  url?: string
  phone_number?: string
  /** Copy-code buttons: an example code for Meta's review. */
  example?: string
}

export interface CreateTemplateInput {
  name: string
  language: string
  /** One of the categories from GET api/reference/template-options. */
  category: string
  bodyText: string
  headerType: 'None' | 'Text'
  headerContent?: string
  footerText?: string
  variables: TemplateVariable[]
  buttons: TemplateButton[]
}

export interface Template {
  id: number
  name: string
  language: string
  category: string
  type?: string
  templateType: string
  status: 'APPROVED' | 'REJECTED' | 'PENDING' | string
  bodyText: string
  rejectReason?: string
  headerType?: string
  headerContent?: string
  footerText?: string
  variables?: TemplateVariable[]
  buttons?: TemplateButton[]
  createdAt?: string
  updatedAt?: string
}

export interface TemplateLanguage {
  code: string
  name: string
}

export interface TemplateCategory {
  id: string
  name: string
}

export interface TemplateStatus {
  id: string
  name: string
}

export interface TemplateType {
  id: string
  name: string
}
