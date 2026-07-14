// src/types/templates.ts

export interface TemplateVariable {
  position: number
  sampleValue?: string
  description?: string
}

export interface Template {
  id: number
  name: string
  language: string
  category: string
  type: string
  status: 'APPROVED' | 'REJECTED' | 'PENDING' | string
  bodyText: string
  headerType?: string
  headerContent?: string
  footerText?: string
  variables?: TemplateVariable[]
  createdAt?: string
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
