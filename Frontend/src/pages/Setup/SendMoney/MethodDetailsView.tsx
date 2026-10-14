import React, { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { Plus, ArrowLeft, TextCursorInput, Landmark } from 'lucide-react'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { StatusBadge } from '../../../components/StatusBadge/StatusBadge'
import { Toggle } from '../../../components/Toggle/Toggle'
import Can from '../../../components/Can/Can'
import {
  remittanceService,
  type BeneficiaryField,
  type Corridor,
  type FieldInputType,
  type Institution,
  type PayoutMethod,
  type RemittanceReference,
  type SaveBeneficiaryFieldInput
} from '../../../services/remittance/remittanceService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { ConfigTable, CountedLabel, FormModal, fromNumber, toNumber } from './ConfigTable'

interface FieldForm {
  key: string
  label: string
  inputType: FieldInputType
  isRequired: boolean
  minLength: string
  maxLength: string
  pattern: string
  helperText: string
  isAccountIdentifier: boolean
  isActive: boolean
  sortOrder: string
}

const toFieldForm = (f: BeneficiaryField | null, nextSort: number): FieldForm => ({
  key: f?.key ?? '',
  label: f?.label ?? '',
  inputType: f?.inputType ?? 'Text',
  isRequired: f?.isRequired ?? true,
  minLength: fromNumber(f?.minLength),
  maxLength: fromNumber(f?.maxLength),
  pattern: f?.pattern ?? '',
  helperText: f?.helperText ?? '',
  isAccountIdentifier: f?.isAccountIdentifier ?? false,
  isActive: f?.isActive ?? true,
  sortOrder: fromNumber(f?.sortOrder ?? nextSort)
})

const toFieldInput = (f: FieldForm): SaveBeneficiaryFieldInput => ({
  key: f.key.trim().toLowerCase(),
  label: f.label.trim(),
  inputType: f.inputType,
  isRequired: f.isRequired,
  minLength: toNumber(f.minLength),
  maxLength: toNumber(f.maxLength),
  pattern: f.pattern.trim() || null,
  helperText: f.helperText.trim() || null,
  isAccountIdentifier: f.isAccountIdentifier,
  isActive: f.isActive,
  sortOrder: toNumber(f.sortOrder) ?? 0
})

interface InstitutionForm { code: string; name: string; isActive: boolean; sortOrder: string }

interface Props {
  corridor: Corridor
  method: PayoutMethod
  reference: RemittanceReference
  canManage: boolean
  onBack: () => void
  onBackToCountries: () => void
}

/** A payout method's recipient form (the Flow's text slots) and its bank / provider list. */
export const MethodDetailsView: React.FC<Props> = ({ corridor, method, reference, canManage, onBack, onBackToCountries }) => {
  const maxSlots = reference.maxBeneficiaryFields

  const [fields, setFields] = useState<BeneficiaryField[]>([])
  const [fieldsLoading, setFieldsLoading] = useState(true)
  const [fieldsError, setFieldsError] = useState<string | null>(null)
  const [institutions, setInstitutions] = useState<Institution[]>([])
  const [banksLoading, setBanksLoading] = useState(true)
  const [banksError, setBanksError] = useState<string | null>(null)

  const [editingField, setEditingField] = useState<BeneficiaryField | null>(null)
  const [fieldFormOpen, setFieldFormOpen] = useState(false)
  const [fieldForm, setFieldForm] = useState<FieldForm>(toFieldForm(null, 0))
  const [editingBank, setEditingBank] = useState<Institution | null>(null)
  const [bankFormOpen, setBankFormOpen] = useState(false)
  const [bankForm, setBankForm] = useState<InstitutionForm>({ code: '', name: '', isActive: true, sortOrder: '0' })
  const [formError, setFormError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteField, setDeleteField] = useState<BeneficiaryField | null>(null)
  const [deleteBank, setDeleteBank] = useState<Institution | null>(null)

  const loadFields = useCallback(async () => {
    setFieldsLoading(true)
    setFieldsError(null)
    try { setFields(await remittanceService.fields(method.id)) }
    catch (error) { setFieldsError(getErrorMessage(error, 'Recipient fields could not be loaded.')) }
    finally { setFieldsLoading(false) }
  }, [method.id])

  const loadBanks = useCallback(async () => {
    setBanksLoading(true)
    setBanksError(null)
    try { setInstitutions(await remittanceService.institutions(method.id)) }
    catch (error) { setBanksError(getErrorMessage(error, 'Banks could not be loaded.')) }
    finally { setBanksLoading(false) }
  }, [method.id])

  useEffect(() => { void loadFields(); void loadBanks() }, [loadFields, loadBanks])

  const activeFields = fields.filter(f => f.isActive)
  const slotsFull = activeFields.length >= maxSlots

  const openField = (f: BeneficiaryField | null) => {
    setEditingField(f)
    setFieldForm(toFieldForm(f, (fields.at(-1)?.sortOrder ?? -1) + 1))
    setFormError(null)
    setFieldFormOpen(true)
  }

  const openBank = (i: Institution | null) => {
    setEditingBank(i)
    setBankForm({ code: i?.code ?? '', name: i?.name ?? '', isActive: i?.isActive ?? true, sortOrder: fromNumber(i?.sortOrder ?? institutions.length) })
    setFormError(null)
    setBankFormOpen(true)
  }

  const run = async (work: () => Promise<unknown>, success: string, failure: string, after: () => void) => {
    setIsSubmitting(true)
    setFormError(null)
    try {
      await work()
      toast.success(success)
      after()
    } catch (error) {
      setFormError(getErrorMessage(error, failure))
    } finally {
      setIsSubmitting(false)
    }
  }

  const submitField = () => run(
    () => remittanceService.saveField(method.id, editingField?.id ?? null, toFieldInput(fieldForm)),
    `${fieldForm.label.trim()} saved.`, 'The field could not be saved.',
    () => { setFieldFormOpen(false); void loadFields() })

  const submitBank = () => run(
    () => remittanceService.saveInstitution(method.id, editingBank?.id ?? null, {
      code: bankForm.code.trim(), name: bankForm.name.trim(), isActive: bankForm.isActive, sortOrder: toNumber(bankForm.sortOrder) ?? 0
    }),
    `${bankForm.name.trim()} saved.`, 'The bank could not be saved.',
    () => { setBankFormOpen(false); void loadBanks() })

  const toggleField = async (f: BeneficiaryField, isActive: boolean) => {
    try {
      await remittanceService.saveField(method.id, f.id, { ...toFieldInput(toFieldForm(f, 0)), isActive })
      void loadFields()
    } catch (error) {
      toast.error(getErrorMessage(error, 'The field could not be updated.'))
    }
  }

  const confirmDeleteField = async () => {
    if (!deleteField) return
    try { await remittanceService.deleteField(deleteField.id); toast.success(`${deleteField.label} deleted.`); void loadFields() }
    catch (error) { toast.error(getErrorMessage(error, 'The field could not be deleted.')) }
    finally { setDeleteField(null) }
  }

  const confirmDeleteBank = async () => {
    if (!deleteBank) return
    try { await remittanceService.deleteInstitution(deleteBank.id); toast.success(`${deleteBank.name} deleted.`); void loadBanks() }
    catch (error) { toast.error(getErrorMessage(error, 'The bank could not be deleted.')) }
    finally { setDeleteBank(null) }
  }

  const ff = fieldForm
  // Activating one more field than the Flow has slots for is refused by the server too.
  const wouldOverflow = ff.isActive && !(editingField?.isActive ?? false) && slotsFull
  const canSubmitField = ff.key.trim() !== '' && ff.label.trim() !== '' && ff.label.trim().length <= 20 && ff.helperText.length <= 80 && !wouldOverflow

  return (
    <>
      <nav className="sm-breadcrumb" aria-label="Breadcrumb">
        <button type="button" className="webhook-link-btn" onClick={onBackToCountries}><ArrowLeft size={14} aria-hidden="true" /> All countries</button>
        <span aria-hidden="true">/</span>
        <button type="button" className="webhook-link-btn" onClick={onBack}>{corridor.destinationCountryName}</button>
        <span aria-hidden="true">/</span>
        <span aria-current="page">{method.name}</span>
      </nav>

      <section className="sm-section" aria-labelledby="sm-fields-title">
        <div className="sm-section-head">
          <div>
            <h2 id="sm-fields-title">Recipient fields</h2>
            <p>What the customer types when adding a new recipient. WhatsApp's form has {maxSlots} slots, filled in this order.</p>
          </div>
          <Can permission="Remittance.Manage">
            <button type="button" className="btn-toolbar-primary" onClick={() => openField(null)}>
              <Plus size={15} aria-hidden="true" /><span>New Field</span>
            </button>
          </Can>
        </div>

        <ol className="sm-slots" aria-label="WhatsApp form slots">
          {Array.from({ length: maxSlots }, (_, i) => {
            const field = activeFields[i]
            return (
              <li key={i} className={field ? 'sm-slot sm-slot-used' : 'sm-slot'}>
                <span className="sm-slot-no">{i + 1}</span>
                <span>{field ? field.label : 'Empty'}</span>
                {field?.isRequired && <span className="sm-slot-req" aria-label="required">*</span>}
              </li>
            )
          })}
        </ol>

        <div className="contacts-card">
          <ConfigTable<BeneficiaryField>
            rows={fields}
            isLoading={fieldsLoading}
            loadError={fieldsError}
            onRetry={() => void loadFields()}
            rowLabel={f => f.label}
            empty={{
              iconName: 'TextCursorInput',
              title: 'No recipient fields',
              message: 'Add the details your payout partner needs, such as account number.',
              action: canManage ? { label: 'New Field', onClick: () => openField(null) } : undefined
            }}
            actions={[
              { label: 'Edit', onSelect: f => openField(f), disabled: () => !canManage },
              { label: 'Delete', onSelect: setDeleteField, destructive: true, disabled: () => !canManage }
            ]}
            columns={[
              { header: 'Label', cell: f => <span>{f.label} {f.isAccountIdentifier && <StatusBadge type="info" text="Account no." />}</span> },
              { header: 'Key', cell: f => <code translate="no" className="sm-code">{f.key}</code> },
              { header: 'Type', cell: f => f.inputType },
              { header: 'Required', cell: f => (f.isRequired ? 'Yes' : 'No') },
              {
                header: 'Rules',
                className: 'setup-truncate',
                title: f => f.pattern ?? undefined,
                cell: f => [
                  f.minLength !== null || f.maxLength !== null ? `${f.minLength ?? 0}–${f.maxLength ?? 80} chars` : null,
                  f.pattern ? 'Pattern' : null
                ].filter(Boolean).join(' · ') || '—'
              },
              {
                header: 'Shown',
                className: 'text-center',
                cell: f => (
                  <Toggle checked={f.isActive} onChange={on => toggleField(f, on)} disabled={!canManage || (!f.isActive && slotsFull)}
                    ariaLabel={`${f.label} is ${f.isActive ? 'shown' : 'hidden'}`}
                    disabledReason={!canManage ? 'You can view Send Money settings but not change them.' : `All ${maxSlots} slots are in use.`} />
                )
              }
            ]}
          />
        </div>
      </section>

      <section className="sm-section" aria-labelledby="sm-banks-title">
        <div className="sm-section-head">
          <div>
            <h2 id="sm-banks-title">Banks &amp; providers</h2>
            <p>The recipient picks one from this list. Leave it empty when the method doesn't need one.</p>
          </div>
          <Can permission="Remittance.Manage">
            <button type="button" className="btn-toolbar-primary" onClick={() => openBank(null)}>
              <Plus size={15} aria-hidden="true" /><span>New Bank</span>
            </button>
          </Can>
        </div>
        <div className="contacts-card">
          <ConfigTable<Institution>
            rows={institutions}
            isLoading={banksLoading}
            loadError={banksError}
            onRetry={() => void loadBanks()}
            rowLabel={i => i.name}
            empty={{ iconName: 'Landmark', title: 'No banks', message: 'Customers are not asked to choose a bank for this method.' }}
            actions={[
              { label: 'Edit', onSelect: i => openBank(i), disabled: () => !canManage },
              { label: 'Delete', onSelect: setDeleteBank, destructive: true, disabled: () => !canManage }
            ]}
            columns={[
              { header: 'Name', cell: i => i.name },
              { header: 'Code', cell: i => <code translate="no" className="sm-code">{i.code}</code> },
              { header: 'Order', className: 'text-right sm-num', cell: i => i.sortOrder },
              { header: 'Status', cell: i => <StatusBadge type={i.isActive ? 'success' : 'closed'} text={i.isActive ? 'Offered' : 'Hidden'} /> }
            ]}
          />
        </div>
      </section>

      <FormModal
        isOpen={fieldFormOpen}
        onClose={() => setFieldFormOpen(false)}
        title={editingField ? `Edit ${editingField.label}` : 'New Recipient Field'}
        subtitle={`${method.name} · ${corridor.destinationCountryName}`}
        icon={<TextCursorInput size={18} />}
        formId="sm-field-form"
        error={formError ?? (wouldOverflow ? `All ${maxSlots} WhatsApp slots are in use. Hide another field first, or save this one hidden.` : null)}
        isSubmitting={isSubmitting}
        canSubmit={canSubmitField}
        submitLabel={editingField ? 'Save Changes' : 'Add Field'}
        onSubmit={submitField}
      >
        <div className="setup-field">
          <CountedLabel htmlFor="sm-field-label" text="Label" value={ff.label} max={20} required />
          <input id="sm-field-label" className="setup-input" value={ff.label} maxLength={40} placeholder="Account number" autoComplete="off" data-autofocus
            onChange={e => setFieldForm(x => ({ ...x, label: e.target.value }))} aria-describedby="sm-field-label-hint" />
          <p id="sm-field-label-hint" className="setup-hint">WhatsApp cuts input labels at 20 characters.</p>
        </div>
        <div className="setup-field-row">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-field-key">Key</label>
            <input id="sm-field-key" className="setup-input" value={ff.key} maxLength={40} placeholder="account_number" translate="no" autoComplete="off"
              onChange={e => setFieldForm(x => ({ ...x, key: e.target.value }))} />
          </div>
          <div className="setup-field">
            <label className="setup-label" htmlFor="sm-field-type">Type</label>
            <select id="sm-field-type" className="setup-input" value={ff.inputType} onChange={e => setFieldForm(x => ({ ...x, inputType: e.target.value as FieldInputType }))}>
              {reference.fieldInputTypes.map(t => <option key={t} value={t}>{t}</option>)}
            </select>
          </div>
        </div>
        <div className="setup-field">
          <CountedLabel htmlFor="sm-field-helper" text="Help text" value={ff.helperText} max={80} />
          <input id="sm-field-helper" className="setup-input" value={ff.helperText} maxLength={120} placeholder="e.g. 10–16 digits" autoComplete="off"
            onChange={e => setFieldForm(x => ({ ...x, helperText: e.target.value }))} />
        </div>
        <div className="setup-field-row">
          <div className="setup-field">
            <label className="setup-label" htmlFor="sm-field-min">Min length</label>
            <input id="sm-field-min" className="setup-input" type="number" min={0} max={80} value={ff.minLength} placeholder="—"
              onChange={e => setFieldForm(x => ({ ...x, minLength: e.target.value }))} />
          </div>
          <div className="setup-field">
            <label className="setup-label" htmlFor="sm-field-max">Max length</label>
            <input id="sm-field-max" className="setup-input" type="number" min={1} max={80} value={ff.maxLength} placeholder="80"
              onChange={e => setFieldForm(x => ({ ...x, maxLength: e.target.value }))} />
          </div>
          <div className="setup-field setup-field-narrow">
            <label className="setup-label" htmlFor="sm-field-sort">Order</label>
            <input id="sm-field-sort" className="setup-input" type="number" value={ff.sortOrder} onChange={e => setFieldForm(x => ({ ...x, sortOrder: e.target.value }))} />
          </div>
        </div>
        <div className="setup-field">
          <label className="setup-label" htmlFor="sm-field-pattern">Pattern (regular expression)</label>
          <input id="sm-field-pattern" className="setup-input sm-mono" value={ff.pattern} maxLength={200} placeholder="[A-Z]{4}0[A-Z0-9]{6}" translate="no" spellCheck={false} autoComplete="off"
            onChange={e => setFieldForm(x => ({ ...x, pattern: e.target.value }))} aria-describedby="sm-field-pattern-hint" />
          <p id="sm-field-pattern-hint" className="setup-hint">The whole value must match. Checked on the server when the customer saves the recipient.</p>
        </div>
        <div className="setup-toggle-row">
          <div className="setup-toggle-item">
            <span className="setup-toggle-label">Required</span>
            <Toggle checked={ff.isRequired} onChange={on => setFieldForm(x => ({ ...x, isRequired: on }))} ariaLabel="Required" />
          </div>
          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Account number</span>
              <p className="setup-hint">Its last four digits identify the recipient in lists (****1234).</p>
            </div>
            <Toggle checked={ff.isAccountIdentifier} onChange={on => setFieldForm(x => ({ ...x, isAccountIdentifier: on }))} ariaLabel="Account number" />
          </div>
          <div className="setup-toggle-item">
            <span className="setup-toggle-label">Shown in WhatsApp</span>
            <Toggle checked={ff.isActive} onChange={on => setFieldForm(x => ({ ...x, isActive: on }))} ariaLabel="Shown in WhatsApp" />
          </div>
        </div>
      </FormModal>

      <FormModal
        isOpen={bankFormOpen}
        onClose={() => setBankFormOpen(false)}
        title={editingBank ? `Edit ${editingBank.name}` : 'New Bank or Provider'}
        subtitle={`${method.name} · ${corridor.destinationCountryName}`}
        icon={<Landmark size={18} />}
        formId="sm-bank-form"
        error={formError}
        isSubmitting={isSubmitting}
        canSubmit={bankForm.code.trim() !== '' && bankForm.name.trim() !== '' && bankForm.name.trim().length <= 30}
        submitLabel={editingBank ? 'Save Changes' : 'Add Bank'}
        onSubmit={submitBank}
      >
        <div className="setup-field">
          <CountedLabel htmlFor="sm-bank-name" text="Name" value={bankForm.name} max={30} required />
          <input id="sm-bank-name" className="setup-input" value={bankForm.name} maxLength={60} placeholder="Bank Central Asia" autoComplete="off" data-autofocus
            onChange={e => setBankForm(x => ({ ...x, name: e.target.value }))} />
        </div>
        <div className="setup-field-row">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-bank-code">Code</label>
            <input id="sm-bank-code" className="setup-input" value={bankForm.code} maxLength={40} placeholder="BCA" translate="no" autoComplete="off"
              onChange={e => setBankForm(x => ({ ...x, code: e.target.value }))} aria-describedby="sm-bank-code-hint" />
            <p id="sm-bank-code-hint" className="setup-hint">Sent to the payout partner.</p>
          </div>
          <div className="setup-field setup-field-narrow">
            <label className="setup-label" htmlFor="sm-bank-sort">Order</label>
            <input id="sm-bank-sort" className="setup-input" type="number" value={bankForm.sortOrder} onChange={e => setBankForm(x => ({ ...x, sortOrder: e.target.value }))} />
          </div>
        </div>
        <div className="setup-toggle-item">
          <span className="setup-toggle-label">Offered in WhatsApp</span>
          <Toggle checked={bankForm.isActive} onChange={on => setBankForm(x => ({ ...x, isActive: on }))} ariaLabel="Offered in WhatsApp" />
        </div>
      </FormModal>

      <ConfirmationModal
        isOpen={deleteField !== null}
        title="Delete Field"
        message={`Delete “${deleteField?.label}”? Recipients already saved keep the value they were given.`}
        confirmText="Delete Field"
        isDestructive
        onConfirm={confirmDeleteField}
        onCancel={() => setDeleteField(null)}
      />
      <ConfirmationModal
        isOpen={deleteBank !== null}
        title="Delete Bank"
        message={`Delete ${deleteBank?.name}? Banks used by saved recipients can only be hidden.`}
        confirmText="Delete Bank"
        isDestructive
        onConfirm={confirmDeleteBank}
        onCancel={() => setDeleteBank(null)}
      />
    </>
  )
}
