import React, { useState } from 'react'
import { createPortal } from 'react-dom'
import { Phone, ExternalLink, Edit2, X, Check } from 'lucide-react'
import type { PhoneInfoModel } from '../../types/waba'
import toast from 'react-hot-toast'
import './PhoneCard.css'

interface PhoneCardProps {
  phoneInfo: PhoneInfoModel | null
  onUpdateLimit?: (newLimit: number) => Promise<{ success: boolean; message: string }>
}

export const PhoneCard: React.FC<PhoneCardProps> = ({ phoneInfo, onUpdateLimit }) => {
  if (!phoneInfo) return null

  const { displayPhoneNumber, verifiedName, numberId, quality, messagesSent, messageLimit } = phoneInfo
  const limitValue = messageLimit || 1000
  const progressPercent = Math.min(100, Math.round((messagesSent / limitValue) * 100))

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
      {isModalOpen && createPortal(
        <div
          className="modal-backdrop fade-in"
          onClick={() => setIsModalOpen(false)}
          style={{
            position: 'fixed',
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            width: '100vw',
            height: '100vh',
            backgroundColor: 'rgba(15, 23, 42, 0.5)',
            backdropFilter: 'blur(8px)',
            WebkitBackdropFilter: 'blur(8px)',
            zIndex: 99999,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center'
          }}
        >
          <div className="modal-container scale-in" onClick={(e) => e.stopPropagation()} style={{ maxWidth: '420px', width: '90%', borderRadius: '16px', position: 'relative', overflow: 'hidden' }}>
            <div className="modal-header" style={{ padding: '20px 24px', borderBottom: '1px solid #f1f5f9', position: 'relative' }}>
              <h3 className="modal-title" style={{ fontSize: '1.15rem', fontWeight: 700, color: '#1e293b', margin: 0 }}>Set Daily Message Limit</h3>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => setIsModalOpen(false)}
                style={{ position: 'absolute', top: '18px', right: '20px', background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}
              >
                <X size={18} />
              </button>
            </div>

            <form onSubmit={handleSaveLimit}>
              <div className="modal-body">
                <p style={{ fontSize: '0.85rem', color: '#64748b', marginBottom: '16px', lineHeight: '1.4' }}>
                  Customize the maximum number of outbound messages this connection can send per day.
                </p>

                <div className="form-group">
                  <label className="waba-input-label" style={{ fontWeight: 600 }}>Daily Message Limit</label>
                  <input
                    type="number"
                    min="1"
                    max="1000000"
                    className="form-control"
                    value={customLimitInput}
                    onChange={(e) => setCustomLimitInput(e.target.value)}
                    required
                    autoFocus
                    placeholder="Enter limit (e.g. 1000, 2500, 5000)"
                    style={{ fontSize: '1rem', fontWeight: 600 }}
                  />
                </div>

                {/* Preset Limit Pills */}
                <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', marginTop: '12px' }}>
                  {[1000, 2500, 5000, 10000, 50000].map((preset) => (
                    <button
                      key={preset}
                      type="button"
                      onClick={() => setCustomLimitInput(String(preset))}
                      style={{
                        padding: '4px 10px',
                        borderRadius: '16px',
                        border: '1px solid #cbd5e1',
                        background: customLimitInput === String(preset) ? '#4f46e5' : '#ffffff',
                        color: customLimitInput === String(preset) ? '#ffffff' : '#475569',
                        fontSize: '0.75rem',
                        fontWeight: 600,
                        cursor: 'pointer',
                        transition: 'all 0.15s ease'
                      }}
                    >
                      {preset.toLocaleString()}
                    </button>
                  ))}
                </div>
              </div>

              <div className="modal-footer" style={{ borderTop: '1px solid #f1f5f9', padding: '16px 20px', display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
                <button
                  type="button"
                  className="btn-modal-cancel"
                  onClick={() => setIsModalOpen(false)}
                  disabled={isSubmitting}
                  style={{ padding: '8px 16px', borderRadius: '6px', border: '1px solid #cbd5e1', background: '#fff', fontSize: '0.875rem', fontWeight: 500, cursor: 'pointer' }}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn-modal-confirm"
                  disabled={isSubmitting}
                  style={{ padding: '8px 20px', borderRadius: '6px', border: 'none', background: '#4f46e5', color: '#fff', fontSize: '0.875rem', fontWeight: 600, cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px' }}
                >
                  <Check size={16} />
                  <span>{isSubmitting ? 'Saving...' : 'Save Limit'}</span>
                </button>
              </div>
            </form>
          </div>
        </div>,
        document.body
      )}
    </div>
  )
}
export default PhoneCard
