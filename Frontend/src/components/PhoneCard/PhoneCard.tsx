import React from 'react'
import { Phone, ExternalLink } from 'lucide-react'
import type { PhoneInfoModel } from '../../types/waba'
import './PhoneCard.css'

interface PhoneCardProps {
  phoneInfo: PhoneInfoModel | null
}

export const PhoneCard: React.FC<PhoneCardProps> = ({ phoneInfo }) => {
  if (!phoneInfo) return null

  const { displayPhoneNumber, verifiedName, numberId, quality, messagesSent, messageLimit } = phoneInfo
  const progressPercent = Math.min(100, Math.round((messagesSent / messageLimit) * 100))

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
            <span className="phone-detail-label">Message Limit</span>
            <div className="message-limit-progress-wrapper">
              <progress 
                className="app-progress" 
                value={messagesSent} 
                max={messageLimit}
              />
              <span className="message-limit-progress-label">
                {progressPercent}% &bull; {messagesSent}/{messageLimit} messages sent today
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
    </div>
  )
}
export default PhoneCard
