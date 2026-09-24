import React, { useEffect, useMemo, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { RefreshCw, Mail, Plus, Eye, Pencil, Trash2, MoreVertical, Search } from 'lucide-react'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { Menu, MenuItem } from '../../../components/Menu/Menu'
import { Skeleton } from '../../../components/Skeleton'
import { SearchableSelect } from '../../../components/SearchableSelect/SearchableSelect'
import { RichTextEditor } from '../../../components/RichTextEditor/RichTextEditor'
import { EmailPreview } from '../../../components/EmailPreview/EmailPreview'
import usePermission from '../../../hooks/usePermission'
import { emailTemplateService } from '../../../services/email/emailTemplateService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'
import type { EmailTemplate, EmailTemplatePreview } from '../../../types/email'
import './EmailTemplates.css'

const FORM_ID = 'email-template-form'

/**
 * Email templates — the single source of truth for every outgoing email.
 *
 * These same rows are what campaigns render from, which is why this page gained create, delete
 * and preview: it was previously edit-and-toggle over four seeded notifications, and an operator
 * had no way to author the template a campaign would send.
 */
export const EmailTemplatesList: React.FC = () => {
  const { has } = usePermission()

  const [templates, setTemplates] = useState<EmailTemplate[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState('All Status')
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)

  // null = drawer closed. A template = editing it. 'new' = creating one.
  const [editing, setEditing] = useState<EmailTemplate | 'new' | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<EmailTemplate | null>(null)

  const [name, setName] = useState('')
  const [templateKey, setTemplateKey] = useState('')
  const [subject, setSubject] = useState('')
  const [bodyHtml, setBodyHtml] = useState('')
  const [language, setLanguage] = useState('')
  const [description, setDescription] = useState('')
  const [variables, setVariables] = useState<string[]>([])
  const [isEnabled, setIsEnabled] = useState(true)

  const [preview, setPreview] = useState<EmailTemplatePreview | null>(null)
  const [isPreviewing, setIsPreviewing] = useState(false)
  const [previewTarget, setPreviewTarget] = useState<EmailTemplate | null>(null)

  const isCreating = editing === 'new'
  const editingTemplate = editing !== null && editing !== 'new' ? editing : null

  const load = async (showSpinner = true) => {
    if (showSpinner) setIsLoading(true)
    try {
      setTemplates(await emailTemplateService.getTemplates())
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load email templates.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  const resetForm = () => {
    setName('')
    setTemplateKey('')
    setSubject('')
    setBodyHtml('')
    setLanguage('')
    setDescription('')
    setVariables([])
    setIsEnabled(true)
    setPreview(null)
  }

  const openCreate = () => {
    resetForm()
    setEditing('new')
  }

  const openEdit = (template: EmailTemplate) => {
    setEditing(template)
    setName(template.name)
    setTemplateKey(template.key)
    setSubject(template.subject)
    setBodyHtml(template.bodyHtml)
    setLanguage(template.language ?? '')
    setDescription(template.description ?? '')
    setVariables(
      (template.availableVariables ?? '').split(',').map((v) => v.trim()).filter(Boolean)
    )
    setIsEnabled(template.isEnabled)
    setPreview(null)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSubmitting(true)

    const payload = {
      name: name.trim(),
      subject: subject.trim(),
      bodyHtml,
      language: language.trim() || undefined,
      description: description.trim() || undefined,
      availableVariables: variables.length > 0 ? variables.join(',') : undefined,
      isEnabled
    }

    try {
      if (isCreating) {
        await emailTemplateService.createTemplate({ ...payload, key: templateKey.trim() || undefined })
        toast.success('Email template created.')
      } else if (editingTemplate) {
        await emailTemplateService.updateTemplate(editingTemplate.id, payload)
        toast.success('Email template saved.')
      }

      setEditing(null)
      void load(false)
    } catch (error) {
      // The server's message explains the refusal — a duplicate key, most often.
      toast.error(getErrorMessage(error, 'Failed to save the template.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleToggle = async (template: EmailTemplate) => {
    try {
      const updated = await emailTemplateService.toggleTemplate(template.id)
      setTemplates((prev) => prev.map((t) => (t.id === template.id ? updated : t)))
      toast.success(updated.isEnabled ? `'${updated.name}' enabled.` : `'${updated.name}' disabled.`)
    } catch (error) {
      // Refused when a scheduled or sending campaign still needs it — the message says which.
      toast.error(getErrorMessage(error, 'Failed to update the template.'))
    }
  }

  const handleDelete = async () => {
    if (!deleteTarget) return
    try {
      await emailTemplateService.deleteTemplate(deleteTarget.id)
      toast.success(`'${deleteTarget.name}' deleted.`)
      setDeleteTarget(null)
      void load(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to delete the template.'))
      setDeleteTarget(null)
    }
  }

  /**
   * Renders the template server-side, with the same renderer the send path uses.
   *
   * Deliberately not approximated in the browser: a client-side preview would eventually
   * disagree with what actually gets sent, which is the one thing a preview must not do.
   */
  const runPreview = async (templateId: number) => {
    setIsPreviewing(true)
    try {
      setPreview(await emailTemplateService.previewTemplate(templateId, undefined, true))
    } finally {
      setIsPreviewing(false)
    }
  }

  const openPreviewFor = async (template: EmailTemplate) => {
    setOpenMenuId(null)
    setPreviewTarget(template)
    setPreview(null)
    await runPreview(template.id)
  }

  const addVariable = () => {
    const entered = window.prompt('Variable name (lowercase letters, numbers and underscores)')
    if (!entered) return

    const cleaned = entered.trim().toLowerCase().replace(/[^a-z0-9_]/g, '_').replace(/^_+|_+$/g, '')
    if (!cleaned) return
    if (variables.includes(cleaned)) return

    setVariables((prev) => [...prev, cleaned])
  }

  /** Appends the placeholder to the body, so a variable does not have to be typed from memory. */
  const insertVariable = (variable: string) => {
    setBodyHtml((prev) => `${prev}{{${variable}}}`)
  }

  const canEdit = has('EmailTemplate.Edit')
  const canCreate = has('EmailTemplate.Create')
  const canDelete = has('EmailTemplate.Delete')
  const canToggle = has('EmailTemplate.Toggle')

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase()

    return templates.filter((template) => {
      const matchesSearch =
        !term ||
        template.name.toLowerCase().includes(term) ||
        template.subject.toLowerCase().includes(term) ||
        template.key.toLowerCase().includes(term)

      const matchesStatus =
        statusFilter === 'All Status' ||
        (statusFilter === 'Active' ? template.isEnabled : !template.isEnabled)

      return matchesSearch && matchesStatus
    })
  }, [templates, search, statusFilter])

  return (
    <motion.div {...pageTransitionProps}>
      <div className="email-templates-hero">
        <div>
          <h1>Email Templates</h1>
          <p>Create and manage reusable email templates for your campaigns.</p>
        </div>

        {canCreate && (
          <button type="button" className="btn-create-template" onClick={openCreate}>
            <Plus size={16} />
            <span>Create Template</span>
          </button>
        )}
      </div>

      <div className="email-templates-toolbar">
        <div className="email-templates-search">
          <Search size={15} />
          <input
            type="text"
            placeholder="Search email templates..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>

        <SearchableSelect
          placeholder="All Status"
          allValue="All Status"
          value={statusFilter}
          options={[
            { value: 'Active', label: 'Active' },
            { value: 'Inactive', label: 'Inactive' }
          ]}
          onChange={setStatusFilter}
        />

        <button type="button" className="btn-toolbar-tertiary" onClick={() => load()}>
          <RefreshCw size={15} />
          <span>Refresh</span>
        </button>
      </div>

      {isLoading ? (
        <div className="contacts-card"><Skeleton variant="table" /></div>
      ) : filtered.length === 0 ? (
        <div className="contacts-card email-templates-empty">
          <Mail size={28} />
          <p>
            {templates.length === 0
              ? 'No email templates yet. Create one to use in a campaign.'
              : 'No templates match your search.'}
          </p>
        </div>
      ) : (
        <div className="email-template-list">
          {filtered.map((template) => (
            <div className="email-template-row" key={template.id}>
              <div className="email-template-main">
                <div className="email-template-heading">
                  <h3>{template.name}</h3>
                  {template.language && (
                    <span className="email-template-lang">{template.language}</span>
                  )}
                  <span className={`email-template-status ${template.isEnabled ? 'active' : 'inactive'}`}>
                    {template.isEnabled ? 'Active' : 'Inactive'}
                  </span>
                  {template.isSystem && <span className="email-template-system">System</span>}
                </div>

                {template.description && (
                  <p className="email-template-description">{template.description}</p>
                )}

                <p className="email-template-subject">
                  <strong>Subject:</strong> {template.subject}
                </p>

                {template.detectedVariables.length > 0 && (
                  <div className="email-template-vars">
                    <span className="email-template-vars-label">Variables:</span>
                    {template.detectedVariables.map((variable) => (
                      <span className="email-template-var" key={variable}>{`{{${variable}}}`}</span>
                    ))}
                  </div>
                )}
              </div>

              <div className="email-template-side">
                <span className="email-template-updated">
                  Updated:{' '}
                  {new Date(template.updatedAt ?? template.createdAt).toLocaleDateString('en-GB', {
                    day: '2-digit', month: 'short', year: 'numeric'
                  })}
                </span>

                <div className="email-template-actions">
                  <button
                    type="button"
                    className="btn-toolbar-tertiary"
                    onClick={() => openPreviewFor(template)}
                  >
                    <Eye size={14} />
                    Preview
                  </button>

                  {canEdit && (
                    <button type="button" className="btn-toolbar-tertiary" onClick={() => openEdit(template)}>
                      <Pencil size={14} />
                      Edit
                    </button>
                  )}

                  <Menu
                    open={openMenuId === template.id}
                    onOpenChange={(open) => setOpenMenuId(open ? template.id : null)}
                    align="end"
                    ariaLabel={`Actions for ${template.name}`}
                    trigger={(triggerProps) => (
                      <button type="button" className="email-template-menu-btn" {...triggerProps}>
                        <MoreVertical size={16} />
                      </button>
                    )}
                  >
                    {canToggle && (
                      <MenuItem onSelect={() => handleToggle(template)}>
                        {template.isEnabled ? 'Disable' : 'Enable'}
                      </MenuItem>
                    )}
                    {canDelete && !template.isSystem && (
                      <MenuItem destructive onSelect={() => setDeleteTarget(template)}>
                        <Trash2 size={14} />
                        Delete
                      </MenuItem>
                    )}
                  </Menu>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* ── Create / edit drawer ──────────────────────────────────────────────────────────── */}
      <Modal
        isOpen={editing !== null}
        onClose={() => setEditing(null)}
        title={isCreating ? 'Create Email Template' : `Edit — ${editingTemplate?.name ?? ''}`}
        subtitle={
          isCreating
            ? 'Create a reusable email template for your campaigns.'
            : 'Changes apply to every campaign that has not yet been sent.'
        }
        icon={<Mail size={18} />}
        size="lg"
        footer={
          <>
            <button
              type="button"
              className="oc-dialog-btn oc-dialog-btn-secondary"
              onClick={() => setEditing(null)}
            >
              Cancel
            </button>
            <button
              type="submit"
              form={FORM_ID}
              className="oc-dialog-btn oc-dialog-btn-primary"
              disabled={isSubmitting || !name.trim() || !subject.trim() || !bodyHtml.trim()}
            >
              {isSubmitting ? 'Saving…' : 'Save Template'}
            </button>
          </>
        }
      >
        <form id={FORM_ID} onSubmit={handleSubmit} className="setup-modal-form">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="template-name">Template Name</label>
            <input
              id="template-name"
              className="setup-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Welcome Email"
              data-autofocus
              required
            />
          </div>

          <div className="setup-field">
            <label className="setup-label" htmlFor="template-key">Template Key</label>
            <input
              id="template-key"
              className="setup-input"
              value={templateKey}
              onChange={(e) => setTemplateKey(e.target.value)}
              placeholder="welcome_email"
              /* Not editable after creation: system notifications and existing campaigns
                 resolve a template by its key, so changing it would silently break them. */
              disabled={!isCreating}
            />
            <p className="setup-hint">
              {isCreating
                ? 'Lowercase letters, numbers and underscores only. Derived from the name if left blank.'
                : 'The key cannot be changed — other features resolve this template by it.'}
            </p>
          </div>

          <div className="setup-field">
            <label className="setup-label required" htmlFor="template-subject">Subject</label>
            <input
              id="template-subject"
              className="setup-input"
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              placeholder="Welcome {{name}} to {{company_name}}"
              required
            />
          </div>

          <div className="setup-field">
            <label className="setup-label required" htmlFor="template-body">Email Content</label>

            <RichTextEditor
              id="template-body"
              value={bodyHtml}
              onChange={setBodyHtml}
              placeholder="Write the email your recipients will receive…"
              toolbarExtra={
                <div className="email-template-varbar">
                  <span className="email-template-vars-label">Available Variables</span>

                  {variables.map((variable) => (
                    <button
                      key={variable}
                      type="button"
                      className="email-template-var clickable"
                      onClick={() => insertVariable(variable)}
                      title={`Insert {{${variable}}}`}
                    >
                      {`{{${variable}}}`}
                    </button>
                  ))}

                  <button type="button" className="email-template-addvar" onClick={addVariable}>
                    <Plus size={12} />
                    Add Variable
                  </button>
                </div>
              }
            />

            <p className="setup-hint">
              Formatting is kept; scripts, event handlers and javascript: links are stripped on save.
            </p>
          </div>

          <div className="email-template-field-row">
            <div className="setup-field">
              <label className="setup-label" htmlFor="template-language">Language</label>
              <input
                id="template-language"
                className="setup-input"
                value={language}
                onChange={(e) => setLanguage(e.target.value)}
                placeholder="en"
                maxLength={10}
              />
            </div>

            <div className="setup-field">
              <label className="setup-label" htmlFor="template-description">Description</label>
              <input
                id="template-description"
                className="setup-input"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="What this template is for"
              />
            </div>
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Status</span>
              <p className="setup-hint">
                {isEnabled
                  ? 'Active — selectable when creating a campaign.'
                  : 'Inactive — hidden from the campaign template picker.'}
              </p>
            </div>
            <button
              type="button"
              className={`setup-switch ${isEnabled ? 'on' : 'off'}`}
              onClick={() => setIsEnabled((prev) => !prev)}
              aria-pressed={isEnabled}
              aria-label="Active"
            >
              <span className="setup-switch-knob" />
            </button>
          </div>

          {/* Preview is only offered for a saved template: it renders server-side from the
              stored row, so an unsaved draft has nothing to render. */}
          {editingTemplate && (
            <div className="setup-field">
              <div className="email-template-preview-head">
                <label className="setup-label">Preview</label>
                <button
                  type="button"
                  className="btn-toolbar-tertiary"
                  onClick={() => runPreview(editingTemplate.id)}
                  disabled={isPreviewing}
                >
                  <Eye size={14} />
                  {isPreviewing ? 'Rendering…' : 'Preview with Sample Data'}
                </button>
              </div>

              {preview ? (
                <EmailPreview
                  subject={preview.subject}
                  bodyHtml={preview.bodyHtml}
                  fromName="OmniConnect"
                  fromAddress="noreply@example.com"
                />
              ) : (
                <p className="setup-hint">
                  Renders the saved template with sample values, using the same renderer that
                  sends it.
                </p>
              )}
            </div>
          )}
        </form>
      </Modal>

      {/* ── Standalone preview ────────────────────────────────────────────────────────────── */}
      <Modal
        isOpen={previewTarget !== null}
        onClose={() => { setPreviewTarget(null); setPreview(null) }}
        title={`Preview — ${previewTarget?.name ?? ''}`}
        icon={<Eye size={18} />}
        size="lg"
      >
        {isPreviewing ? (
          <Skeleton variant="card" />
        ) : (
          <EmailPreview
            subject={preview?.subject ?? previewTarget?.subject}
            bodyHtml={preview?.bodyHtml ?? previewTarget?.bodyHtml}
            fromName="OmniConnect"
            fromAddress="noreply@example.com"
          />
        )}
      </Modal>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete email template"
        message={
          `Delete "${deleteTarget?.name}"? This cannot be undone. Templates used by a campaign `
          + 'cannot be deleted — disable them instead.'
        }
        confirmText="Delete"
        isDestructive
        showWarningIcon
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default EmailTemplatesList
