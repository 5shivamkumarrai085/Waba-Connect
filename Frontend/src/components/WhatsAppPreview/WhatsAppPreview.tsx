import React, { useMemo } from 'react'
import { ExternalLink, Phone, Reply, Copy } from 'lucide-react'
import './WhatsAppPreview.css'

export interface WhatsAppPreviewButton {
  /** QUICK_REPLY, URL, PHONE_NUMBER or COPY_CODE — the icon follows the type. */
  type: string
  text: string
}

interface WhatsAppPreviewProps {
  bodyText?: string
  headerText?: string
  footerText?: string
  buttons?: WhatsAppPreviewButton[]
  /** The business name shown above the chat. */
  senderName?: string
  /** Shown when there is no body yet. */
  emptyMessage?: string
  className?: string
}

const BUTTON_ICONS: Record<string, React.ElementType> = {
  URL: ExternalLink,
  PHONE_NUMBER: Phone,
  COPY_CODE: Copy,
  QUICK_REPLY: Reply
}

/** A WhatsApp message as the customer will see it: header, body, footer and buttons. */
export const WhatsAppPreview: React.FC<WhatsAppPreviewProps> = ({
  bodyText,
  headerText,
  footerText,
  buttons = [],
  senderName = 'Your business',
  emptyMessage = 'Select a template to see a preview.',
  className
}) => {
  // The time the preview is opened, in the viewer's own locale and clock format.
  const time = useMemo(() => new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(new Date()), [])
  const initials = senderName.split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0]?.toUpperCase()).join('')

  return (
    <div className={`whatsapp-preview-card${className ? ` ${className}` : ''}`} aria-label="Message preview">
      <div className="whatsapp-preview-header">
        <div className="whatsapp-preview-avatar" aria-hidden="true">{initials || 'B'}</div>
        <div className="whatsapp-preview-sender-name">{senderName}</div>
      </div>
      <div className="whatsapp-preview-chat-area">
        {bodyText ? (
          <div className="whatsapp-message">
            <div className="whatsapp-chat-bubble">
              {headerText && <p className="whatsapp-chat-bubble-header">{headerText}</p>}
              <p className="whatsapp-chat-bubble-text">{bodyText}</p>
              {footerText && <p className="whatsapp-chat-bubble-footer">{footerText}</p>}
              <span className="whatsapp-chat-bubble-time">{time}</span>
            </div>
            {buttons.length > 0 && (
              <ul className="whatsapp-chat-buttons" aria-label="Buttons">
                {buttons.map((b, i) => {
                  const Icon = BUTTON_ICONS[b.type] ?? Reply
                  return (
                    <li key={i} className="whatsapp-chat-button">
                      <Icon size={14} aria-hidden="true" />
                      <span>{b.text}</span>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
        ) : (
          <div className="whatsapp-preview-empty-state">{emptyMessage}</div>
        )}
      </div>
    </div>
  )
}

export default WhatsAppPreview
