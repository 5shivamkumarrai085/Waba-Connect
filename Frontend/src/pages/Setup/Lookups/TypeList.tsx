import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Plus, RefreshCw, MoreVertical, Lock } from 'lucide-react'
import { Menu, MenuItem } from '../../../components/Menu/Menu'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import ColorPicker from '../../../components/ColorPicker/ColorPicker'
import Can from '../../../components/Can/Can'
import usePermission from '../../../hooks/usePermission'
import { lookupService, type ContactTypeLookup } from '../../../services/setup/lookupService'
import { useLookupStore } from '../../../store/lookupStore'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'

const FORM_ID = 'type-form'

export const TypeList: React.FC = () => {
  const { has } = usePermission()

  const [types, setTypes] = useState<ContactTypeLookup[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)
  const [editing, setEditing] = useState<ContactTypeLookup | null>(null)
  const [isModalOpen, setIsModalOpen] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<ContactTypeLookup | null>(null)

  const [name, setName] = useState('')
  const [color, setColor] = useState('#6366F1')
  const [isActive, setIsActive] = useState(true)

  const load = async (showSpinner = true) => {
    if (showSpinner) setIsLoading(true)
    try {
      setTypes(await lookupService.getTypes())
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load types.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  const openCreate = () => {
    setEditing(null)
    setName('')
    setColor('#22C55E')
    setIsActive(true)
    setIsModalOpen(true)
  }

  const openEdit = (type: ContactTypeLookup) => {
    setEditing(type)
    setName(type.name)
    setColor(type.color || '#6B7280')
    setIsActive(type.isActive)
    setIsModalOpen(true)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSubmitting(true)
    try {
      const payload = { name: name.trim(), color, isActive, sortOrder: editing?.sortOrder ?? types.length }
      if (editing) {
        await lookupService.updateType(editing.id, payload)
        toast.success('Type updated.')
      } else {
        await lookupService.createType(payload)
        toast.success('Type created.')
      }
      setIsModalOpen(false)
      void load(false)
      // Contacts and Chat read types from the shared lookup store. Without this, a type created
      // or renamed here would not reach an already-open Contacts tab until a full reload.
      void useLookupStore.getState().invalidate()
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to save the type.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await lookupService.deleteType(deleteTarget.id)
      toast.success(`Type '${deleteTarget.name}' deleted.`)
      void load(false)
      // Contacts and Chat read types from the shared lookup store. Without this, a type created
      // or renamed here would not reach an already-open Contacts tab until a full reload.
      void useLookupStore.getState().invalidate()
    } catch (error) {
      // Expected: built-in types, and types still used by contacts.
      toast.error(getErrorMessage(error, 'Failed to delete the type.'))
    } finally {
      setDeleteTarget(null)
    }
  }

  const canEdit = has('ContactType.Edit')
  const canDelete = has('ContactType.Delete')

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Type</h1>
        <p>Define the categories a contact can be classified as.</p>
      </div>

      <div className="contacts-toolbar">
        <Can permission="ContactType.Create">
          <button type="button" className="btn-toolbar-primary" onClick={openCreate}>
            <Plus size={15} />
            <span>New Type</span>
          </button>
        </Can>
        <button type="button" className="btn-toolbar-tertiary" onClick={() => load()}>
          <RefreshCw size={15} />
          <span>Refresh</span>
        </button>
      </div>

      <div className="contacts-card">
        <div className="data-table-wrapper">
          {isLoading ? (
            <Skeleton variant="table" />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th className="actions-col">ACTIONS</th>
                  <th>ID</th>
                  <th>NAME</th>
                  <th>COLOR</th>
                  <th className="text-center">IN USE</th>
                  <th className="text-center">ACTIVE</th>
                </tr>
              </thead>
              <tbody>
                {types.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="no-records-row">
                      <EmptyState
                        iconName="Tags"
                        title="No types yet"
                        message="Add a type to start categorising contacts."
                        action={has('ContactType.Create') ? { label: 'New Type', onClick: openCreate } : undefined}
                      />
                    </td>
                  </tr>
                ) : (
                  types.map((type) => (
                    <tr key={type.id}>
                      <td className="actions-col">
                        <div className="contact-actions-menu-wrapper">
                          <Menu
                            open={openMenuId === type.id}
                            onOpenChange={(isOpen) => setOpenMenuId(isOpen ? type.id : null)}
                            align="start"
                            offset={4}
                            className="contact-actions-dropdown"
                            ariaLabel="Row actions"
                            trigger={(props) => (
                              <button {...props} type="button" className="contact-actions-trigger" aria-label="Row actions">
                                <MoreVertical size={16} />
                              </button>
                            )}
                          >
                            <MenuItem className="contact-actions-item" disabled={!canEdit} onSelect={() => openEdit(type)}>
                              Edit
                            </MenuItem>
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              disabled={!canDelete || type.isSystem}
                              onSelect={() => setDeleteTarget(type)}
                            >
                              Delete
                            </MenuItem>
                          </Menu>
                        </div>
                      </td>
                      <td>{type.id}</td>
                      <td>
                        <div className="setup-role-name-cell">
                          <button
                            type="button"
                            className="setup-user-name"
                            onClick={() => canEdit && openEdit(type)}
                            disabled={!canEdit}
                          >
                            {type.name}
                          </button>
                          {type.isSystem && (
                            <span className="setup-role-badge muted" title="Built-in — cannot be deleted">
                              <Lock size={11} />
                              Built-in
                            </span>
                          )}
                        </div>
                      </td>
                      <td>
                        <div className="setup-color-cell">
                          <span className="setup-color-dot" style={{ backgroundColor: type.color || '#6B7280' }} />
                          <code>{type.color || '—'}</code>
                        </div>
                      </td>
                      {/* Surfaced so an admin can see why a delete will be refused before trying. */}
                      <td className="text-center">{type.usageCount}</td>
                      <td className="text-center">
                        <span className={`setup-role-badge ${type.isActive ? 'admin' : 'muted'}`}>
                          {type.isActive ? 'Active' : 'Hidden'}
                        </span>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          )}
        </div>
      </div>

      <Modal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title={editing ? 'Edit Type' : 'New Type'}
        size="sm"
        footer={
          <>
            <button type="button" className="oc-dialog-btn oc-dialog-btn-secondary" onClick={() => setIsModalOpen(false)}>
              Cancel
            </button>
            <button type="submit" form={FORM_ID} className="oc-dialog-btn oc-dialog-btn-primary" disabled={isSubmitting || !name.trim()}>
              {isSubmitting ? 'Saving…' : 'Submit'}
            </button>
          </>
        }
      >
        <form id={FORM_ID} onSubmit={handleSubmit} className="setup-modal-form">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="type-name">Name</label>
            <input
              id="type-name"
              className="setup-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              data-autofocus
              required
            />
            {editing && (
              <p className="setup-hint">
                Stored value stays <code>{editing.value}</code>. Renaming only changes the label —
                no contacts are modified.
              </p>
            )}
          </div>

          <div className="setup-field">
            <label className="setup-label required">Color</label>
            <ColorPicker value={color} onChange={setColor} />
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Active</span>
              <p className="setup-hint">Hidden types stay on existing contacts but leave the picker.</p>
            </div>
            <button
              type="button"
              className={`setup-switch ${isActive ? 'on' : 'off'}`}
              onClick={() => setIsActive((prev) => !prev)}
              aria-pressed={isActive}
              aria-label="Active"
            >
              <span className="setup-switch-knob" />
            </button>
          </div>
        </form>
      </Modal>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Type"
        message={`Delete the type '${deleteTarget?.name}'? This cannot be undone.`}
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default TypeList
