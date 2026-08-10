import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Plus, RefreshCw, MoreVertical, Lock } from 'lucide-react'
import { Menu, MenuItem } from '../../../components/Menu/Menu'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import Can from '../../../components/Can/Can'
import usePermission from '../../../hooks/usePermission'
import { lookupService, type ContactSourceLookup } from '../../../services/setup/lookupService'
import { useLookupStore } from '../../../store/lookupStore'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'

const FORM_ID = 'source-form'

export const SourceList: React.FC = () => {
  const { has } = usePermission()

  const [sources, setSources] = useState<ContactSourceLookup[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)
  const [editing, setEditing] = useState<ContactSourceLookup | null>(null)
  const [isModalOpen, setIsModalOpen] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<ContactSourceLookup | null>(null)

  const [name, setName] = useState('')
  const [isActive, setIsActive] = useState(true)

  const load = async (showSpinner = true) => {
    if (showSpinner) setIsLoading(true)
    try {
      setSources(await lookupService.getSources())
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load sources.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  const openCreate = () => {
    setEditing(null)
    setName('')
    setIsActive(true)
    setIsModalOpen(true)
  }

  const openEdit = (source: ContactSourceLookup) => {
    setEditing(source)
    setName(source.name)
    setIsActive(source.isActive)
    setIsModalOpen(true)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSubmitting(true)
    try {
      const payload = { name: name.trim(), isActive, sortOrder: editing?.sortOrder ?? sources.length }
      if (editing) {
        await lookupService.updateSource(editing.id, payload)
        toast.success('Source updated.')
      } else {
        await lookupService.createSource(payload)
        toast.success('Source created.')
      }
      setIsModalOpen(false)
      void load(false)
      // Keeps an already-open Contacts list in step with a source added or recoloured here.
      void useLookupStore.getState().invalidate()
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to save the source.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await lookupService.deleteSource(deleteTarget.id)
      toast.success(`Source '${deleteTarget.name}' deleted.`)
      void load(false)
      // Keeps an already-open Contacts list in step with a source added or recoloured here.
      void useLookupStore.getState().invalidate()
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to delete the source.'))
    } finally {
      setDeleteTarget(null)
    }
  }

  const canEdit = has('Source.Edit')
  const canDelete = has('Source.Delete')

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Source</h1>
        <p>Track where your contacts originally came from.</p>
      </div>

      <div className="contacts-toolbar">
        <Can permission="Source.Create">
          <button type="button" className="btn-toolbar-primary" onClick={openCreate}>
            <Plus size={15} />
            <span>New Source</span>
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
                  <th>ID</th>
                  <th>NAME</th>
                  <th className="text-center">IN USE</th>
                  <th className="text-center">ACTIVE</th>
                  <th className="text-center">ACTIONS</th>
                </tr>
              </thead>
              <tbody>
                {sources.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="no-records-row">
                      <EmptyState
                        iconName="Layers"
                        title="No sources yet"
                        message="Add a source to record where contacts come from."
                        action={has('Source.Create') ? { label: 'New Source', onClick: openCreate } : undefined}
                      />
                    </td>
                  </tr>
                ) : (
                  sources.map((source) => (
                    <tr key={source.id}>
                      <td>{source.id}</td>
                      <td>
                        <div className="setup-role-name-cell">
                          <button
                            type="button"
                            className="setup-user-name"
                            onClick={() => canEdit && openEdit(source)}
                            disabled={!canEdit}
                          >
                            {source.name}
                          </button>
                          {source.isSystem && (
                            <span className="setup-role-badge muted" title="Built-in — cannot be deleted">
                              <Lock size={11} />
                              Built-in
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="text-center">{source.usageCount}</td>
                      <td className="text-center">
                        <span className={`setup-role-badge ${source.isActive ? 'admin' : 'muted'}`}>
                          {source.isActive ? 'Active' : 'Hidden'}
                        </span>
                      </td>
                      <td className="text-center">
                        <div className="contact-actions-menu-wrapper">
                          <Menu
                            open={openMenuId === source.id}
                            onOpenChange={(isOpen) => setOpenMenuId(isOpen ? source.id : null)}
                            align="end"
                            offset={4}
                            className="contact-actions-dropdown"
                            ariaLabel="Row actions"
                            trigger={(props) => (
                              <button {...props} type="button" className="contact-actions-trigger" aria-label="Row actions">
                                <MoreVertical size={16} />
                              </button>
                            )}
                          >
                            <MenuItem className="contact-actions-item" disabled={!canEdit} onSelect={() => openEdit(source)}>
                              Edit
                            </MenuItem>
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              disabled={!canDelete || source.isSystem}
                              onSelect={() => setDeleteTarget(source)}
                            >
                              Delete
                            </MenuItem>
                          </Menu>
                        </div>
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
        title={editing ? 'Edit Source' : 'New Source'}
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
            <label className="setup-label required" htmlFor="source-name">Name</label>
            <input
              id="source-name"
              className="setup-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              data-autofocus
              required
            />
            {editing && (
              <p className="setup-hint">
                Stored value stays <code>{editing.value}</code>. Renaming only changes the label.
              </p>
            )}
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Active</span>
              <p className="setup-hint">Hidden sources stay on existing contacts but leave the picker.</p>
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
        title="Delete Source"
        message={`Delete the source '${deleteTarget?.name}'? This cannot be undone.`}
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default SourceList
