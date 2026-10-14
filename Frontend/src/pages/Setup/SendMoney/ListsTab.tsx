import React, { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { Plus, ListChecks } from 'lucide-react'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { StatusBadge } from '../../../components/StatusBadge/StatusBadge'
import { Toggle } from '../../../components/Toggle/Toggle'
import Can from '../../../components/Can/Can'
import { remittanceService, humanize, type Lookup, type RemittanceReference } from '../../../services/remittance/remittanceService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { ConfigTable, CountedLabel, FormModal, fromNumber, toNumber } from './ConfigTable'

/** What each list is for, shown above it. Keyed by the category the server reports. */
const purposeOf: Record<string, string> = {
  purpose: 'Why the customer is sending money. Asked on the regulatory-details step and kept with the transfer for AML reporting.',
  source_of_funds: 'Where the money comes from. Asked on the regulatory-details step and kept with the transfer for AML reporting.',
  relationship: 'How the customer knows the recipient. Asked when adding a recipient; leave empty to skip the question.'
}

interface LookupForm { code: string; label: string; isActive: boolean; sortOrder: string }

/** Setup › Send Money › Lists: purpose, source of funds, relationship. */
export const ListsTab: React.FC<{ reference: RemittanceReference; canManage: boolean }> = ({ reference, canManage }) => {
  const [category, setCategory] = useState(reference.lookupCategories[0] ?? '')
  const [items, setItems] = useState<Lookup[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [editing, setEditing] = useState<Lookup | null>(null)
  const [isFormOpen, setIsFormOpen] = useState(false)
  const [form, setForm] = useState<LookupForm>({ code: '', label: '', isActive: true, sortOrder: '0' })
  const [formError, setFormError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<Lookup | null>(null)

  const load = useCallback(async () => {
    if (!category) return
    setIsLoading(true)
    setLoadError(null)
    try { setItems(await remittanceService.lookups(category)) }
    catch (error) { setLoadError(getErrorMessage(error, 'The list could not be loaded.')) }
    finally { setIsLoading(false) }
  }, [category])

  useEffect(() => { void load() }, [load])

  const open = (l: Lookup | null) => {
    setEditing(l)
    setForm({ code: l?.code ?? '', label: l?.label ?? '', isActive: l?.isActive ?? true, sortOrder: fromNumber(l?.sortOrder ?? items.length) })
    setFormError(null)
    setIsFormOpen(true)
  }

  const save = async (id: number | null, f: LookupForm) =>
    remittanceService.saveLookup(category, id, { code: f.code.trim().toLowerCase(), label: f.label.trim(), isActive: f.isActive, sortOrder: toNumber(f.sortOrder) ?? 0 })

  const submit = async () => {
    setIsSubmitting(true)
    setFormError(null)
    try {
      const saved = await save(editing?.id ?? null, form)
      toast.success(`${saved.label} saved.`)
      setIsFormOpen(false)
      void load()
    } catch (error) {
      setFormError(getErrorMessage(error, 'The item could not be saved.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const setActive = async (l: Lookup, isActive: boolean) => {
    try {
      await save(l.id, { code: l.code, label: l.label, isActive, sortOrder: String(l.sortOrder) })
      void load()
    } catch (error) {
      toast.error(getErrorMessage(error, 'The item could not be updated.'))
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try { await remittanceService.deleteLookup(deleteTarget.id); toast.success(`${deleteTarget.label} deleted.`); void load() }
    catch (error) { toast.error(getErrorMessage(error, 'The item could not be deleted.')) }
    finally { setDeleteTarget(null) }
  }

  const listName = humanize(category)

  return (
    <>
      <div className="sm-list-switch" role="radiogroup" aria-label="List">
        {reference.lookupCategories.map(c => (
          <button key={c} type="button" role="radio" aria-checked={c === category}
            className={`sm-chip${c === category ? ' sm-chip-active' : ''}`} onClick={() => setCategory(c)}>
            {humanize(c)}
          </button>
        ))}
      </div>
      {purposeOf[category] && <p className="sm-muted sm-list-purpose">{purposeOf[category]}</p>}

      <div className="contacts-toolbar">
        <Can permission="Remittance.Manage">
          <button type="button" className="btn-toolbar-primary" onClick={() => open(null)}>
            <Plus size={15} aria-hidden="true" /><span>New Item</span>
          </button>
        </Can>
      </div>

      <div className="contacts-card">
        <ConfigTable<Lookup>
          rows={items}
          isLoading={isLoading}
          loadError={loadError}
          onRetry={() => void load()}
          rowLabel={l => l.label}
          empty={{
            iconName: 'ListChecks',
            title: `No ${listName.toLowerCase()} options`,
            message: category === 'relationship' ? 'Customers are not asked about their relationship to the recipient.' : 'Customers cannot finish a transfer until this list has options.',
            action: canManage ? { label: 'New Item', onClick: () => open(null) } : undefined
          }}
          actions={[
            { label: 'Edit', onSelect: l => open(l), disabled: () => !canManage },
            { label: 'Delete', onSelect: setDeleteTarget, destructive: true, disabled: () => !canManage }
          ]}
          columns={[
            { header: 'Label', cell: l => l.label },
            { header: 'Code', cell: l => <code translate="no" className="sm-code">{l.code}</code> },
            { header: 'Order', className: 'text-right sm-num', cell: l => l.sortOrder },
            {
              header: 'Offered',
              className: 'text-center',
              cell: l => (
                <Toggle checked={l.isActive} onChange={on => setActive(l, on)} disabled={!canManage}
                  ariaLabel={`${l.label} is ${l.isActive ? 'offered' : 'hidden'}`} disabledReason="You can view Send Money settings but not change them." />
              )
            }
          ]}
        />
      </div>
      {items.length >= 200 && <StatusBadge type="warning" text="WhatsApp lists show at most 200 options" />}

      <FormModal
        isOpen={isFormOpen}
        onClose={() => setIsFormOpen(false)}
        title={editing ? `Edit ${editing.label}` : `New ${listName} Option`}
        subtitle={listName}
        icon={<ListChecks size={18} />}
        formId="sm-lookup-form"
        error={formError}
        isSubmitting={isSubmitting}
        canSubmit={form.code.trim() !== '' && form.label.trim() !== '' && form.label.trim().length <= 30}
        submitLabel={editing ? 'Save Changes' : 'Add Option'}
        onSubmit={submit}
      >
        <div className="setup-field">
          <CountedLabel htmlFor="sm-lookup-label" text="Label" value={form.label} max={30} required />
          <input id="sm-lookup-label" className="setup-input" value={form.label} maxLength={60} autoComplete="off" data-autofocus
            onChange={e => setForm(x => ({ ...x, label: e.target.value }))} />
        </div>
        <div className="setup-field-row">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="sm-lookup-code">Code</label>
            <input id="sm-lookup-code" className="setup-input" value={form.code} maxLength={40} placeholder="family_support" translate="no" autoComplete="off"
              onChange={e => setForm(x => ({ ...x, code: e.target.value }))} aria-describedby="sm-lookup-code-hint" />
            <p id="sm-lookup-code-hint" className="setup-hint">Stored on transfers and used in reports. Lower-case letters, digits and underscores.</p>
          </div>
          <div className="setup-field setup-field-narrow">
            <label className="setup-label" htmlFor="sm-lookup-sort">Order</label>
            <input id="sm-lookup-sort" className="setup-input" type="number" value={form.sortOrder} onChange={e => setForm(x => ({ ...x, sortOrder: e.target.value }))} />
          </div>
        </div>
        <div className="setup-toggle-item">
          <span className="setup-toggle-label">Offered in WhatsApp</span>
          <Toggle checked={form.isActive} onChange={on => setForm(x => ({ ...x, isActive: on }))} ariaLabel="Offered in WhatsApp" />
        </div>
      </FormModal>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Option"
        message={`Delete “${deleteTarget?.label}”? Transfers that used it keep its code.`}
        confirmText="Delete Option"
        isDestructive
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </>
  )
}
