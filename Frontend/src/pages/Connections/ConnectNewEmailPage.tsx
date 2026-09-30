import React, { useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate } from 'react-router-dom'
import { X } from 'lucide-react'
import toast from 'react-hot-toast'
import { emailConnectionService } from '../../services/email/emailConnectionService'
import { getErrorMessage } from '../../utils/errorHelper'
import './ConnectNewWabaPage.css'
import './ConnectNewEmailPage.css'

/**
 * Step 1 of connecting an email sender: what this connection is, and who it sends as.
 *
 * Mirrors ConnectNewWabaPage deliberately — same card, same stepper bar, same stylesheet — so
 * the two channels feel like one product rather than two features bolted together. Step 2 lives
 * on its own page, as it does for WABA, because provider configuration is long enough to deserve
 * the full width and because an operator often has to leave and come back with credentials.
 */
export const ConnectNewEmailPage: React.FC = () => {
  const navigate = useNavigate()

  const [name, setName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [emailAddress, setEmailAddress] = useState('')
  const [replyToEmail, setReplyToEmail] = useState('')
  const [description, setDescription] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  // Inline error shown under the name field when the server returns 409 Conflict
  const [nameError, setNameError] = useState<string | null>(null)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    if (!name.trim()) {
      toast.error('Connection name is required.')
      return
    }

    if (!displayName.trim()) {
      toast.error('Display name is required — it is what recipients see as the sender.')
      return
    }

    if (!emailAddress.trim()) {
      toast.error('Email address is required.')
      return
    }

    setIsSubmitting(true)
    setNameError(null)
    try {
      const created = await emailConnectionService.createConnection({
        name: name.trim(),
        displayName: displayName.trim(),
        emailAddress: emailAddress.trim(),
        replyToEmail: replyToEmail.trim() || undefined,
        description: description.trim() || undefined
      })

      toast.success(`Connection "${created.connectionName}" created. Now configure a provider.`)
      navigate(`/connect-email?emailConfigurationId=${created.id}`)
    } catch (err: any) {
      // 409 Conflict = duplicate name — surface as inline field error, not a floating toast.
      // Any other status gets the toast so unexpected failures are still visible.
      const status = err?.response?.status
      const msg = getErrorMessage(err, 'Could not create the email connection.')
      if (status === 409) {
        setNameError(msg)
      } else {
        toast.error(msg)
      }
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <motion.div className="waba-wizard-wrapper" {...pageTransitionProps}>
      <div className="waba-wizard-card">
        <div className="waba-wizard-header email omni-page-hero form-card-hero">
          <div>
            <h2 className="waba-wizard-title">Connect New Email</h2>
            <p className="waba-wizard-subtitle">Connect an email account to send email campaigns.</p>
          </div>
          <button
            type="button"
            onClick={() => navigate('/connections')}
            className="waba-wizard-close-btn"
            aria-label="Cancel and return to connections"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="waba-stepper-bar">
          <div className="waba-step-item">
            <div className="waba-step-badge active">1</div>
            <div className="waba-step-labels">
              <span className="waba-step-title">Basic Details</span>
              {name && <span className="waba-step-sub">{name}</span>}
            </div>
          </div>

          <div className="waba-step-divider" />

          <div className="waba-step-item">
            <div className="waba-step-badge pending">2</div>
            <div className="waba-step-labels">
              <span className="waba-step-title">Email Configuration</span>
              <span className="waba-step-sub">Choose a provider</span>
            </div>
          </div>
        </div>

        <form onSubmit={handleSubmit} className="waba-wizard-body">
          <h3 className="waba-wizard-heading">Let&apos;s start with the basics</h3>
          <p className="waba-wizard-subheading">
            Provide the basic details for your email connection.
          </p>

          <div className="email-field-grid">
            <div className="waba-field-group">
              <label className="waba-field-label">
                Connection Name <span className="waba-field-required">*</span>
              </label>
              <input
                type="text"
                required
                placeholder="e.g. Marketing Email"
                value={name}
                onChange={(e) => {
                  setName(e.target.value)
                  // Clear the inline error as soon as the user edits the name
                  if (nameError) setNameError(null)
                }}
                className={`waba-field-input${nameError ? ' field-input-error' : ''}`}
                autoFocus
              />
              {nameError ? (
                <p className="waba-field-error">{nameError} Use a different name, or go to <button type="button" className="btn-inline-link" onClick={() => navigate('/connections')}>Connections</button> to configure the existing one.</p>
              ) : (
                <p className="waba-field-hint">This name will help you identify this connection in the future.</p>
              )}
            </div>

            <div className="waba-field-group">
              <label className="waba-field-label">
                Display Name <span className="waba-field-required">*</span>
              </label>
              <input
                type="text"
                required
                placeholder="e.g. OmniConnect Marketing"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                className="waba-field-input"
              />
              <p className="waba-field-hint">
                This name will be shown as the sender name to recipients.
              </p>
            </div>

            <div className="waba-field-group">
              <label className="waba-field-label">
                Email Address <span className="waba-field-required">*</span>
              </label>
              <input
                type="email"
                required
                placeholder="e.g. marketing@example.com"
                value={emailAddress}
                onChange={(e) => setEmailAddress(e.target.value)}
                className="waba-field-input"
              />
              <p className="waba-field-hint">The email address to send campaigns from.</p>
            </div>

            <div className="waba-field-group">
              <label className="waba-field-label">Reply-To Email (Optional)</label>
              <input
                type="email"
                placeholder="e.g. support@example.com"
                value={replyToEmail}
                onChange={(e) => setReplyToEmail(e.target.value)}
                className="waba-field-input"
              />
              <p className="waba-field-hint">Replies to your emails will be sent to this address.</p>
            </div>
          </div>

          <div className="waba-field-group">
            <label className="waba-field-label">Description (Optional)</label>
            <textarea
              rows={3}
              placeholder="e.g. Primary email connection for marketing campaigns"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="waba-field-input email-field-textarea"
            />
            <p className="waba-field-hint">
              Add a short description to remember what this connection is used for.
            </p>
          </div>

          <div className="waba-wizard-footer">
            <button type="button" onClick={() => navigate('/connections')} className="btn-wizard-cancel">
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSubmitting || !name.trim() || !displayName.trim() || !emailAddress.trim()}
              className="btn-wizard-next"
            >
              {isSubmitting ? 'Creating Connection...' : 'Next →'}
            </button>
          </div>
        </form>
      </div>
    </motion.div>
  )
}

export default ConnectNewEmailPage
