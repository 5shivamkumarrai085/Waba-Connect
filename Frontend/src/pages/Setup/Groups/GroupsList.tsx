import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Plus, RefreshCw, MoreVertical } from 'lucide-react'
import { Menu, MenuItem } from '../../../components/Menu/Menu'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { SearchBar } from '../../../components/SearchBar/SearchBar'
import { Skeleton } from '../../../components/Skeleton'
import ColorPicker from '../../../components/ColorPicker/ColorPicker'
import Can from '../../../components/Can/Can'
import usePermission from '../../../hooks/usePermission'
import { groupService, type ContactGroupRecord } from '../../../services/setup/groupService'
import { useLookupStore } from '../../../store/lookupStore'
import { getErrorMessage } from '../../../utils/errorHelper'
import { formatAbsoluteDateTime } from '../../../utils/dateHelper'
import { pageTransitionProps } from '../../../utils/motion'

const FORM_ID = 'group-form'
const DEFAULT_GROUP_COLOR = '#8B5CF6'

/**
 * Contact groups, administered from Setup alongside the other lookups.
 *
 * Groups are a genuine table with their own CRUD, so unlike Status/Source/Type there is no
 * immutable `value` to protect and no built-in rows — every group is user-created and every
 * group is deletable.
 */
export const GroupsList: React.FC = () => {
  const { has } = usePermission()

  const [groups, setGroups] = useState<ContactGroupRecord[]>([])
  const [search, setSearch] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)
  const [editing, setEditing] = useState<ContactGroupRecord | null>(null)
  const [isModalOpen, setIsModalOpen] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<ContactGroupRecord | null>(null)

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [color, setColor] = useState(DEFAULT_GROUP_COLOR)

  const load = async (showSpinner = true, term = search) => {
    if (showSpinner) setIsLoading(true)
    try {
      setGroups(await groupService.getAll(term))
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load groups.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  // Debounced so typing doesn't fire a request per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => { void load(false, search) }, 400)
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search])

  const openCreate = () => {
    setEditing(null)
    setName('')
    setDescription('')
    setColor(DEFAULT_GROUP_COLOR)
    setIsModalOpen(true)
  }

  const openEdit = (group: ContactGroupRecord) => {
    setEditing(group)
    setName(group.name)
    setDescription(group.description ?? '')
    setColor(group.color || DEFAULT_GROUP_COLOR)
    setIsModalOpen(true)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSubmitting(true)
    try {
      const payload = { name: name.trim(), description: description.trim() || null, color }
      if (editing) {
        await groupService.update(editing.id, payload)
        toast.success('Group updated.')
      } else {
        await groupService.create(payload)
        toast.success('Group created.')
      }
      setIsModalOpen(false)
      void load(false)
      // Groups drive both the Contacts group column and its filter dropdown.
      void useLookupStore.getState().invalidate()
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to save the group.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await groupService.remove(deleteTarget.id)
      toast.success(`Group '${deleteTarget.name}' deleted.`)
      void load(false)
      // Groups drive both the Contacts group column and its filter dropdown.
      void useLookupStore.getState().invalidate()
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to delete the group.'))
    } finally {
      setDeleteTarget(null)
    }
  }

  const canEdit = has('ContactGroup.Edit')
  const canDelete = has('ContactGroup.Delete')

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Groups</h1>
        <p>Organise contacts into groups you can target from campaigns.</p>
      </div>

      <div className="contacts-toolbar">
        <Can permission="ContactGroup.Create">
          <button type="button" className="btn-toolbar-primary" onClick={openCreate}>
            <Plus size={15} />
            <span>New Group</span>
          </button>
        </Can>
        <button type="button" className="btn-toolbar-tertiary" onClick={() => load()}>
          <RefreshCw size={15} />
          <span>Refresh</span>
        </button>
      </div>

      <div className="contacts-card">
        <div className="setup-list-controls">
          <SearchBar value={search} onChange={setSearch} placeholder="Search groups..." />
        </div>

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
                  <th>DESCRIPTION</th>
                  <th className="text-center">MEMBERS</th>
                  <th>CREATED</th>
                </tr>
              </thead>
              <tbody>
                {groups.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="no-records-row">
                      <EmptyState
                        iconName="Users"
                        title={search ? 'No matching groups' : 'No groups yet'}
                        message={
                          search
                            ? 'Try a different search term.'
                            : 'Create a group to start organising contacts.'
                        }
                        action={
                          !search && has('ContactGroup.Create')
                            ? { label: 'New Group', onClick: openCreate }
                            : undefined
                        }
                      />
                    </td>
                  </tr>
                ) : (
                  groups.map((group) => (
                    <tr key={group.id}>
                      <td className="actions-col">
                        <div className="contact-actions-menu-wrapper">
                          <Menu
                            open={openMenuId === group.id}
                            onOpenChange={(isOpen) => setOpenMenuId(isOpen ? group.id : null)}
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
                            <MenuItem className="contact-actions-item" disabled={!canEdit} onSelect={() => openEdit(group)}>
                              Edit
                            </MenuItem>
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              disabled={!canDelete}
                              onSelect={() => setDeleteTarget(group)}
                            >
                              Delete
                            </MenuItem>
                          </Menu>
                        </div>
                      </td>
                      <td>{group.id}</td>
                      <td>
                        <button
                          type="button"
                          className="setup-user-name"
                          onClick={() => canEdit && openEdit(group)}
                          disabled={!canEdit}
                        >
                          {group.name}
                        </button>
                      </td>
                      <td>
                        <div className="setup-color-cell">
                          <span className="setup-color-dot" style={{ backgroundColor: group.color || '#8B5CF6' }} />
                          <code>{group.color || 'auto'}</code>
                        </div>
                      </td>
                      <td className="setup-muted-cell">{group.description || '—'}</td>
                      {/* Surfaced so an admin can see what a delete would affect. */}
                      <td className="text-center">{group.memberCount}</td>
                      <td>{formatAbsoluteDateTime(group.createdAt)}</td>
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
        title={editing ? 'Edit Group' : 'New Group'}
        subtitle="Group contacts for targeting and filtering"
        placement="right"
        size="sm"
        // Cancel is gone: the drawer already has a close button in its header, and two
        // ways to abandon the same form is one more than anybody needs. Save is the only
        // thing the footer is for now.
        footer={
          <button type="submit" form={FORM_ID} className="oc-dialog-btn oc-dialog-btn-primary" disabled={isSubmitting || !name.trim()}>
            {isSubmitting ? 'Saving…' : 'Submit'}
          </button>
        }
      >
        <form id={FORM_ID} onSubmit={handleSubmit} className="setup-modal-form">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="group-name">Name</label>
            <input
              id="group-name"
              className="setup-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              data-autofocus
              required
            />
          </div>

          <div className="setup-field">
            <label className="setup-label required">Color</label>
            <ColorPicker value={color} onChange={setColor} />
            <p className="setup-hint">Used for the group badge on the contacts list.</p>
          </div>

          <div className="setup-field">
            <label className="setup-label" htmlFor="group-description">Description</label>
            <textarea
              id="group-description"
              className="setup-input"
              rows={3}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What is this group for?"
            />
          </div>
        </form>
      </Modal>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Group"
        message={
          deleteTarget && deleteTarget.memberCount > 0
            ? `Delete '${deleteTarget.name}'? ${deleteTarget.memberCount} contact(s) will be removed from it. The contacts themselves are not deleted.`
            : `Delete the group '${deleteTarget?.name}'? This cannot be undone.`
        }
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default GroupsList
