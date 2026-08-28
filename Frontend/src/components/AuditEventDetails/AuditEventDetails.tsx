import React, { useMemo } from 'react'
import { ArrowDownLeft, ArrowUpRight, CheckCircle2, Paperclip, Trash2, XCircle } from 'lucide-react'
import { Modal } from '../Modal/Modal'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'
import type { AuditLogModel } from '../../types/reporting'
import './AuditEventDetails.css'

/**
 * One entity's changes, as serialised by the backend's audit change capture.
 * Mirrors Backend/Services/Interfaces/IAuditChangeBuffer.cs.
 */
interface AuditChangeGroup {
  entityType: string
  entityId?: string | null
  entityName?: string | null
  action: string
  changes: { field: string; oldValue?: string | null; newValue?: string | null }[]
}

/** One deleted chat message. Mirrors Backend/Models/DTOs/Activity/AuditMetadata.cs. */
interface AuditDeletedMessage {
  id: number
  direction?: string | null
  text?: string | null
  mediaType?: string | null
  mediaFileName?: string | null
  sentAt: string
  status?: string | null
}

/** Event-specific payload envelope. Every section is optional by design. */
interface AuditMetadata {
  deletedMessages?: AuditDeletedMessage[] | null
  deletedMessageCount?: number | null
  deletedMessagesTruncated?: boolean | null
}

interface AuditEventDetailsProps {
  event: AuditLogModel | null
  onClose: () => void
}

const Row: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
  <div className="audit-detail-row">
    <span className="audit-detail-label">{label}</span>
    <span className="audit-detail-value">{children}</span>
  </div>
)

/**
 * A deleted message, rendered as the message it was.
 *
 * Laid out like a conversation — incoming on the left, outgoing on the right — because that is
 * what makes "who said this" readable at a glance. A three-column table of the same data would be
 * accurate and unreadable; the point of the record is that someone can look at it and recognise
 * the conversation that was removed.
 */
const DeletedMessage: React.FC<{ message: AuditDeletedMessage }> = ({ message }) => {
  const isIncoming = message.direction?.toLowerCase() === 'incoming'
  const hasMedia = Boolean(message.mediaFileName || message.mediaType)

  // An attachment is stored with a generated placeholder in its text column. Rendering that
  // beneath the attachment row would show the same filename twice. Matched exactly rather than
  // by "contains", so a real caption that happens to mention the file still survives.
  const isPlaceholderText =
    Boolean(message.mediaFileName) && message.text === `[Attachment: ${message.mediaFileName}]`
  const body = isPlaceholderText ? null : message.text

  return (
    <li className={`audit-msg${isIncoming ? ' is-incoming' : ' is-outgoing'}`}>
      <div className="audit-msg-bubble">
        {/* Text and media are not exclusive — a WhatsApp attachment can carry a caption, and
            dropping either would misrepresent what was deleted. */}
        {hasMedia && (
          <div className="audit-msg-media">
            <Paperclip size={12} />
            <span>{message.mediaFileName || message.mediaType}</span>
            {message.mediaFileName && message.mediaType && (
              <span className="audit-msg-media-type">{message.mediaType}</span>
            )}
          </div>
        )}
        {body ? (
          <p className="audit-msg-text">{body}</p>
        ) : (
          !hasMedia && <p className="audit-msg-text is-empty">(no text)</p>
        )}
        <div className="audit-msg-meta">
          {isIncoming ? <ArrowDownLeft size={11} /> : <ArrowUpRight size={11} />}
          <span className="audit-msg-direction">{isIncoming ? 'Incoming' : 'Outgoing'}</span>
          <span className="audit-msg-dot">·</span>
          <span>{formatAbsoluteDateTime(message.sentAt)}</span>
          {message.status && (
            <>
              <span className="audit-msg-dot">·</span>
              <span>{message.status}</span>
            </>
          )}
        </div>
      </div>
    </li>
  )
}

