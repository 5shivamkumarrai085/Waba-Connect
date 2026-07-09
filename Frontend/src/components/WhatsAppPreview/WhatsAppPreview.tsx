import React from 'react'
import './WhatsAppPreview.css'

interface WhatsAppPreviewProps {
  bodyText?: string
}

export const WhatsAppPreview: React.FC<WhatsAppPreviewProps> = ({ bodyText }) => {
  return (
    <div className="whatsapp-preview-card">
      <div className="whatsapp-preview-header">
        <div className="whatsapp-preview-avatar">OC</div>
        <div>
          <div className="whatsapp-preview-sender-name">OmniConnect Business</div>
        </div>
      </div>
      <div className="whatsapp-preview-chat-area">
        {bodyText ? (
          <div className="whatsapp-chat-bubble">
            <p className="whatsapp-chat-bubble-text">{bodyText}</p>
            <span className="whatsapp-chat-bubble-time">12:00 PM</span>
          </div>
        ) : (
          <div className="whatsapp-preview-empty-state">
            Select template to see preview
          </div>
        )}
      </div>
    </div>
  )
}
export default WhatsAppPreview
