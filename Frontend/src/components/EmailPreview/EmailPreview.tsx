import React from 'react'
import { Paperclip, FileText, Image as ImageIcon } from 'lucide-react'
import './EmailPreview.css'

export interface EmailPreviewAttachment {
  fileName: string
  contentType?: string | null
  sizeBytes?: number | null
}

interface EmailPreviewProps {
  fromName?: string | null
  fromAddress?: string | null
  toAddress?: string | null
  replyTo?: string | null
  subject?: string | null
  /** Rendered body. Already sanitized server-side — see the note on the render below. */
  bodyHtml?: string | null
  attachments?: EmailPreviewAttachment[]
  /** Shown while the server-side render is in flight. */
  isLoading?: boolean
}

/**
 * The live preview for an email campaign — the counterpart to WhatsAppPreview.
 *
 * Shows the envelope (from, to, reply-to, subject) above the rendered body, because for email
 * the envelope is most of what an operator needs to check: the wrong sender address or a missing
 * reply-to is a far more common mistake than a malformed body.
 *
 * The `{{contact_email}}` style placeholder in the To line is deliberate. At this point in the
 * wizard the recipients are a set, not one person, so showing a concrete address would imply the
 * preview is of a specific send when it is of the template.
 */
export const EmailPreview: React.FC<EmailPreviewProps> = ({
  fromName,
  fromAddress,
  toAddress,
  replyTo,
  subject,
  bodyHtml,
  attachments = [],
  isLoading = false
}) => {
  const hasBody = Boolean(bodyHtml && bodyHtml.trim().length > 0)

  return (
    <div className="email-preview">
      <div className="email-preview-envelope">
        <div className="email-preview-row">
          <span className="email-preview-label">From:</span>
          <span className="email-preview-value">
            {fromName ? `${fromName} <${fromAddress ?? 'not selected'}>` : fromAddress ?? 'not selected'}
          </span>
        </div>

        <div className="email-preview-row">
          <span className="email-preview-label">To:</span>
          <span className="email-preview-value email-preview-placeholder">
            {toAddress ?? '{{contact_email}}'}
          </span>
        </div>

        {replyTo && (
          <div className="email-preview-row">
            <span className="email-preview-label">Reply To:</span>
            <span className="email-preview-value email-preview-muted">{replyTo}</span>
          </div>
        )}

        <div className="email-preview-row email-preview-subject-row">
          <span className="email-preview-label">Subject:</span>
          <span className="email-preview-value email-preview-subject">
            {subject || 'No subject yet'}
          </span>
        </div>
      </div>

      <div className="email-preview-body">
        {isLoading ? (
          <p className="email-preview-hint">Rendering preview…</p>
        ) : hasBody ? (
          /*
           * The body is inserted as HTML because that is what an email body is — rendering it as
           * text would defeat the point of a preview.
           *
           * Safe to do here specifically because this markup has been through the server's
           * allowlist sanitizer (SanitizationHelper.SanitizeHtml), which strips script tags,
           * javascript: URLs and inline event handlers. It is never raw operator input: the
           * preview endpoint returns the same sanitized body that would be sent.
           */
          <div
            className="email-preview-content"
            dangerouslySetInnerHTML={{ __html: bodyHtml as string }}
          />
        ) : (
          <p className="email-preview-hint">
            Select a template to preview the email your recipients will receive.
          </p>
        )}
      </div>

      {attachments.length > 0 && (
        <div className="email-preview-attachments">
          <div className="email-preview-attachments-title">
            <Paperclip size={12} />
            {attachments.length} {attachments.length === 1 ? 'Attachment' : 'Attachments'}
          </div>

          <div className="email-preview-attachment-list">
            {attachments.map((attachment) => (
              <span key={attachment.fileName} className="email-preview-attachment">
                {isImage(attachment) ? <ImageIcon size={13} /> : <FileText size={13} />}
                <span className="email-preview-attachment-name">{attachment.fileName}</span>
              </span>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

const isImage = (attachment: EmailPreviewAttachment): boolean => {
  if (attachment.contentType?.startsWith('image/')) return true

  // Falls back to the extension, because an attachment added through the upload flow does not
  // always carry a content type.
  const extension = attachment.fileName.split('.').pop()?.toLowerCase()
  return ['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg'].includes(extension ?? '')
}

export default EmailPreview
