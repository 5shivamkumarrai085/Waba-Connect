import React, { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { Plus, CreditCard } from 'lucide-react'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { StatusBadge } from '../../../components/StatusBadge/StatusBadge'
import { Toggle } from '../../../components/Toggle/Toggle'
import Can from '../../../components/Can/Can'
import { remittanceService, type PaymentMethod, type RemittanceReference, type SavePaymentMethodInput } from '../../../services/remittance/remittanceService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { ConfigTable, CountedLabel, FormModal, fromNumber, toNumber } from './ConfigTable'

interface PaymentForm {
  sourceCountryCode: string
  provider: string
  code: string
  label: string
  description: string
  badge: string
  paymentWindowMinutes: string
  isActive: boolean
  sortOrder: string
}

const toForm = (p: PaymentMethod | null, providers: string[], fallbackCountry: string): PaymentForm => ({
  sourceCountryCode: p?.sourceCountryCode ?? fallbackCountry,
  provider: p?.provider ?? providers[0] ?? '',
  code: p?.code ?? '',
  label: p?.label ?? '',
  description: p?.description ?? '',
  badge: p?.badge ?? '',
  paymentWindowMinutes: fromNumber(p?.paymentWindowMinutes ?? 15),
  isActive: p?.isActive ?? true,
  sortOrder: fromNumber(p?.sortOrder ?? 0)
})

const toInput = (f: PaymentForm): SavePaymentMethodInput => ({
  sourceCountryCode: f.sourceCountryCode.trim().toUpperCase(),
  provider: f.provider,
  code: f.code.trim().toUpperCase(),
  label: f.label.trim(),
  description: f.description.trim() || null,
  badge: f.badge.trim() || null,
  paymentWindowMinutes: toNumber(f.paymentWindowMinutes) ?? 15,
  isActive: f.isActive,
  sortOrder: toNumber(f.sortOrder) ?? 0
})

const windowText = (minutes: number) =>
  minutes % 1440 === 0 ? `${minutes / 1440} day${minutes === 1440 ? '' : 's'}` : minutes % 60 === 0 ? `${minutes / 60} h` : `${minutes} min`

/**
 * Setup › Send Money › Payment methods: how the customer pays for a transfer. Each one hands off
 * to its provider's hosted page or counter reference — no card or banking details are ever asked
 * for in WhatsApp.
 */
export const PaymentMethodsTab: React.FC<{ reference: RemittanceReference; canManage: boolean }> = ({ reference, canManage }) => {
  const [methods, setMethods] = useState<PaymentMethod[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [editing, setEditing] = useState<PaymentMethod | null>(null)
  const [isFormOpen, setIsFormOpen] = useState(false)
  const [form, setForm] = useState<PaymentForm>(toForm(null, reference.paymentProviders, ''))
  const [formError, setFormError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<PaymentMethod | null>(null)

  const load = useCallback(async () => {
    setIsLoading(true)
    setLoadError(null)
    try { setMethods(await remittanceService.paymentMethods()) }
    catch (error) { setLoadError(getErrorMessage(error, 'Payment methods could not be loaded.')) }
    finally { setIsLoading(false) }
  }, [])

  useEffect(() => { void load() }, [load])

  const open = (p: PaymentMethod | null) => {
    setEditing(p)
    setForm(toForm(p, reference.paymentProviders, methods[0]?.sourceCountryCode ?? ''))
    setFormError(null)
    setIsFormOpen(true)
  }

  const submit = async () => {
    setIsSubmitting(true)
    setFormError(null)
    try {
      const saved = await remittanceService.savePaymentMethod(editing?.id ?? null, toInput(form))
      toast.success(`${saved.label} saved.`)
      setIsFormOpen(false)
      void load()
    } catch (error) {
      setFormError(getErrorMessage(error, 'The payment method could not be saved.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const setActive = async (p: PaymentMethod, isActive: boolean) => {
    try {
      await remittanceService.savePaymentMethod(p.id, { ...toInput(toForm(p, reference.paymentProviders, '')), isActive })
      void load()
    } catch (error) {
      toast.error(getErrorMessage(error, 'The payment method could not be updated.'))
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try { await remittanceService.deletePaymentMethod(deleteTarget.id); toast.success(`${deleteTarget.label} deleted.`); void load() }
    catch (error) { toast.error(getErrorMessage(error, 'The payment method could not be deleted.')) }
    finally { setDeleteTarget(null) }
  }

  const f = form
  const canSubmit = f.sourceCountryCode.trim() !== '' && f.code.trim() !== '' && f.label.trim() !== '' && f.label.trim().length <= 30
    && f.badge.length <= 20 && f.description.length <= 300 && f.provider !== ''

  return (
    <>
      <div className="contacts-toolbar">
        <Can permission="Remittance.Manage">
          <button type="button" className="btn-toolbar-primary" onClick={() => open(null)}>
            <Plus size={15} aria-hidden="true" /><span>New Payment Method</span>
          </button>
        </Can>
      </div>

      <div className="contacts-card">
        <ConfigTable<PaymentMethod>
          rows={methods}
          isLoading={isLoading}
          loadError={loadError}
          onRetry={() => void load()}
          rowLabel={p => p.label}
          empty={{
            iconName: 'CreditCard',
            title: 'No payment methods',
            message: 'Customers cannot confirm a transfer until their country has at least one.',
            action: canManage ? { label: 'New Payment Method', onClick: () => open(null) } : undefined
          }}
          actions={[
            { label: 'Edit', onSelect: p => open(p), disabled: () => !canManage },
            { label: 'Delete', onSelect: setDeleteTarget, destructive: true, disabled: () => !canManage }
          ]}
          columns={[
            { header: 'Method', cell: p => <span>{p.label} {p.badge && <StatusBadge type="info" text={p.badge} />}</span> },
            { header: 'Code', cell: p => <code translate="no" className="sm-code">{p.code}</code> },
            { header: 'Customers in', cell: p => <span translate="no">{p.sourceCountryCode}</span> },
            { header: 'Provider', cell: p => <span translate="no">{p.provider}</span> },
            { header: 'Pay within', cell: p => windowText(p.paymentWindowMinutes) },
            {
              header: 'Offered',
              className: 'text-center',
              cell: p => (
                <Toggle checked={p.isActive} onChange={on => setActive(p, on)} disabled={!canManage}
                  ariaLabel={`${p.label} is ${p.isActive ? 'offered' : 'hidden'}`} disabledReason="You can view Send Money settings but not change them." />
              )
            }
          ]}
        />
      </div>

      <FormModal
        isOpen={isFormOpen}
        onClose={() => setIsFormOpen(false)}
        title={editing ? `Edit ${editing.label}` : 'New Payment Method'}
        subtitle="Paid on the provider's page — never inside WhatsApp"
        icon={<CreditCard size={18} />}
        formId="sm-payment-form"
        error={formError}
        isSubmitting={isSubmitting}
        canSubmit={canSubmit}
        submitLabel={editing ? 'Save Changes' : 'Add Method'}
        onSubmit={submit}
      >
        <div className="setup-field">
          <CountedLabel htmlFor="sm-pay-label" text="Label" value={f.label} max={30} required />
          <input id="sm-pay-label" className="setup-input" value={f.label} maxLength={60} placeholder="FPX Online Banking" autoComplete="off" data-autofocus
            onChange={e => setForm(x => ({ ...x, label: e.target.value }))} />
        </div>
        <div className="setup-field-row">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-pay-code">Code</label>
            <input id="sm-pay-code" className="setup-input" value={f.code} maxLength={40} placeholder="FPX" translate="no" autoComplete="off"
              onChange={e => setForm(x => ({ ...x, code: e.target.value }))} />
          </div>
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-pay-country">Customers in</label>
            <input id="sm-pay-country" className="setup-input" value={f.sourceCountryCode} maxLength={2} placeholder="MY" translate="no" autoComplete="off"
              onChange={e => setForm(x => ({ ...x, sourceCountryCode: e.target.value }))} />
          </div>
        </div>
        <div className="setup-field-row">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-pay-provider">Provider</label>
            <select id="sm-pay-provider" className="setup-input" value={f.provider} onChange={e => setForm(x => ({ ...x, provider: e.target.value }))}>
              {reference.paymentProviders.map(p => <option key={p} value={p}>{p}</option>)}
            </select>
          </div>
          <div className="setup-field">
            <label className="setup-label" htmlFor="sm-pay-window">Pay within (minutes)</label>
            <input id="sm-pay-window" className="setup-input" type="number" min={1} max={2880} value={f.paymentWindowMinutes}
              onChange={e => setForm(x => ({ ...x, paymentWindowMinutes: e.target.value }))} />
          </div>
        </div>
        <div className="setup-field">
          <CountedLabel htmlFor="sm-pay-desc" text="Description" value={f.description} max={300} />
          <textarea id="sm-pay-desc" className="setup-input setup-textarea" value={f.description} maxLength={400} rows={3}
            placeholder="Pay instantly from your bank account." onChange={e => setForm(x => ({ ...x, description: e.target.value }))} />
        </div>
        <div className="setup-field-row">
          <div className="setup-field">
            <CountedLabel htmlFor="sm-pay-badge" text="Badge" value={f.badge} max={20} />
            <input id="sm-pay-badge" className="setup-input" value={f.badge} maxLength={40} placeholder="INSTANT" autoComplete="off"
              onChange={e => setForm(x => ({ ...x, badge: e.target.value }))} />
          </div>
          <div className="setup-field setup-field-narrow">
            <label className="setup-label" htmlFor="sm-pay-sort">Order</label>
            <input id="sm-pay-sort" className="setup-input" type="number" value={f.sortOrder} onChange={e => setForm(x => ({ ...x, sortOrder: e.target.value }))} />
          </div>
        </div>
        <div className="setup-toggle-item">
          <span className="setup-toggle-label">Offered in WhatsApp</span>
          <Toggle checked={f.isActive} onChange={on => setForm(x => ({ ...x, isActive: on }))} ariaLabel="Offered in WhatsApp" />
        </div>
      </FormModal>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Payment Method"
        message={`Delete ${deleteTarget?.label}? Existing transfers keep their payment details.`}
        confirmText="Delete Method"
        isDestructive
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </>
  )
}
