import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Plus, RefreshCw, MoreVertical, MessageSquareReply } from 'lucide-react'
import { Menu, MenuItem } from '../../../components/Menu/Menu'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import Can from '../../../components/Can/Can'
import usePermission from '../../../hooks/usePermission'
import { aiReplyService, type CannedReply } from '../../../services/setup/aiReplyService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'

const FORM_ID = 'canned-reply-form'

export const CannedRepliesList: React.FC = () => {
  const { has } = usePermission()

  const [replies, setReplies] = useState<CannedReply[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)
  const [editing, setEditing] = useState<CannedReply | null>(null)
  const [isModalOpen, setIsModalOpen] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<CannedReply | null>(null)

  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [isPublic, setIsPublic] = useState(true)
  const [isActive, setIsActive] = useState(true)

  const load = async (showSpinner = true) => {
    if (showSpinner) setIsLoading(true)
    try {
      setReplies(await aiReplyService.getCannedReplies(false))
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load canned replies.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  const openCreate = () => {
    setEditing(null)
    setTitle('')
    setDescription('')
    setIsPublic(true)
    setIsActive(true)
    setIsModalOpen(true)
  }

  const openEdit = (reply: CannedReply) => {
    setEditing(reply)
    setTitle(reply.title)
    setDescription(reply.description)
    setIsPublic(reply.isPublic)
    setIsActive(reply.isActive)
    setIsModalOpen(true)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSubmitting(true)
    try {
      const payload = { title: title.trim(), description, isPublic, isActive }
      if (editing) {
        await aiReplyService.updateCannedReply(editing.id, payload)
        toast.success('Canned reply updated.')
      } else {
        await aiReplyService.createCannedReply(payload)
        toast.success('Canned reply created.')
      }
      setIsModalOpen(false)
      void load(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to save the reply.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleTogglePublic = async (reply: CannedReply) => {
    try {
      await aiReplyService.toggleCannedReplyPublic(reply.id)
      setReplies((prev) => prev.map((r) => (r.id === reply.id ? { ...r, isPublic: !r.isPublic } : r)))
      toast.success(reply.isPublic ? 'Reply is now private to you.' : 'Reply is now shared with everyone.')
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to update the reply.'))
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await aiReplyService.deleteCannedReply(deleteTarget.id)
      toast.success(`Reply '${deleteTarget.title}' deleted.`)
      void load(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to delete the reply.'))
    } finally {
      setDeleteTarget(null)
    }
  }

  const canEdit = has('CannedReply.Edit')
  const canDelete = has('CannedReply.Delete')

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Canned Reply</h1>
        <p>Saved replies agents can drop into a conversation.</p>
      </div>

      <div className="contacts-toolbar">
        <Can permission="CannedReply.Create">
          <button type="button" className="btn-toolbar-primary" onClick={openCreate}>
            <Plus size={15} />
            <span>New Canned Reply</span>
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
                  <th>TITLE</th>
                  <th>DESCRIPTION</th>
                  <th className="text-center">PUBLIC</th>
                  <th className="text-center">ACTIVE</th>
                </tr>
              </thead>
              <tbody>
                {replies.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="no-records-row">
                      <EmptyState
                        iconName="MessageSquareReply"
                        title="No canned replies yet"
                        message="Save a reply so agents can insert it with one click."
                        action={has('CannedReply.Create') ? { label: 'New Canned Reply', onClick: openCreate } : undefined}
                      />
                    </td>
                  </tr>
                ) : (
                  replies.map((reply) => (
                    <tr key={reply.id}>
                      <td className="actions-col">
                        <div className="contact-actions-menu-wrapper">
                          <Menu
                            open={openMenuId === reply.id}
                            onOpenChange={(isOpen) => setOpenMenuId(isOpen ? reply.id : null)}
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
                            <MenuItem className="contact-actions-item" disabled={!canEdit} onSelect={() => openEdit(reply)}>
                              Edit
                            </MenuItem>
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              disabled={!canDelete}
                              onSelect={() => setDeleteTarget(reply)}
                            >
                              Delete
                            </MenuItem>
                          </Menu>
                        </div>
                      </td>
                      <td>{reply.id}</td>
                      <td>
                        <button
                          type="button"
                          className="setup-user-name"
                          onClick={() => canEdit && openEdit(reply)}
                          disabled={!canEdit}
                        >
                          {reply.title}
                        </button>
                      </td>
                      <td className="setup-truncate" title={reply.description}>{reply.description}</td>
                      <td className="text-center">
                        <button
                          type="button"
                          className={`setup-switch ${reply.isPublic ? 'on' : 'off'}`}
                          onClick={() => canEdit && handleTogglePublic(reply)}
                          disabled={!canEdit}
                          aria-pressed={reply.isPublic}
                          aria-label={reply.isPublic ? 'Shared with everyone' : 'Private to you'}
                          title={reply.isPublic ? 'Shared with everyone' : 'Private to you'}
                        >
                          <span className="setup-switch-knob" />
                        </button>
                      </td>
                      <td className="text-center">
                        <span className={`setup-role-badge ${reply.isActive ? 'admin' : 'muted'}`}>
                          {reply.isActive ? 'Active' : 'Inactive'}
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
        title={editing ? 'Edit Canned Reply' : 'New Canned Reply'}
        icon={<MessageSquareReply size={18} />}
        size="md"
        footer={
          <>
            <button type="button" className="oc-dialog-btn oc-dialog-btn-secondary" onClick={() => setIsModalOpen(false)}>
              Cancel
            </button>
            <button
              type="submit"
              form={FORM_ID}
              className="oc-dialog-btn oc-dialog-btn-primary"
              disabled={isSubmitting || !title.trim() || !description.trim()}
            >
              {isSubmitting ? 'Saving…' : 'Submit'}
            </button>
          </>
        }
      >
        <form id={FORM_ID} onSubmit={handleSubmit} className="setup-modal-form">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="reply-title">Title</label>
            <input
              id="reply-title"
              className="setup-input"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Welcome to our support team"
              data-autofocus
              required
            />
          </div>

          <div className="setup-field">
            <label className="setup-label required" htmlFor="reply-description">Description</label>
            <textarea
              id="reply-description"
              className="setup-input setup-textarea"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={5}
              placeholder="The text inserted into the message box."
              required
            />
            <p className="setup-hint">This exact text is inserted into the composer, line breaks included.</p>
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Public</span>
              <p className="setup-hint">Shared with every agent. Turn off to keep it to yourself.</p>
            </div>
            <button
              type="button"
              className={`setup-switch ${isPublic ? 'on' : 'off'}`}
              onClick={() => setIsPublic((prev) => !prev)}
              aria-pressed={isPublic}
              aria-label="Public"
            >
              <span className="setup-switch-knob" />
            </button>
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Active</span>
              <p className="setup-hint">Inactive replies stay saved but leave the chat picker.</p>
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
        title="Delete Canned Reply"
        message={`Delete '${deleteTarget?.title}'? This cannot be undone.`}
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default CannedRepliesList
