import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Plus, RefreshCw, MoreVertical, Star, Sparkles } from 'lucide-react'
import { Menu, MenuItem } from '../../../components/Menu/Menu'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import Can from '../../../components/Can/Can'
import usePermission from '../../../hooks/usePermission'
import { aiReplyService, type AiPrompt } from '../../../services/setup/aiReplyService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'

const FORM_ID = 'ai-prompt-form'

export const AiPromptsList: React.FC = () => {
  const { has } = usePermission()

  const [prompts, setPrompts] = useState<AiPrompt[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)
  const [editing, setEditing] = useState<AiPrompt | null>(null)
  const [isModalOpen, setIsModalOpen] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<AiPrompt | null>(null)

  const [name, setName] = useState('')
  const [promptText, setPromptText] = useState('')
  const [description, setDescription] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [isDefault, setIsDefault] = useState(false)

  const load = async (showSpinner = true) => {
    if (showSpinner) setIsLoading(true)
    try {
      setPrompts(await aiReplyService.getPrompts())
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load AI prompts.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  const openCreate = () => {
    setEditing(null)
    setName('')
    setPromptText('')
    setDescription('')
    setIsActive(true)
    setIsDefault(false)
    setIsModalOpen(true)
  }

  const openEdit = (prompt: AiPrompt) => {
    setEditing(prompt)
    setName(prompt.name)
    setPromptText(prompt.promptText)
    setDescription(prompt.description ?? '')
    setIsActive(prompt.isActive)
    setIsDefault(prompt.isDefault)
    setIsModalOpen(true)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSubmitting(true)
    try {
      const payload = { name: name.trim(), promptText, description: description.trim(), isActive, isDefault }
      if (editing) {
        await aiReplyService.updatePrompt(editing.id, payload)
        toast.success('AI prompt updated.')
      } else {
        await aiReplyService.createPrompt(payload)
        toast.success('AI prompt created.')
      }
      setIsModalOpen(false)
      void load(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to save the prompt.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await aiReplyService.deletePrompt(deleteTarget.id)
      toast.success(`Prompt '${deleteTarget.name}' deleted.`)
      void load(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to delete the prompt.'))
    } finally {
      setDeleteTarget(null)
    }
  }

  const canEdit = has('AiPrompt.Edit')
  const canDelete = has('AiPrompt.Delete')

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>AI Prompts</h1>
        <p>Define how the AI assistant behaves when it replies to customers.</p>
      </div>

      <div className="contacts-toolbar">
        <Can permission="AiPrompt.Create">
          <button type="button" className="btn-toolbar-primary" onClick={openCreate}>
            <Plus size={15} />
            <span>New Prompt</span>
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
                  <th>PROMPT</th>
                  <th className="text-center">ACTIVE</th>
                </tr>
              </thead>
              <tbody>
                {prompts.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="no-records-row">
                      <EmptyState
                        iconName="Sparkles"
                        title="No AI prompts yet"
                        message="Create a prompt to control how the assistant replies."
                        action={has('AiPrompt.Create') ? { label: 'New Prompt', onClick: openCreate } : undefined}
                      />
                    </td>
                  </tr>
                ) : (
                  prompts.map((prompt) => (
                    <tr key={prompt.id}>
                      <td className="actions-col">
                        <div className="contact-actions-menu-wrapper">
                          <Menu
                            open={openMenuId === prompt.id}
                            onOpenChange={(isOpen) => setOpenMenuId(isOpen ? prompt.id : null)}
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
                            <MenuItem className="contact-actions-item" disabled={!canEdit} onSelect={() => openEdit(prompt)}>
                              Edit
                            </MenuItem>
                            <MenuItem
                              destructive
                              className="contact-actions-item"
                              disabled={!canDelete || prompt.isDefault}
                              onSelect={() => setDeleteTarget(prompt)}
                            >
                              Delete
                            </MenuItem>
                          </Menu>
                        </div>
                      </td>
                      <td>{prompt.id}</td>
                      <td>
                        <div className="setup-role-name-cell">
                          <button
                            type="button"
                            className="setup-user-name"
                            onClick={() => canEdit && openEdit(prompt)}
                            disabled={!canEdit}
                          >
                            {prompt.name}
                          </button>
                          {prompt.isDefault && (
                            <span className="setup-role-badge admin" title="Used when nothing else specifies a prompt">
                              <Star size={11} />
                              Default
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="setup-truncate" title={prompt.promptText}>
                        {prompt.description || prompt.promptText}
                      </td>
                      <td className="text-center">
                        <span className={`setup-role-badge ${prompt.isActive ? 'admin' : 'muted'}`}>
                          {prompt.isActive ? 'Active' : 'Inactive'}
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
        title={editing ? 'Edit AI Prompt' : 'New AI Prompt'}
        icon={<Sparkles size={18} />}
        size="lg"
        footer={
          <>
            <button type="button" className="oc-dialog-btn oc-dialog-btn-secondary" onClick={() => setIsModalOpen(false)}>
              Cancel
            </button>
            <button
              type="submit"
              form={FORM_ID}
              className="oc-dialog-btn oc-dialog-btn-primary"
              disabled={isSubmitting || !name.trim() || !promptText.trim()}
            >
              {isSubmitting ? 'Saving…' : 'Submit'}
            </button>
          </>
        }
      >
        <form id={FORM_ID} onSubmit={handleSubmit} className="setup-modal-form">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="prompt-name">Name</label>
            <input
              id="prompt-name"
              className="setup-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Support Agent"
              data-autofocus
              required
            />
          </div>

          <div className="setup-field">
            <label className="setup-label" htmlFor="prompt-description">Description</label>
            <input
              id="prompt-description"
              className="setup-input"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Short summary shown in the list"
            />
          </div>

          <div className="setup-field">
            <label className="setup-label required" htmlFor="prompt-text">Prompt</label>
            <textarea
              id="prompt-text"
              className="setup-input setup-textarea"
              value={promptText}
              onChange={(e) => setPromptText(e.target.value)}
              rows={8}
              placeholder="You are a polite, concise customer support assistant…"
              required
            />
            <p className="setup-hint">
              Sent to the model as the system prompt. Line breaks are preserved.
            </p>
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Active</span>
              <p className="setup-hint">Inactive prompts cannot be selected on a flow node.</p>
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

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Default</span>
              <p className="setup-hint">Used whenever a conversation doesn&apos;t specify a prompt.</p>
            </div>
            <button
              type="button"
              className={`setup-switch ${isDefault ? 'on' : 'off'}`}
              onClick={() => setIsDefault((prev) => !prev)}
              aria-pressed={isDefault}
              aria-label="Default prompt"
              disabled={editing?.isDefault}
            >
              <span className="setup-switch-knob" />
            </button>
          </div>
        </form>
      </Modal>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete AI Prompt"
        message={`Delete the prompt '${deleteTarget?.name}'? Flows using it fall back to their own instructions.`}
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default AiPromptsList
