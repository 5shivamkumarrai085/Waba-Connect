import React, { useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate } from 'react-router-dom'
import { X } from 'lucide-react'
import toast from 'react-hot-toast'
import { connectionService } from '../../services/connections/connectionService'
import './ConnectNewWabaPage.css'

export const ConnectNewWabaPage: React.FC = () => {
  const navigate = useNavigate()

  // Basic Details Fields
  const [name, setName] = useState('')
  const [nickname, setNickname] = useState('')
  const [description, setDescription] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) {
      toast.error('Connection name is required.')
      return
    }
    if (!nickname.trim()) {
      toast.error('Nickname is required.')
      return
    }

    setIsSubmitting(true)
    try {
      const created = await connectionService.createConnection({
        name: name.trim(),
        nickname: nickname.trim(),
        description: description.trim() || undefined
      })

      toast.success(`Connection "${created.name}" created! Redirecting to setup...`)
      navigate(`/connect-waba?connectionId=${created.id}`)
    } catch {
      toast.error('Failed to create connection. Please try again.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <motion.div className="waba-wizard-wrapper" {...pageTransitionProps}>
      <div className="waba-wizard-card">
        {/* Header */}
        <div className="waba-wizard-header omni-page-hero form-card-hero">
          <h2 className="waba-wizard-title">Connect New WABA</h2>
          <button
            type="button"
            onClick={() => navigate('/connections')}
            className="waba-wizard-close-btn"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Stepper Indicator Bar */}
        <div className="waba-stepper-bar">
          {/* Step 1 Badge */}
          <div className="waba-step-item">
            <div className="waba-step-badge active">
              1
            </div>
            <div className="waba-step-labels">
              <span className="waba-step-title">Basic Details</span>
              {name && <span className="waba-step-sub">{name}</span>}
            </div>
          </div>

          <div className="waba-step-divider" />

          {/* Step 2 Badge */}
          <div className="waba-step-item">
            <div className="waba-step-badge pending">
              2
            </div>
            <div className="waba-step-labels">
              <span className="waba-step-title">WABA Connection</span>
              <span className="waba-step-sub">Enter WABA details</span>
            </div>
          </div>
        </div>

        {/* Basic Details Form */}
        <form onSubmit={handleSubmit} className="waba-wizard-body">
          <h3 className="waba-wizard-heading">Let's start with the basics</h3>
          <p className="waba-wizard-subheading">
            Give your connection a name to easily identify it later.
          </p>

          <div className="waba-field-group">
            <label className="waba-field-label">
              Connection Name <span className="waba-field-required">*</span>
            </label>
            <input
              type="text"
              required
              placeholder="e.g. Support Line, Sales WABA"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="waba-field-input"
              autoFocus
            />
            <p className="waba-field-hint">This name will help you identify this connection in the future.</p>
          </div>

          <div className="waba-field-group">
            <label className="waba-field-label">
              Nickname <span className="waba-field-required">*</span>
            </label>
            <input
              type="text"
              required
              placeholder="e.g. SALE"
              value={nickname}
              maxLength={4}
              onChange={(e) => setNickname(e.target.value.toUpperCase())}
              className="waba-field-input"
              style={{ maxWidth: '160px', fontWeight: 700, letterSpacing: '0.05em' }}
            />
            <p className="waba-field-hint">A short tag (max 4 characters) shown on campaigns sent from this connection.</p>
          </div>

          <div className="waba-field-group">
            <label className="waba-field-label">Description (Optional)</label>
            <textarea
              rows={3}
              placeholder="e.g. Primary WhatsApp connection for customer support"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="waba-field-input"
              style={{ resize: 'vertical' }}
            />
            <p className="waba-field-hint">Add a short description to remember what this connection is used for.</p>
          </div>

          <div className="waba-wizard-footer">
            <button
              type="button"
              onClick={() => navigate('/connections')}
              className="btn-wizard-cancel"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSubmitting || !name.trim()}
              className="btn-wizard-next"
            >
              {isSubmitting ? 'Creating Connection...' : 'Continue to Setup →'}
            </button>
          </div>
        </form>
      </div>
    </motion.div>
  )
}