export const AuditEventDetails: React.FC<AuditEventDetailsProps> = ({ event, onClose }) => {
  // Parsed defensively: ChangesJson is written by a background capture, and a details panel
  // that throws on one malformed row would take the whole page down with it.
  const groups = useMemo<AuditChangeGroup[]>(() => {
    if (!event?.changesJson) return []
    try {
      const parsed = JSON.parse(event.changesJson)
      return Array.isArray(parsed) ? parsed : []
    } catch {
      return []
    }
  }, [event?.changesJson])

  // Same defensive treatment as changesJson, and for the same reason. Historical entries predate
  // this column entirely, so null is the normal case rather than an error.
  const metadata = useMemo<AuditMetadata | null>(() => {
    if (!event?.metadataJson) return null
    try {
      const parsed = JSON.parse(event.metadataJson)
      return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : null
    } catch {
      return null
    }
  }, [event?.metadataJson])

  const deletedMessages = Array.isArray(metadata?.deletedMessages) ? metadata.deletedMessages : []
  // The recorded count, not the list length: the list is capped, and reporting its length as the
  // total would understate a large deletion.
  const deletedCount = metadata?.deletedMessageCount ?? deletedMessages.length

  const isFailure = event?.status === 'Failed'

  return (
    <Modal
      isOpen={Boolean(event)}
      onClose={onClose}
      placement="right"
      size="custom"
      title="Event Details"
      showCloseButton
    >
      {event && (
        <div className="audit-detail">
          <div className={`audit-detail-status${isFailure ? ' is-failed' : ''}`}>
            {isFailure ? <XCircle size={16} /> : <CheckCircle2 size={16} />}
            <span>{isFailure ? 'Failed' : 'Successfully Completed'}</span>
          </div>

          <div className="audit-detail-rows">
            <Row label="Event ID">
              <span className="audit-detail-mono">{event.eventNumber}</span>
            </Row>
            <Row label="Timestamp">{formatAbsoluteDateTime(event.time)}</Row>
            <Row label="User">{event.user || 'System'}</Row>
            <Row label="Module">{event.module || '—'}</Row>
            <Row label="Action">{event.action || '—'}</Row>
            <Row label="Entity Type">{event.entityType || '—'}</Row>
            <Row label="Entity Name / ID">
              {event.entityName
                ? `${event.entityName}${event.entityId ? ` (${event.entityId})` : ''}`
                : event.entityId || '—'}
            </Row>
            <Row label="IP Address">
              <span className="audit-detail-mono">{event.ipAddress || '—'}</span>
            </Row>
            <Row label="User Agent">
              <span className="audit-detail-agent">{event.userAgent || '—'}</span>
            </Row>
          </div>

          <div className="audit-detail-section">
            <h3 className="audit-detail-section-title">Description</h3>
            <p className="audit-detail-description">{event.description || '—'}</p>
          </div>

          {/* Only for events that actually removed messages. A chat delete is the one operation
              where the deleted content is the record — after a conversation delete these rows
              exist nowhere else. */}
          {deletedMessages.length > 0 && (
            <div className="audit-detail-section">
              <h3 className="audit-detail-section-title audit-msg-section-title">
                <Trash2 size={13} />
                <span>Deleted messages</span>
                <span className="audit-msg-count">{deletedCount}</span>
              </h3>
              <ol className="audit-msg-list">
                {deletedMessages.map((message) => (
                  <DeletedMessage key={message.id} message={message} />
                ))}
              </ol>
              {metadata?.deletedMessagesTruncated && (
                <p className="audit-detail-empty audit-msg-truncated">
                  Showing the first {deletedMessages.length} of {deletedCount} deleted messages.
                </p>
              )}
            </div>
          )}

          <div className="audit-detail-section">
            <h3 className="audit-detail-section-title">Changes</h3>
            {groups.length === 0 ? (
              // Honest rather than blank. Reads happen, and events recorded before change
              // capture shipped genuinely have nothing to show.
              <p className="audit-detail-empty">No field changes were recorded for this event.</p>
            ) : (
              groups.map((group, groupIndex) => (
                <div key={`${group.entityType}-${group.entityId}-${groupIndex}`} className="audit-changes-group">
                  {/* Only labelled when one event touched more than one entity — a single-entity
                      change would just repeat the Entity Type row above. */}
                  {groups.length > 1 && (
                    <div className="audit-changes-group-head">
                      {group.entityType}
                      {group.entityId ? ` ${group.entityId}` : ''}
                      {group.entityName ? ` — ${group.entityName}` : ''}
                      <span className="audit-changes-group-action">{group.action}</span>
                    </div>
                  )}
                  <div className="audit-changes-table-wrap">
                    <table className="audit-changes-table">
                      <thead>
                        <tr>
                          <th>Field</th>
                          <th>Old Value</th>
                          <th>New Value</th>
                        </tr>
                      </thead>
                      <tbody>
                        {group.changes.map((change, index) => (
                          <tr key={`${change.field}-${index}`}>
                            <td className="audit-change-field">{change.field}</td>
                            <td className="audit-change-old">{change.oldValue ?? '—'}</td>
                            <td className="audit-change-new">{change.newValue ?? '—'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              ))
            )}
          </div>
        </div>
      )}
    </Modal>
  )
}

export default AuditEventDetails
