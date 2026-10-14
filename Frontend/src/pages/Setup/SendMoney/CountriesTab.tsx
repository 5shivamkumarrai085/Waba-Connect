import React, { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { Plus, Globe2, ChevronRight, Gauge } from 'lucide-react'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { StatusBadge } from '../../../components/StatusBadge/StatusBadge'
import { Toggle } from '../../../components/Toggle/Toggle'
import Can from '../../../components/Can/Can'
import {
  remittanceService,
  formatMoney,
  formatRate,
  type Corridor,
  type RatePreview,
  type RemittanceReference,
  type SaveCorridorInput
} from '../../../services/remittance/remittanceService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { formatAbsoluteDateTime } from '../../../utils/dateHelper'
import { ConfigTable, FormModal, fromNumber, toNumber } from './ConfigTable'
import { PayoutMethodsView } from './PayoutMethodsView'

interface CorridorForm {
  sourceCountryCode: string
  sourceCurrency: string
  destinationCountryCode: string
  destinationCountryName: string
  destinationCurrency: string
  destinationDecimals: string
  minSendAmount: string
  maxSendAmount: string
  fxMarginBps: string
  isActive: boolean
  sortOrder: string
}

const emptyForm: CorridorForm = {
  sourceCountryCode: '', sourceCurrency: '', destinationCountryCode: '', destinationCountryName: '', destinationCurrency: '',
  destinationDecimals: '2', minSendAmount: '', maxSendAmount: '', fxMarginBps: '0', isActive: true, sortOrder: '0'
}

const toForm = (c: Corridor): CorridorForm => ({
  sourceCountryCode: c.sourceCountryCode, sourceCurrency: c.sourceCurrency, destinationCountryCode: c.destinationCountryCode,
  destinationCountryName: c.destinationCountryName, destinationCurrency: c.destinationCurrency, destinationDecimals: fromNumber(c.destinationDecimals),
  minSendAmount: fromNumber(c.minSendAmount), maxSendAmount: fromNumber(c.maxSendAmount), fxMarginBps: fromNumber(c.fxMarginBps),
  isActive: c.isActive, sortOrder: fromNumber(c.sortOrder)
})

const toInput = (f: CorridorForm): SaveCorridorInput => ({
  sourceCountryCode: f.sourceCountryCode.trim().toUpperCase(),
  sourceCurrency: f.sourceCurrency.trim().toUpperCase(),
  destinationCountryCode: f.destinationCountryCode.trim().toUpperCase(),
  destinationCountryName: f.destinationCountryName.trim(),
  destinationCurrency: f.destinationCurrency.trim().toUpperCase(),
  destinationDecimals: toNumber(f.destinationDecimals) ?? 2,
  minSendAmount: toNumber(f.minSendAmount) ?? 0,
  maxSendAmount: toNumber(f.maxSendAmount) ?? 0,
  fxMarginBps: toNumber(f.fxMarginBps) ?? 0,
  isActive: f.isActive,
  sortOrder: toNumber(f.sortOrder) ?? 0
})

/** Setup › Send Money › Countries: routes, then their payout methods, then recipient fields and banks. */
export const CountriesTab: React.FC<{ reference: RemittanceReference; canManage: boolean }> = ({ reference, canManage }) => {
  const [corridors, setCorridors] = useState<Corridor[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [selected, setSelected] = useState<Corridor | null>(null)

  const [editing, setEditing] = useState<Corridor | null>(null)
  const [isFormOpen, setIsFormOpen] = useState(false)
  const [form, setForm] = useState<CorridorForm>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<Corridor | null>(null)
  const [rate, setRate] = useState<{ corridor: Corridor; preview: RatePreview | null; error: string | null } | null>(null)

  const load = useCallback(async () => {
    setIsLoading(true)
    setLoadError(null)
    try {
      const list = await remittanceService.corridors()
      setCorridors(list)
      setSelected(s => (s ? list.find(c => c.id === s.id) ?? null : null))
    } catch (error) {
      setLoadError(getErrorMessage(error, 'Countries could not be loaded.'))
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => { void load() }, [load])

  const openCreate = () => {
    // New routes usually share the sending side of the existing ones.
    const first = corridors[0]
    setEditing(null)
    setForm({ ...emptyForm, sourceCountryCode: first?.sourceCountryCode ?? '', sourceCurrency: first?.sourceCurrency ?? '' })
    setFormError(null)
    setIsFormOpen(true)
  }

  const openEdit = (c: Corridor) => {
    setEditing(c)
    setForm(toForm(c))
    setFormError(null)
    setIsFormOpen(true)
  }

  const submit = async () => {
    setIsSubmitting(true)
    setFormError(null)
    try {
      const saved = await remittanceService.saveCorridor(editing?.id ?? null, toInput(form))
      toast.success(editing ? `${saved.destinationCountryName} saved.` : `${saved.destinationCountryName} added. Add its payout methods next.`)
      setIsFormOpen(false)
      await load()
    } catch (error) {
      setFormError(getErrorMessage(error, 'The country could not be saved.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await remittanceService.deleteCorridor(deleteTarget.id)
      toast.success(`${deleteTarget.destinationCountryName} deleted.`)
      void load()
    } catch (error) {
      toast.error(getErrorMessage(error, 'The country could not be deleted.'))
    } finally {
      setDeleteTarget(null)
    }
  }

  const setActive = async (c: Corridor, isActive: boolean) => {
    try {
      await remittanceService.saveCorridor(c.id, { ...toInput(toForm(c)), isActive })
      toast.success(`${c.destinationCountryName} ${isActive ? 'is now offered' : 'is no longer offered'} in WhatsApp.`)
      void load()
    } catch (error) {
      toast.error(getErrorMessage(error, 'The country could not be updated.'))
    }
  }

  const checkRate = async (c: Corridor) => {
    setRate({ corridor: c, preview: null, error: null })
    try {
      setRate({ corridor: c, preview: await remittanceService.previewRate(c.id), error: null })
    } catch (error) {
      setRate({ corridor: c, preview: null, error: getErrorMessage(error, 'The rate could not be checked.') })
    }
  }

  if (selected) {
    return (
      <PayoutMethodsView
        corridor={selected}
        reference={reference}
        canManage={canManage}
        onBack={() => { setSelected(null); void load() }}
      />
    )
  }

  const f = form
  const canSubmit = [f.sourceCountryCode, f.sourceCurrency, f.destinationCountryCode, f.destinationCountryName, f.destinationCurrency, f.minSendAmount, f.maxSendAmount]
    .every(v => v.trim() !== '')

  return (
    <>
      <div className="contacts-toolbar">
        <Can permission="Remittance.Manage">
          <button type="button" className="btn-toolbar-primary" onClick={openCreate}>
            <Plus size={15} aria-hidden="true" /><span>New Country</span>
          </button>
        </Can>
      </div>

      <div className="contacts-card">
        <ConfigTable<Corridor>
          rows={corridors}
          isLoading={isLoading}
          loadError={loadError}
          onRetry={() => void load()}
          rowLabel={c => c.destinationCountryName}
          empty={{
            iconName: 'Globe',
            title: 'No countries yet',
            message: 'Add a destination country, then the ways money can be paid out there.',
            action: canManage ? { label: 'New Country', onClick: openCreate } : undefined
          }}
          actions={[
            { label: 'Payout Methods', onSelect: c => setSelected(c) },
            { label: 'Check Live Rate', onSelect: c => void checkRate(c) },
            { label: 'Edit', onSelect: openEdit, disabled: () => !canManage },
            { label: 'Delete', onSelect: setDeleteTarget, destructive: true, disabled: () => !canManage }
          ]}
          columns={[
            {
              header: 'Destination',
              cell: c => (
                <button type="button" className="setup-user-name sm-drill" onClick={() => setSelected(c)}>
                  <span>{c.destinationCountryName}</span>
                  <span className="sm-code" translate="no">{c.sourceCountryCode} → {c.destinationCountryCode}</span>
                  <ChevronRight size={14} aria-hidden="true" />
                </button>
              )
            },
            { header: 'Currencies', cell: c => <span translate="no">{c.sourceCurrency} → {c.destinationCurrency}</span> },
            { header: 'Send limits', cell: c => `${formatMoney(c.sourceCurrency, c.minSendAmount)} – ${formatMoney(c.sourceCurrency, c.maxSendAmount)}` },
            { header: 'FX margin', className: 'text-right sm-num', cell: c => `${(c.fxMarginBps / 100).toFixed(2)}%` },
            {
              header: 'Payout methods',
              className: 'text-right sm-num',
              cell: c => c.payoutMethodCount === 0 ? <StatusBadge type="warning" text="None — hidden" /> : c.payoutMethodCount
            },
            {
              header: 'Offered',
              className: 'text-center',
              cell: c => (
                <Toggle checked={c.isActive} onChange={on => setActive(c, on)} disabled={!canManage}
                  ariaLabel={`${c.destinationCountryName} is ${c.isActive ? 'offered' : 'not offered'}`}
                  disabledReason="You can view Send Money settings but not change them." />
              )
            }
          ]}
        />
      </div>

      <FormModal
        isOpen={isFormOpen}
        onClose={() => setIsFormOpen(false)}
        title={editing ? `Edit ${editing.destinationCountryName}` : 'New Country'}
        subtitle="A route customers can send money on"
        icon={<Globe2 size={18} />}
        formId="sm-corridor-form"
        error={formError}
        isSubmitting={isSubmitting}
        canSubmit={canSubmit}
        submitLabel={editing ? 'Save Changes' : 'Add Country'}
        onSubmit={submit}
      >
        <fieldset className="sm-fieldset">
          <legend className="setup-label">Sending from</legend>
          <div className="setup-field-row">
            <div className="setup-field">
              <label className="setup-label required" htmlFor="sm-src-country">Country code</label>
              <input id="sm-src-country" className="setup-input" value={f.sourceCountryCode} maxLength={2} placeholder="MY" translate="no" autoComplete="off"
                onChange={e => setForm(x => ({ ...x, sourceCountryCode: e.target.value }))} data-autofocus />
            </div>
            <div className="setup-field">
              <label className="setup-label required" htmlFor="sm-src-currency">Currency</label>
              <input id="sm-src-currency" className="setup-input" value={f.sourceCurrency} maxLength={3} placeholder="MYR" translate="no" autoComplete="off"
                onChange={e => setForm(x => ({ ...x, sourceCurrency: e.target.value }))} />
            </div>
          </div>
        </fieldset>

        <fieldset className="sm-fieldset">
          <legend className="setup-label">Sending to</legend>
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-dst-name">Country name</label>
            <input id="sm-dst-name" className="setup-input" value={f.destinationCountryName} maxLength={60} placeholder="Indonesia" autoComplete="off"
              onChange={e => setForm(x => ({ ...x, destinationCountryName: e.target.value }))} aria-describedby="sm-dst-name-hint" />
            <p id="sm-dst-name-hint" className={`setup-hint${f.destinationCountryName.trim().length > 24 ? ' setup-hint-warning' : ''}`}>
              WhatsApp shows “{f.destinationCountryName.trim() || 'Name'} ({f.destinationCurrency.trim().toUpperCase() || 'CUR'})” — keep the name to 24 characters.
            </p>
          </div>
          <div className="setup-field-row">
            <div className="setup-field">
              <label className="setup-label required" htmlFor="sm-dst-country">Country code</label>
              <input id="sm-dst-country" className="setup-input" value={f.destinationCountryCode} maxLength={2} placeholder="ID" translate="no" autoComplete="off"
                onChange={e => setForm(x => ({ ...x, destinationCountryCode: e.target.value }))} />
            </div>
            <div className="setup-field">
              <label className="setup-label required" htmlFor="sm-dst-currency">Currency</label>
              <input id="sm-dst-currency" className="setup-input" value={f.destinationCurrency} maxLength={3} placeholder="IDR" translate="no" autoComplete="off"
                onChange={e => setForm(x => ({ ...x, destinationCurrency: e.target.value }))} />
            </div>
            <div className="setup-field">
              <label className="setup-label" htmlFor="sm-dst-decimals">Decimals</label>
              <input id="sm-dst-decimals" className="setup-input" type="number" min={0} max={4} value={f.destinationDecimals}
                onChange={e => setForm(x => ({ ...x, destinationDecimals: e.target.value }))} aria-describedby="sm-dst-decimals-hint" />
            </div>
          </div>
          <p id="sm-dst-decimals-hint" className="setup-hint">Minor units of the receive currency — 0 for IDR and VND. Amounts received are rounded down to these.</p>
        </fieldset>

        <div className="setup-field-row">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-min">Minimum send</label>
            <input id="sm-min" className="setup-input" type="number" min={0} step="0.01" inputMode="decimal" value={f.minSendAmount}
              onChange={e => setForm(x => ({ ...x, minSendAmount: e.target.value }))} />
          </div>
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-max">Maximum send</label>
            <input id="sm-max" className="setup-input" type="number" min={0} step="0.01" inputMode="decimal" value={f.maxSendAmount}
              onChange={e => setForm(x => ({ ...x, maxSendAmount: e.target.value }))} />
          </div>
        </div>

        <div className="setup-field-row">
          <div className="setup-field">
            <label className="setup-label" htmlFor="sm-margin">FX margin (basis points)</label>
            <input id="sm-margin" className="setup-input" type="number" min={0} max={2000} value={f.fxMarginBps}
              onChange={e => setForm(x => ({ ...x, fxMarginBps: e.target.value }))} aria-describedby="sm-margin-hint" />
            <p id="sm-margin-hint" className="setup-hint">100 bps = 1%. The customer's rate is the market rate less this margin.</p>
          </div>
          <div className="setup-field setup-field-narrow">
            <label className="setup-label" htmlFor="sm-sort">Order</label>
            <input id="sm-sort" className="setup-input" type="number" value={f.sortOrder} onChange={e => setForm(x => ({ ...x, sortOrder: e.target.value }))} />
          </div>
        </div>

        <div className="setup-toggle-item">
          <div>
            <span className="setup-toggle-label">Offered in WhatsApp</span>
            <p className="setup-hint">Only offered countries with at least one active payout method appear in the Flow.</p>
          </div>
          <Toggle checked={f.isActive} onChange={on => setForm(x => ({ ...x, isActive: on }))} ariaLabel="Offered in WhatsApp" />
        </div>
      </FormModal>

      <Modal
        isOpen={rate !== null}
        onClose={() => setRate(null)}
        title={rate ? `Live rate — ${rate.corridor.sourceCurrency} → ${rate.corridor.destinationCurrency}` : 'Live rate'}
        subtitle="What a customer would be quoted right now"
        icon={<Gauge size={18} />}
        size="sm"
      >
        {rate && !rate.preview && !rate.error && <p className="sm-muted" role="status">Checking the rate provider…</p>}
        {rate?.error && <div className="sm-form-error" role="alert">{rate.error}</div>}
        {rate?.preview && (rate.preview.ok ? (
          <dl className="sm-facts">
            <dt>Market rate</dt><dd className="sm-num">1 {rate.preview.from} = {formatRate(rate.preview.midRate ?? 0)} {rate.preview.to}</dd>
            <dt>Customer rate</dt><dd className="sm-num">1 {rate.preview.from} = {formatRate(rate.preview.appliedRate ?? 0)} {rate.preview.to}</dd>
            <dt>Provider</dt><dd translate="no">{rate.preview.provider}</dd>
            <dt>Published</dt><dd>{rate.preview.asOfUtc ? formatAbsoluteDateTime(rate.preview.asOfUtc) : '—'}</dd>
          </dl>
        ) : (
          <div className="sm-form-error" role="alert">{rate.preview.error ?? 'No rate is available.'} Customers on this route see this message instead of a price.</div>
        ))}
      </Modal>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Country"
        message={`Delete ${deleteTarget?.destinationCountryName} with its payout methods, fields and banks? Routes that already have transfers can only be switched off.`}
        confirmText="Delete Country"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </>
  )
}
