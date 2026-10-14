import React, { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { Plus, ArrowLeft, Banknote } from 'lucide-react'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { StatusBadge } from '../../../components/StatusBadge/StatusBadge'
import { Toggle } from '../../../components/Toggle/Toggle'
import Can from '../../../components/Can/Can'
import {
  remittanceService,
  formatMoney,
  type Corridor,
  type FeeRule,
  type PayoutMethod,
  type RemittanceReference,
  type SavePayoutMethodInput
} from '../../../services/remittance/remittanceService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { ConfigTable, CountedLabel, FormModal, fromNumber, toNumber } from './ConfigTable'
import { MethodDetailsView } from './MethodDetailsView'

interface MethodForm {
  code: string
  name: string
  deliveryEta: string
  payoutProvider: string
  isActive: boolean
  sortOrder: string
  fixedFee: string
  percentFee: string
  minFee: string
  maxFee: string
  firstTransferFee: string
  promoLabel: string
}

const toForm = (m: PayoutMethod | null, providers: string[]): MethodForm => ({
  code: m?.code ?? '',
  name: m?.name ?? '',
  deliveryEta: m?.deliveryEta ?? '',
  payoutProvider: m?.payoutProvider ?? providers[0] ?? '',
  isActive: m?.isActive ?? true,
  sortOrder: fromNumber(m?.sortOrder ?? 0),
  fixedFee: fromNumber(m?.fee?.fixedFee ?? 0),
  percentFee: fromNumber(m?.fee?.percentFee ?? 0),
  minFee: fromNumber(m?.fee?.minFee),
  maxFee: fromNumber(m?.fee?.maxFee),
  firstTransferFee: fromNumber(m?.fee?.firstTransferFee),
  promoLabel: m?.fee?.promoLabel ?? ''
})

const toInput = (f: MethodForm): SavePayoutMethodInput => ({
  code: f.code.trim().toUpperCase(),
  name: f.name.trim(),
  deliveryEta: f.deliveryEta.trim() || null,
  payoutProvider: f.payoutProvider,
  isActive: f.isActive,
  sortOrder: toNumber(f.sortOrder) ?? 0,
  fee: {
    fixedFee: toNumber(f.fixedFee) ?? 0,
    percentFee: toNumber(f.percentFee) ?? 0,
    minFee: toNumber(f.minFee),
    maxFee: toNumber(f.maxFee),
    firstTransferFee: toNumber(f.firstTransferFee),
    promoLabel: f.promoLabel.trim() || null
  }
})

const feeSummary = (currency: string, fee: FeeRule | null) => {
  if (!fee) return 'No fee'
  const parts = [formatMoney(currency, fee.fixedFee)]
  if (fee.percentFee > 0) parts.push(`${fee.percentFee}%`)
  let text = parts.join(' + ')
  if (fee.minFee !== null || fee.maxFee !== null) text += ` (${fee.minFee ?? 0}–${fee.maxFee ?? '∞'})`
  return text
}

interface Props {
  corridor: Corridor
  reference: RemittanceReference
  canManage: boolean
  onBack: () => void
}

/** One country's payout methods — how the money reaches the recipient — with their fees. */
export const PayoutMethodsView: React.FC<Props> = ({ corridor, reference, canManage, onBack }) => {
  const [methods, setMethods] = useState<PayoutMethod[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [selected, setSelected] = useState<PayoutMethod | null>(null)

  const [editing, setEditing] = useState<PayoutMethod | null>(null)
  const [isFormOpen, setIsFormOpen] = useState(false)
  const [form, setForm] = useState<MethodForm>(toForm(null, reference.payoutProviders))
  const [formError, setFormError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<PayoutMethod | null>(null)

  const load = useCallback(async () => {
    setIsLoading(true)
    setLoadError(null)
    try {
      setMethods(await remittanceService.payoutMethods(corridor.id))
    } catch (error) {
      setLoadError(getErrorMessage(error, 'Payout methods could not be loaded.'))
    } finally {
      setIsLoading(false)
    }
  }, [corridor.id])

  useEffect(() => { void load() }, [load])

  const open = (m: PayoutMethod | null) => {
    setEditing(m)
    setForm(toForm(m, reference.payoutProviders))
    setFormError(null)
    setIsFormOpen(true)
  }

  const submit = async () => {
    setIsSubmitting(true)
    setFormError(null)
    try {
      const saved = await remittanceService.savePayoutMethod(corridor.id, editing?.id ?? null, toInput(form))
      toast.success(editing ? `${saved.name} saved.` : `${saved.name} added. Set up its recipient fields next.`)
      setIsFormOpen(false)
      void load()
    } catch (error) {
      setFormError(getErrorMessage(error, 'The payout method could not be saved.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const setActive = async (m: PayoutMethod, isActive: boolean) => {
    try {
      await remittanceService.savePayoutMethod(corridor.id, m.id, { ...toInput(toForm(m, reference.payoutProviders)), isActive })
      toast.success(`${m.name} ${isActive ? 'is now offered' : 'is no longer offered'}.`)
      void load()
    } catch (error) {
      toast.error(getErrorMessage(error, 'The payout method could not be updated.'))
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await remittanceService.deletePayoutMethod(deleteTarget.id)
      toast.success(`${deleteTarget.name} deleted.`)
      void load()
    } catch (error) {
      toast.error(getErrorMessage(error, 'The payout method could not be deleted.'))
    } finally {
      setDeleteTarget(null)
    }
  }

  if (selected) {
    return (
      <MethodDetailsView
        corridor={corridor}
        method={selected}
        reference={reference}
        canManage={canManage}
        onBack={() => { setSelected(null); void load() }}
        onBackToCountries={onBack}
      />
    )
  }

  const f = form
  const canSubmit = f.code.trim() !== '' && f.name.trim() !== '' && f.name.trim().length <= 30 && f.payoutProvider !== '' && f.promoLabel.length <= 20

  return (
    <>
      <nav className="sm-breadcrumb" aria-label="Breadcrumb">
        <button type="button" className="webhook-link-btn" onClick={onBack}><ArrowLeft size={14} aria-hidden="true" /> All countries</button>
        <span aria-hidden="true">/</span>
        <span aria-current="page">{corridor.destinationCountryName} <span className="sm-code" translate="no">{corridor.sourceCurrency} → {corridor.destinationCurrency}</span></span>
      </nav>

      <div className="contacts-toolbar">
        <Can permission="Remittance.Manage">
          <button type="button" className="btn-toolbar-primary" onClick={() => open(null)}>
            <Plus size={15} aria-hidden="true" /><span>New Payout Method</span>
          </button>
        </Can>
      </div>

      <div className="contacts-card">
        <ConfigTable<PayoutMethod>
          rows={methods}
          isLoading={isLoading}
          loadError={loadError}
          onRetry={() => void load()}
          rowLabel={m => m.name}
          empty={{
            iconName: 'Banknote',
            title: 'No payout methods yet',
            message: `${corridor.destinationCountryName} is hidden from customers until it has at least one, such as bank transfer or e-wallet.`,
            action: canManage ? { label: 'New Payout Method', onClick: () => open(null) } : undefined
          }}
          actions={[
            { label: 'Recipient Fields & Banks', onSelect: setSelected },
            { label: 'Edit', onSelect: m => open(m), disabled: () => !canManage },
            { label: 'Delete', onSelect: setDeleteTarget, destructive: true, disabled: () => !canManage }
          ]}
          columns={[
            {
              header: 'Method',
              cell: m => (
                <button type="button" className="setup-user-name sm-drill" onClick={() => setSelected(m)}>
                  <span>{m.name}</span>
                  <span className="sm-code" translate="no">{m.code}</span>
                </button>
              )
            },
            { header: 'Delivery', cell: m => m.deliveryEta ?? '—' },
            { header: 'Fee', cell: m => feeSummary(corridor.sourceCurrency, m.fee) },
            {
              header: 'First transfer',
              cell: m => m.fee?.firstTransferFee !== null && m.fee?.firstTransferFee !== undefined
                ? <>{formatMoney(corridor.sourceCurrency, m.fee.firstTransferFee)} {m.fee.promoLabel && <StatusBadge type="success" text={m.fee.promoLabel} />}</>
                : '—'
            },
            {
              header: 'Fields',
              className: 'text-right sm-num',
              cell: m => m.fieldCount === 0 ? <StatusBadge type="warning" text="None" /> : `${m.fieldCount} / ${reference.maxBeneficiaryFields}`
            },
            { header: 'Banks', className: 'text-right sm-num', cell: m => m.institutionCount || '—' },
            { header: 'Payout partner', cell: m => <span translate="no">{m.payoutProvider}</span> },
            {
              header: 'Offered',
              className: 'text-center',
              cell: m => (
                <Toggle checked={m.isActive} onChange={on => setActive(m, on)} disabled={!canManage}
                  ariaLabel={`${m.name} is ${m.isActive ? 'offered' : 'not offered'}`}
                  disabledReason="You can view Send Money settings but not change them." />
              )
            }
          ]}
        />
      </div>

      <FormModal
        isOpen={isFormOpen}
        onClose={() => setIsFormOpen(false)}
        title={editing ? `Edit ${editing.name}` : 'New Payout Method'}
        subtitle={`How money reaches recipients in ${corridor.destinationCountryName}`}
        icon={<Banknote size={18} />}
        formId="sm-method-form"
        error={formError}
        isSubmitting={isSubmitting}
        canSubmit={canSubmit}
        submitLabel={editing ? 'Save Changes' : 'Add Method'}
        onSubmit={submit}
      >
        <div className="setup-field">
          <CountedLabel htmlFor="sm-method-name" text="Name" value={f.name} max={30} required />
          <input id="sm-method-name" className="setup-input" value={f.name} maxLength={60} placeholder="Bank Transfer" autoComplete="off" data-autofocus
            onChange={e => setForm(x => ({ ...x, name: e.target.value }))} />
        </div>
        <div className="setup-field-row">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-method-code">Code</label>
            <input id="sm-method-code" className="setup-input" value={f.code} maxLength={40} placeholder="BANK_TRANSFER" translate="no" autoComplete="off"
              onChange={e => setForm(x => ({ ...x, code: e.target.value }))} />
          </div>
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-method-provider">Payout partner</label>
            <select id="sm-method-provider" className="setup-input" value={f.payoutProvider} onChange={e => setForm(x => ({ ...x, payoutProvider: e.target.value }))}>
              {reference.payoutProviders.map(p => <option key={p} value={p}>{p}</option>)}
            </select>
          </div>
        </div>
        <div className="setup-field">
          <label className="setup-label" htmlFor="sm-method-eta">Delivery time</label>
          <input id="sm-method-eta" className="setup-input" value={f.deliveryEta} maxLength={60} placeholder="Within minutes" autoComplete="off"
            onChange={e => setForm(x => ({ ...x, deliveryEta: e.target.value }))} aria-describedby="sm-method-eta-hint" />
          <p id="sm-method-eta-hint" className="setup-hint">Shown under the method in WhatsApp.</p>
        </div>

        <fieldset className="sm-fieldset">
          <legend className="setup-label">Fee ({corridor.sourceCurrency})</legend>
          <div className="setup-field-row">
            <div className="setup-field">
              <label className="setup-label" htmlFor="sm-fee-fixed">Fixed</label>
              <input id="sm-fee-fixed" className="setup-input" type="number" min={0} step="0.01" inputMode="decimal" value={f.fixedFee}
                onChange={e => setForm(x => ({ ...x, fixedFee: e.target.value }))} />
            </div>
            <div className="setup-field">
              <label className="setup-label" htmlFor="sm-fee-percent">Percent</label>
              <input id="sm-fee-percent" className="setup-input" type="number" min={0} max={100} step="0.01" inputMode="decimal" value={f.percentFee}
                onChange={e => setForm(x => ({ ...x, percentFee: e.target.value }))} />
            </div>
          </div>
          <div className="setup-field-row">
            <div className="setup-field">
              <label className="setup-label" htmlFor="sm-fee-min">Minimum</label>
              <input id="sm-fee-min" className="setup-input" type="number" min={0} step="0.01" inputMode="decimal" value={f.minFee} placeholder="None"
                onChange={e => setForm(x => ({ ...x, minFee: e.target.value }))} />
            </div>
            <div className="setup-field">
              <label className="setup-label" htmlFor="sm-fee-max">Maximum</label>
              <input id="sm-fee-max" className="setup-input" type="number" min={0} step="0.01" inputMode="decimal" value={f.maxFee} placeholder="None"
                onChange={e => setForm(x => ({ ...x, maxFee: e.target.value }))} />
            </div>
          </div>
          <div className="setup-field-row">
            <div className="setup-field">
              <label className="setup-label" htmlFor="sm-fee-first">First-transfer fee</label>
              <input id="sm-fee-first" className="setup-input" type="number" min={0} step="0.01" inputMode="decimal" value={f.firstTransferFee} placeholder="Same as usual"
                onChange={e => setForm(x => ({ ...x, firstTransferFee: e.target.value }))} />
            </div>
            <div className="setup-field">
              <CountedLabel htmlFor="sm-fee-promo" text="Badge" value={f.promoLabel} max={20} />
              <input id="sm-fee-promo" className="setup-input" value={f.promoLabel} maxLength={40} placeholder="PROMO" autoComplete="off"
                onChange={e => setForm(x => ({ ...x, promoLabel: e.target.value }))} />
            </div>
          </div>
          <p className="setup-hint">The fee is fixed + percent of the amount, kept between the minimum and maximum. A customer's first paid transfer uses the first-transfer fee instead.</p>
        </fieldset>

        <div className="setup-field-row">
          <div className="setup-toggle-item sm-grow">
            <div>
              <span className="setup-toggle-label">Offered in WhatsApp</span>
            </div>
            <Toggle checked={f.isActive} onChange={on => setForm(x => ({ ...x, isActive: on }))} ariaLabel="Offered in WhatsApp" />
          </div>
          <div className="setup-field setup-field-narrow">
            <label className="setup-label" htmlFor="sm-method-sort">Order</label>
            <input id="sm-method-sort" className="setup-input" type="number" value={f.sortOrder} onChange={e => setForm(x => ({ ...x, sortOrder: e.target.value }))} />
          </div>
        </div>
      </FormModal>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Payout Method"
        message={`Delete ${deleteTarget?.name} with its recipient fields and banks? Methods that already have transfers can only be switched off.`}
        confirmText="Delete Method"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </>
  )
}
