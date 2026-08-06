import React, { useState } from 'react'
import { Phone, ExternalLink, Edit2, Check } from 'lucide-react'
import { Modal } from '../Modal/Modal'
import type { PhoneInfoModel } from '../../types/waba'
import toast from 'react-hot-toast'
import './PhoneCard.css'

interface PhoneCardProps {
  phoneInfo: PhoneInfoModel | null
  onUpdateLimit?: (newLimit: number) => Promise<{ success: boolean; message: string }>
}

export const PhoneCard: React.FC<PhoneCardProps> = ({ phoneInfo, onUpdateLimit }) => {
  // Hooks must run unconditionally on every render — this pre-existing early
  // return used to sit above the useState calls below, violating the Rules
  // of Hooks (React would throw if phoneInfo ever toggled null <-> non-null
  // across renders of the same mounted instance). Derive a safe default and
  // move the guard below the hooks instead.
  const limitValue = phoneInfo?.messageLimit || 1000

  const [isModalOpen, setIsModalOpen] = useState(false)
  const [customLimitInput, setCustomLimitInput] = useState<string>(String(limitValue))
  const [isSubmitting, setIsSubmitting] = useState(false)

  const handleOpenModal = () => {
    setCustomLimitInput(String(limitValue))
    setIsModalOpen(true)
  }

  const handleSaveLimit = async (e: React.FormEvent) => {
    e.preventDefault()
    const parsed = parseInt(customLimitInput, 10)
    if (isNaN(parsed) || parsed <= 0) {
      toast.error('Please enter a valid positive message limit.')
      return
    }
    if (!onUpdateLimit) return

    setIsSubmitting(true)
    const res = await onUpdateLimit(parsed)
    setIsSubmitting(false)

    if (res.success) {
      toast.success(res.message || 'Daily message limit updated!')
      setIsModalOpen(false)
    } else {
      toast.error(res.message || 'Failed to update message limit.')
    }
  }

  if (!phoneInfo) return null

  const { displayPhoneNumber, verifiedName, numberId, quality, messagesSent } = phoneInfo
  const progressPercent = Math.min(100, Math.round((messagesSent / limitValue) * 100))

  return (
    <div className="phone-card">
      <div className="phone-card-header">
        <div className="phone-card-icon-box">
          <Phone size={18} />
        </div>
        <div className="phone-card-title-area">
          <h3 className="phone-card-title">Phone</h3>
          <span className="phone-card-subtitle">Default Phone Number</span>
        </div>
      </div>

      <div className="phone-card-grid">
        {/* Left Column Info */}
        <div className="phone-detail-group">
          <div className="phone-detail-item">
            <span className="phone-detail-label">Display Phone Number</span>
            <span className="phone-detail-value">{displayPhoneNumber}</span>
          </div>
          <div className="phone-detail-item">
            <span className="phone-detail-label">Verified Name</span>
            <span className="phone-detail-value">{verifiedName}</span>
          </div>
          <div className="phone-detail-item">
            <span className="phone-detail-label">Number ID</span>
            <span className="phone-detail-value">{numberId}</span>
          </div>
        </div>

        {/* Right Column Info */}
        <div className="phone-detail-group">
          <div className="phone-detail-item">
            <span className="phone-detail-label">Quality</span>
            <span className={`phone-detail-value quality-${(quality || '').toLowerCase()}`}>
              {quality}
            </span>
          </div>
          <div className="phone-detail-item">
            <div className="phone-detail-label-row">
              <span className="phone-detail-label">Message Limit</span>
              {onUpdateLimit && (
                <button
                  type="button"
                  className="btn-customize-limit"
                  onClick={handleOpenModal}
                  title="Customize daily message limit for this connection"
                >
                  <Edit2 size={11} />
                  <span>Customize</span>
                </button>
              )}
            </div>
            <div className="message-limit-progress-wrapper">
              <progress 
                className="app-progress" 
                value={messagesSent} 
                max={limitValue}
              />
              <span className="message-limit-progress-label">
                {progressPercent}% &bull; {messagesSent}/{limitValue} messages sent today
              </span>
            </div>
          </div>
        </div>
      </div>

      <div className="phone-card-footer">
        <button 
          type="button" 
          className="phone-manage-btn"
          onClick={() => window.open('https://business.facebook.com/wa/manage/phone-numbers', '_blank')}
        >
          <span>Manage Phone Numbers</span>
          <ExternalLink size={14} />
        </button>
      </div>

      {/* Customize Message Limit Modal */}
      <Modal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title="Set Daily Message Limit"
        size="sm"
        footer={
          <>
            <button
              type="button"
              className="oc-dialog-btn oc-dialog-btn-secondary"
              onClick={() => setIsModalOpen(false)}
              disabled={isSubmitting}
            >
              Cancel
            </button>
            <button
              type="submit"
              form="phone-limit-form"
              className="oc-dialog-btn oc-dialog-btn-primary"
              disabled={isSubmitting}
            >
              <Check size={16} />
              <span>{isSubmitting ? 'Saving...' : 'Save Limit'}</span>
            </button>
          </>
        }
      >
        <form id="phone-limit-form" onSubmit={handleSaveLimit}>
          <p className="phone-limit-intro">
            Customize the maximum number of outbound messages this connection can send per day.
          </p>

          <div className="form-group">
            <label className="waba-input-label">Daily Message Limit</label>
            <input
              type="number"
              min="1"
              max="1000000"
              className="form-control phone-limit-input"
              value={customLimitInput}
              onChange={(e) => setCustomLimitInput(e.target.value)}
              required
              data-autofocus
              placeholder="Enter limit (e.g. 1000, 2500, 5000)"
            />
          </div>

          {/* Preset Limit Pills */}
          <div className="phone-limit-presets">
            {[1000, 2500, 5000, 10000, 50000].map((preset) => (
              <button
                key={preset}
                type="button"
                onClick={() => setCustomLimitInput(String(preset))}
                className={`phone-limit-preset${
                  customLimitInput === String(preset) ? ' is-selected' : ''
                }`}
              >
                {preset.toLocaleString()}
              </button>
            ))}
          </div>
        </form>
      </Modal>
    </div>
  )
}
export default PhoneCard
