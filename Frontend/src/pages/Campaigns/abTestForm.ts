/** The A/B test part of the campaign wizard's form. */

export interface AbVariantForm {
  templateId?: number
  emailTemplateId?: number
  subjectOverride?: string
}

export interface AbTestForm {
  enabled: boolean
  /** 0 until the test is switched on; then the server's default applies. */
  percent: number
  metric: string
  decideAfterHours: number
  variants: AbVariantForm[]
}

export const emptyAbTest = (): AbTestForm => ({ enabled: false, percent: 0, metric: '', decideAfterHours: 0, variants: [{}] })
