import React, { useEffect, useRef, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { RefreshCw, Mail, Info, Pin } from 'lucide-react'
import { Modal } from '../../../components/Modal/Modal'
import { Skeleton } from '../../../components/Skeleton'
import usePermission from '../../../hooks/usePermission'
import { apiClient } from '../../../services/apiClient'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'

interface EmailTemplate {
  id: number
  key: string
  name: string
  subject: string
  bodyHtml: string
  isEnabled: boolean
  isSystem: boolean
  availableVariables?: string | null
}

const FORM_ID = 'email-template-form'

export const EmailTemplatesList: React.FC = () => {
  const { has } = usePermission()

  const [templates, setTemplates] = useState<EmailTemplate[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [editing, setEditing] = useState<EmailTemplate | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  const [name, setName] = useState('')
  const [subject, setSubject] = useState('')
  const [bodyHtml, setBodyHtml] = useState('')
  const [isEnabled, setIsEnabled] = useState(true)
  const bodyRef = useRef<HTMLTextAreaElement>(null)

  const load = async (showSpinner = true) => {
    if (showSpinner) setIsLoading(true)
    try {
      const response = await apiClient.get('/setup/email-templates')
      setTemplates(response.data?.data ?? [])
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to load email templates.'))
    } finally {
      setIsLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  const openEdit = (template: EmailTemplate) => {
    setEditing(template)
    setName(template.name)
    setSubject(template.subject)
    setBodyHtml(template.bodyHtml)
    setIsEnabled(template.isEnabled)
  }

  const handleToggle = async (template: EmailTemplate) => {
    try {
      await apiClient.patch(`/setup/email-templates/${template.id}/toggle`)
      setTemplates((prev) => prev.map((t) => (t.id === template.id ? { ...t, isEnabled: !t.isEnabled } : t)))
      toast.success(template.isEnabled ? `'${template.name}' disabled.` : `'${template.name}' enabled.`)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to update the template.'))
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!editing) return
    setIsSubmitting(true)
    try {
      await apiClient.put(`/setup/email-templates/${editing.id}`, { name, subject, bodyHtml, isEnabled })
      toast.success('Email template saved.')
      setEditing(null)
      void load(false)
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to save the template.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  /** Inserts a placeholder at the caret so variables don't have to be typed from memory. */
  const insertVariable = (variable: string) => {
    const textarea = bodyRef.current
    const token = `{${variable}}`
    const start = textarea?.selectionStart ?? bodyHtml.length
    const end = textarea?.selectionEnd ?? bodyHtml.length

    setBodyHtml(bodyHtml.slice(0, start) + token + bodyHtml.slice(end))
    requestAnimationFrame(() => {
      textarea?.focus()
      const caret = start + token.length
      textarea?.setSelectionRange(caret, caret)
    })
  }

  const canEdit = has('EmailTemplate.Edit')
  const canToggle = has('EmailTemplate.Toggle')
  const variables = (editing?.availableVariables ?? '').split(',').map((v) => v.trim()).filter(Boolean)

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Email Templates</h1>
        <p>Content and on/off state for the emails the platform can send.</p>
      </div>

      {/* Said plainly on the page: an operator toggling these should not walk away believing
          mail is going out. */}
      <div className="setup-info-box setup-translate-notice">
        <Info size={16} />
        <div>
          <strong>Templates only — no email is sent</strong>
          <p>
            No mail server is configured in this build, so nothing is delivered. These templates
            are stored ready for a future sending implementation.
          </p>
        </div>
      </div>

      <div className="contacts-toolbar">
        <button type="button" className="btn-toolbar-tertiary" onClick={() => load()}>
          <RefreshCw size={15} />
          <span>Refresh</span>
        </button>
      </div>

      {isLoading ? (
        <div className="contacts-card"><Skeleton variant="table" /></div>
      ) : (
        <div className="email-template-grid">
          {templates.map((template) => (
            <div className="email-template-card" key={template.id}>
              <div className="email-template-info">
                <button
                  type="button"
                  className="email-template-name"
                  onClick={() => canEdit && openEdit(template)}
                  disabled={!canEdit}
                >
                  {template.name}
                </button>
                <span className="email-template-subject">
                  {template.key === 'NewContactAssigned' && <Pin size={11} />}
                  {template.subject}
                </span>
              </div>

              <button
                type="button"
                className={`setup-switch ${template.isEnabled ? 'on' : 'off'}`}
                onClick={() => canToggle && handleToggle(template)}
                disabled={!canToggle}
                aria-pressed={template.isEnabled}
                aria-label={`${template.isEnabled ? 'Disable' : 'Enable'} ${template.name}`}
              >
                <span className="setup-switch-knob" />
              </button>
            </div>
          ))}
        </div>
      )}

      <Modal
        isOpen={editing !== null}
        onClose={() => setEditing(null)}
        title={`Edit — ${editing?.name ?? ''}`}
        icon={<Mail size={18} />}
        size="lg"
        footer={
          <>
            <button type="button" className="oc-dialog-btn oc-dialog-btn-secondary" onClick={() => setEditing(null)}>
              Cancel
            </button>
            <button
              type="submit"
              form={FORM_ID}
              className="oc-dialog-btn oc-dialog-btn-primary"
              disabled={isSubmitting || !subject.trim() || !bodyHtml.trim()}
            >
              {isSubmitting ? 'Saving…' : 'Save Template'}
            </button>
          </>
        }
      >
        <form id={FORM_ID} onSubmit={handleSubmit} className="setup-modal-form">
          <div className="setup-field">
            <label className="setup-label required" htmlFor="template-name">Name</label>
            <input
              id="template-name"
              className="setup-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              data-autofocus
              required
            />
          </div>

          <div className="setup-field">
            <label className="setup-label required" htmlFor="template-subject">Subject</label>
            <input
              id="template-subject"
              className="setup-input"
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              required
            />
          </div>

          {variables.length > 0 && (
            <div className="setup-field">
              <label className="setup-label">Available variables</label>
              <div className="email-template-vars">
                {variables.map((variable) => (
                  <button
                    key={variable}
                    type="button"
                    className="email-template-var"
                    onClick={() => insertVariable(variable)}
                    title={`Insert {${variable}}`}
                  >
                    {`{${variable}}`}
                  </button>
                ))}
              </div>
              <p className="setup-hint">Click to insert at the cursor.</p>
            </div>
          )}

          <div className="setup-field">
            <label className="setup-label required" htmlFor="template-body">Body (HTML)</label>
            <textarea
              ref={bodyRef}
              id="template-body"
              className="setup-input setup-textarea email-template-body"
              value={bodyHtml}
              onChange={(e) => setBodyHtml(e.target.value)}
              rows={10}
              required
            />
            <p className="setup-hint">
              Basic formatting tags are kept; scripts and event handlers are stripped on save.
            </p>
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Enabled</span>
              <p className="setup-hint">Marks the template as active for a future sender.</p>
            </div>
            <button
              type="button"
              className={`setup-switch ${isEnabled ? 'on' : 'off'}`}
              onClick={() => setIsEnabled((prev) => !prev)}
              aria-pressed={isEnabled}
              aria-label="Enabled"
            >
              <span className="setup-switch-knob" />
            </button>
          </div>
        </form>
      </Modal>
    </motion.div>
  )
}

export default EmailTemplatesList
