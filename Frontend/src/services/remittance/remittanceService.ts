import { apiClient } from '../apiClient'

// Setup › Send Money and the Transfers screen. Every option shown to customers in the WhatsApp
// Flow (countries, payout methods, recipient fields, banks, fees, pick-lists, payment methods) is
// configured through these endpoints — none of it lives in the frontend.

export type FieldInputType = 'Text' | 'Number' | 'Email' | 'Phone'

/** Submitting: one worker is handing the payout to the provider. NeedsReview: paid, payout not confirmed — a person must check. */
export type TransferOrderStatus = 'Created' | 'AwaitingPayment' | 'Paid' | 'Submitting' | 'Processing' | 'Completed' | 'Expired' | 'Cancelled' | 'Failed' | 'NeedsReview'
export type TransferPaymentStatus = 'Pending' | 'Succeeded' | 'Failed' | 'Expired' | 'Refunded'

export interface FlowPurpose { value: string; label: string }

export interface RemittanceReference {
  paymentProviders: string[]
  payoutProviders: string[]
  fieldInputTypes: FieldInputType[]
  lookupCategories: string[]
  orderStatuses: TransferOrderStatus[]
  paymentStatuses: TransferPaymentStatus[]
  flowPurposes: FlowPurpose[]
  maxBeneficiaryFields: number
  enabled: boolean
}

export interface Corridor {
  id: number
  sourceCountryCode: string
  sourceCurrency: string
  destinationCountryCode: string
  destinationCountryName: string
  destinationCurrency: string
  destinationDecimals: number
  minSendAmount: number
  maxSendAmount: number
  fxMarginBps: number
  isActive: boolean
  sortOrder: number
  payoutMethodCount: number
}

export type SaveCorridorInput = Omit<Corridor, 'id' | 'payoutMethodCount'>

export interface FeeRule {
  fixedFee: number
  percentFee: number
  minFee: number | null
  maxFee: number | null
  firstTransferFee: number | null
  promoLabel: string | null
}

export interface PayoutMethod {
  id: number
  corridorId: number
  code: string
  name: string
  deliveryEta: string | null
  payoutProvider: string
  isActive: boolean
  sortOrder: number
  fee: FeeRule | null
  fieldCount: number
  institutionCount: number
}

export type SavePayoutMethodInput = Omit<PayoutMethod, 'id' | 'corridorId' | 'fieldCount' | 'institutionCount'>

export interface BeneficiaryField {
  id: number
  key: string
  label: string
  inputType: FieldInputType
  isRequired: boolean
  minLength: number | null
  maxLength: number | null
  pattern: string | null
  helperText: string | null
  isAccountIdentifier: boolean
  isActive: boolean
  sortOrder: number
}

export type SaveBeneficiaryFieldInput = Omit<BeneficiaryField, 'id'>

export interface Institution { id: number; code: string; name: string; isActive: boolean; sortOrder: number }
export type SaveInstitutionInput = Omit<Institution, 'id'>

export interface Lookup { id: number; category: string; code: string; label: string; isActive: boolean; sortOrder: number }
export type SaveLookupInput = Omit<Lookup, 'id' | 'category'>

export interface PaymentMethod {
  id: number
  sourceCountryCode: string
  provider: string
  code: string
  label: string
  description: string | null
  badge: string | null
  paymentWindowMinutes: number
  isActive: boolean
  sortOrder: number
}

export type SavePaymentMethodInput = Omit<PaymentMethod, 'id'>

export interface RatePreview {
  corridorId: number
  from: string
  to: string
  ok: boolean
  midRate: number | null
  appliedRate: number | null
  asOfUtc: string | null
  provider: string | null
  error: string | null
}

export interface TransferOrderSummary {
  id: number
  reference: string
  contactName: string
  contactPhone: string
  beneficiaryName: string
  institutionName: string | null
  payoutMethodName: string
  sendCurrency: string
  sendAmount: number
  totalPayable: number
  receiveCurrency: string
  receiveAmount: number
  orderStatus: TransferOrderStatus
  paymentStatus: TransferPaymentStatus
  paymentMethodCode: string
  paymentProvider: string
  createdAt: string
  paidAt: string | null
  completedAt: string | null
}

export interface TransferOrderEvent {
  id: number
  kind: string
  fromStatus: string | null
  toStatus: string | null
  source: string
  provider: string | null
  providerEventId: string | null
  detail: string | null
  occurredAt: string
}

export interface TransferOrderDetail {
  order: TransferOrderSummary
  maskedAccount: string | null
  feeAmount: number
  appliedRate: number
  sourceOfFunds: string | null
  purpose: string | null
  remarks: string | null
  providerPaymentId: string | null
  paymentUrl: string | null
  paymentReference: string | null
  paymentExpiresAt: string | null
  payoutProvider: string
  payoutReference: string | null
  events: TransferOrderEvent[]
}

export interface Paged<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export interface OrderFilters {
  page: number
  pageSize: number
  search?: string
  orderStatus?: string
  paymentStatus?: string
}

const unwrap = <T,>(response: { data?: { data?: T } }): T => response.data?.data as T

let referenceCache: Promise<RemittanceReference> | null = null

