import React, { useState } from 'react'
import { Paperclip, Reply, ReplyAll, Forward, ChevronDown, ChevronUp } from 'lucide-react'
import { Avatar } from '../Avatar/Avatar'
import type { Message } from '../../types/chat'
import './EmailThreadMessage.css'

interface EmailThreadMessageProps {
  message: Message
  /** Opens the composer for this message. Omitted where replying is not available. */
  onRespond?: (mode: 'reply' | 'replyAll' | 'forward') => void
}

/**
 * One email in a thread — card-style matching the reference image.
 *
 * Renders sender avatar + name row, To/Cc lines, body, and reply action buttons.
 */
export const EmailThreadMessage: React.FC<EmailThreadMessageProps> = ({ message, onRespond }) => {
  const isOutgoing = message.type === 'outgoing'
  const hasHtml = Boolean(message.htmlBody && message.htmlBody.trim().length > 0)
  const [collapsed, setCollapsed] = useState(false)

  const senderName = message.fromName || message.fromAddress || (isOutgoing ? 'You (Support Team)' : 'Customer')
  const senderEmail = message.fromAddress ?? ''

  return (
    <article className={`email-msg-card${isOutgoing ? ' outgoing' : ' incoming'}`}>
      {/* Header row: avatar + sender + timestamp + expand toggle */}
      <div className="email-msg-card-header">
        <Avatar name={senderName} size="small" />

        <div className="email-msg-card-sender">
          <div className="email-msg-card-name-row">
            <strong className="email-msg-sender-name">{senderName}</strong>
            {message.status && (
              <span className={`email-msg-status-pill ${message.status}`}>{message.status}</span>
            )}
          </div>
          {senderEmail && (
            <div className="email-msg-sender-email">{senderEmail}</div>
          )}
        </div>

        <div className="email-msg-card-meta">
          <time className="email-msg-card-time">{message.time}</time>
          {message.hasAttachments && (
            <span className="email-msg-card-attach" title="Has attachments">
              <Paperclip size={13} />
            </span>
          )}
          {onRespond && (
            <button
              type="button"
              className="email-msg-card-more"
              title="Reply"
              onClick={() => onRespond('reply')}
              aria-label="Reply to this message"
            >
              <Reply size={15} aria-hidden="true" />
            </button>
          )}
          <button
            type="button"
            className="email-msg-card-toggle"
            title={collapsed ? 'Expand' : 'Collapse'}
            onClick={() => setCollapsed(c => !c)}
            aria-label={collapsed ? 'Expand message' : 'Collapse message'}
          >
            {collapsed ? <ChevronDown size={15} /> : <ChevronUp size={15} />}
          </button>
        </div>
      </div>

      {!collapsed && (
        <>
          {/* To / Cc lines */}
          <div className="email-msg-card-addresses">
            {message.toAddresses && (
              <div className="email-msg-addr-row">
                <span className="email-msg-addr-label">To:</span>
                <span className="email-msg-addr-value">{message.toAddresses}</span>
              </div>
            )}
            {message.ccAddresses && (
              <div className="email-msg-addr-row">
                <span className="email-msg-addr-label">Cc:</span>
                <span className="email-msg-addr-value">{message.ccAddresses}</span>
              </div>
            )}
          </div>

          {/* Subject */}
          {message.subject && (
            <div className="email-msg-card-subject">{message.subject}</div>
          )}

          {/* Body */}
          <div className="email-msg-card-body">
            {hasHtml ? (
              <iframe
                className="email-msg-iframe"
                title={message.subject || 'Email body'}
                sandbox=""
                srcDoc={`<!doctype html><meta charset="utf-8">
<style>body{margin:0;font:14px/1.6 system-ui,-apple-system,"Segoe UI",sans-serif;color:#1e293b}
img{max-width:100%;height:auto}a{color:#2563eb}p{margin:0 0 8px}</style>${message.htmlBody}`}
              />
            ) : (
              <p className="email-msg-plain">{message.text}</p>
            )}
          </div>

          {/* Reply / Reply All / Forward buttons */}
          {onRespond && (
            <div className="email-msg-card-actions">
              <button
                type="button"
                className="email-msg-action-btn"
                onClick={() => onRespond('reply')}
              >
                <Reply size={14} />
                Reply
              </button>
              <button
                type="button"
                className="email-msg-action-btn"
                onClick={() => onRespond('replyAll')}
              >
                <ReplyAll size={14} />
                Reply All
              </button>
              <button
                type="button"
                className="email-msg-action-btn"
                onClick={() => onRespond('forward')}
              >
                <Forward size={14} />
                Forward
              </button>
            </div>
          )}
        </>
      )}
    </article>
  )
}

export default EmailThreadMessage