export const remittanceService = {
  /** Cached for the session, like the other reference catalogues. */
  reference: (): Promise<RemittanceReference> => {
    referenceCache ??= apiClient.get('/remittance/reference').then(r => unwrap<RemittanceReference>(r))
    referenceCache.catch(() => { referenceCache = null })
    return referenceCache
  },

  corridors: async (): Promise<Corridor[]> => unwrap<Corridor[]>(await apiClient.get('/remittance/corridors')) ?? [],
  saveCorridor: async (id: number | null, input: SaveCorridorInput): Promise<Corridor> =>
    unwrap<Corridor>(id ? await apiClient.put(`/remittance/corridors/${id}`, input) : await apiClient.post('/remittance/corridors', input)),
  deleteCorridor: async (id: number): Promise<void> => { await apiClient.delete(`/remittance/corridors/${id}`) },
  previewRate: async (corridorId: number): Promise<RatePreview> => unwrap<RatePreview>(await apiClient.get(`/remittance/corridors/${corridorId}/rate`)),

  payoutMethods: async (corridorId: number): Promise<PayoutMethod[]> =>
    unwrap<PayoutMethod[]>(await apiClient.get(`/remittance/corridors/${corridorId}/payout-methods`)) ?? [],
  savePayoutMethod: async (corridorId: number, id: number | null, input: SavePayoutMethodInput): Promise<PayoutMethod> =>
    unwrap<PayoutMethod>(id
      ? await apiClient.put(`/remittance/corridors/${corridorId}/payout-methods/${id}`, input)
      : await apiClient.post(`/remittance/corridors/${corridorId}/payout-methods`, input)),
  deletePayoutMethod: async (id: number): Promise<void> => { await apiClient.delete(`/remittance/payout-methods/${id}`) },

  fields: async (methodId: number): Promise<BeneficiaryField[]> =>
    unwrap<BeneficiaryField[]>(await apiClient.get(`/remittance/payout-methods/${methodId}/fields`)) ?? [],
  saveField: async (methodId: number, id: number | null, input: SaveBeneficiaryFieldInput): Promise<BeneficiaryField> =>
    unwrap<BeneficiaryField>(id
      ? await apiClient.put(`/remittance/payout-methods/${methodId}/fields/${id}`, input)
      : await apiClient.post(`/remittance/payout-methods/${methodId}/fields`, input)),
  deleteField: async (id: number): Promise<void> => { await apiClient.delete(`/remittance/fields/${id}`) },

  institutions: async (methodId: number): Promise<Institution[]> =>
    unwrap<Institution[]>(await apiClient.get(`/remittance/payout-methods/${methodId}/institutions`)) ?? [],
  saveInstitution: async (methodId: number, id: number | null, input: SaveInstitutionInput): Promise<Institution> =>
    unwrap<Institution>(id
      ? await apiClient.put(`/remittance/payout-methods/${methodId}/institutions/${id}`, input)
      : await apiClient.post(`/remittance/payout-methods/${methodId}/institutions`, input)),
  deleteInstitution: async (id: number): Promise<void> => { await apiClient.delete(`/remittance/institutions/${id}`) },

  lookups: async (category: string): Promise<Lookup[]> => unwrap<Lookup[]>(await apiClient.get(`/remittance/lookups/${category}`)) ?? [],
  saveLookup: async (category: string, id: number | null, input: SaveLookupInput): Promise<Lookup> =>
    unwrap<Lookup>(id ? await apiClient.put(`/remittance/lookups/${category}/${id}`, input) : await apiClient.post(`/remittance/lookups/${category}`, input)),
  deleteLookup: async (id: number): Promise<void> => { await apiClient.delete(`/remittance/lookups/${id}`) },

  paymentMethods: async (): Promise<PaymentMethod[]> => unwrap<PaymentMethod[]>(await apiClient.get('/remittance/payment-methods')) ?? [],
  savePaymentMethod: async (id: number | null, input: SavePaymentMethodInput): Promise<PaymentMethod> =>
    unwrap<PaymentMethod>(id ? await apiClient.put(`/remittance/payment-methods/${id}`, input) : await apiClient.post('/remittance/payment-methods', input)),
  deletePaymentMethod: async (id: number): Promise<void> => { await apiClient.delete(`/remittance/payment-methods/${id}`) },

  orders: async (filters: OrderFilters): Promise<Paged<TransferOrderSummary>> =>
    unwrap<Paged<TransferOrderSummary>>(await apiClient.get('/remittance/orders', {
      params: {
        page: filters.page,
        pageSize: filters.pageSize,
        search: filters.search || undefined,
        orderStatus: filters.orderStatus || undefined,
        paymentStatus: filters.paymentStatus || undefined
      }
    })),
  order: async (id: number): Promise<TransferOrderDetail> => unwrap<TransferOrderDetail>(await apiClient.get(`/remittance/orders/${id}`))
}

// ── display helpers ──────────────────────────────────────────────────────────────────────────

/** "source_of_funds" → "Source of funds", "AwaitingPayment" → "Awaiting payment". */
export const humanize = (value: string) => {
  const spaced = value.replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

export const orderStatusBadge = (status: TransferOrderStatus): string => {
  switch (status) {
    case 'Completed': return 'success'
    case 'Paid':
    case 'Submitting':
    case 'Processing': return 'info'
    case 'Created':
    case 'AwaitingPayment': return 'pending'
    case 'Failed':
    case 'NeedsReview': return 'error'
    default: return 'closed'
  }
}

export const paymentStatusBadge = (status: TransferPaymentStatus): string => {
  switch (status) {
    case 'Succeeded': return 'success'
    case 'Pending': return 'pending'
    case 'Failed': return 'error'
    default: return 'closed'
  }
}

/** A rate as the customer saw it in WhatsApp (RemittancePricing.FormatRate): 2, 4 or 6 decimals by size, truncated. */
export const formatRate = (rate: number) => {
  const digits = rate >= 100 ? 2 : rate >= 1 ? 4 : 6
  const factor = 10 ** digits
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: digits }).format(Math.trunc(rate * factor) / factor)
}

export const formatMoney = (currency: string, amount: number, decimals = 2) =>
  `${currency} ${new Intl.NumberFormat(undefined, { minimumFractionDigits: decimals, maximumFractionDigits: decimals }).format(amount)}`

export default remittanceService
