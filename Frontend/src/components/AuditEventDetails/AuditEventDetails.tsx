import React, { useMemo } from 'react'
import {
  Activity,
  ArrowDownLeft,
  ArrowUpRight,
  CheckCircle2,
  Clock,
  Fingerprint,
  Globe,
  Hash,
  Layers,
  LayoutGrid,
  Monitor,
  Paperclip,
  ShieldCheck,
  Trash2,
  User,
  XCircle
} from 'lucide-react'
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

/**
 * One fact about the event: a tinted icon tile, a small uppercase label, and the value.
 *
 * The tone only colours the tile. It carries no meaning the text does not already state,
 * so a reader who cannot distinguish the hues loses nothing.
 */
const DetailRow: React.FC<{
  icon: React.ReactNode
  label: string
  tone?: 'blue' | 'neutral' | 'purple' | 'success' | 'danger'
  children: React.ReactNode
}> = ({ icon, label, tone = 'blue', children }) => (
  <div className="audit-detail-row">
    <span className={`audit-detail-icon is-${tone}`}>{icon}</span>
    <span className="audit-detail-body">
      <span className="audit-detail-label">{label}</span>
      <span className="audit-detail-value">{children}</span>
    </span>
  </div>
)

/**
 * Best-effort browser and OS from a user-agent string.
 *
 * Deliberately shallow: a full UA database is not worth shipping to render two lines,
 * and the raw string is displayed underneath regardless — so anything this fails to
 * recognise is still fully readable rather than lost. Unknown values say "Unknown"
 * instead of guessing.
 */
const describeUserAgent = (ua?: string | null): { browser: string; os: string } => {
  if (!ua) return { browser: 'Unknown', os: 'Unknown' }

  // Order matters: Edge and Opera both advertise Chrome, and Chrome advertises Safari.
  const browsers: [RegExp, string][] = [
    [/Edg\/(\d+)/, 'Edge'],
    [/OPR\/(\d+)/, 'Opera'],
    [/Chrome\/(\d+)/, 'Chrome'],
    [/Firefox\/(\d+)/, 'Firefox'],
    [/Version\/(\d+).*Safari/, 'Safari']
  ]

  let browser = 'Unknown'
  for (const [rx, name] of browsers) {
    const m = ua.match(rx)
    if (m) {
      browser = `${name} ${m[1]}`
      break
    }
  }

  const os = /Windows NT 10/.test(ua)
    ? 'Windows 10/11'
    : /Windows/.test(ua)
      ? 'Windows'
      : /Mac OS X/.test(ua)
        ? 'macOS'
        : /Android/.test(ua)
          ? 'Android'
          : /iPhone|iPad/.test(ua)
            ? 'iOS'
            : /Linux/.test(ua)
              ? 'Linux'
              : 'Unknown'

  return { browser, os }
}

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
  const agent = useMemo(() => describeUserAgent(event?.userAgent), [event?.userAgent])

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
          {/* ── Overview + Timeline ────────────────────────────────────────
              Two columns, as the host lays this drawer out: the facts of the event
              on the left, its progression on the right, split by a dashed rule. */}
          <div className="audit-detail-section">
            <div className="audit-overview-grid">
              <div>
                <h3 className="audit-detail-section-title">
                  <LayoutGrid size={12} />
                  <span>Overview</span>
                </h3>

                <div className="audit-detail-list">
                  <DetailRow icon={<Hash size={15} />} tone="neutral" label="Event ID">
                    <span className="audit-pill audit-pill-mono">{event.eventNumber}</span>
                  </DetailRow>

                  <DetailRow icon={<Layers size={15} />} tone="blue" label="Module">
                    <span className="audit-pill audit-pill-mono">{event.module || '—'}</span>
                  </DetailRow>

                  <DetailRow icon={<Activity size={15} />} tone="purple" label="Action">
                    <span className="audit-pill audit-pill-action">{event.action || '—'}</span>
                  </DetailRow>

                  <DetailRow
                    icon={isFailure ? <XCircle size={15} /> : <CheckCircle2 size={15} />}
                    tone={isFailure ? 'danger' : 'success'}
                    label="Result"
                  >
                    <span className={`audit-pill audit-pill-result${isFailure ? ' is-failed' : ''}`}>
                      <span className="audit-pill-dot" />
                      {isFailure ? 'Failed' : 'Success'}
                    </span>
                  </DetailRow>

                  <DetailRow icon={<Clock size={15} />} tone="blue" label="Timestamp">
                    {formatAbsoluteDateTime(event.time)}
                  </DetailRow>
                </div>
              </div>

              <div className="audit-overview-divider">
                <h3 className="audit-detail-section-title">
                  <Clock size={12} />
                  <span>Event Timeline</span>
                </h3>

                {/* Two steps, because that is genuinely all an audit row records: who
                    set it off, and how it ended. Inventing intermediate steps would be
                    decoration pretending to be data. */}
                <div className="audit-timeline">
                  <div className="audit-timeline-step">
                    <span className="audit-timeline-dot" />
                    <div className="audit-timeline-card">
                      <span className="audit-timeline-label">
                        Triggered by {event.user || 'System'}
                      </span>
                      <span className="audit-timeline-time">
                        <Clock size={11} />
                        {formatAbsoluteDateTime(event.time)}
                      </span>
                    </div>
                  </div>

                  <div className="audit-timeline-step">
                    <span
                      className={`audit-timeline-dot${isFailure ? ' is-danger' : ' is-success'}`}
                    />
                    <div className="audit-timeline-card">
                      <span className="audit-timeline-label">
                        {isFailure ? 'Event Failed' : 'Event Completed Successfully'}
                      </span>
                      <span className="audit-timeline-time">
                        <ShieldCheck size={11} />
                        {event.module || 'System'}
                      </span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* ── Actor & authentication ─────────────────────────────────────── */}
          <div className="audit-detail-section">
            <h3 className="audit-detail-section-title">
              <User size={12} />
              <span>Actor &amp; Authentication Context</span>
            </h3>

            <div className="audit-field-grid">
              <DetailRow icon={<User size={15} />} tone="blue" label="Actor Name">
                {event.user || 'System'}
              </DetailRow>

              <DetailRow icon={<Fingerprint size={15} />} tone="neutral" label="Actor ID">
                <span className="audit-pill audit-pill-mono">
                  {event.userId ?? 'System / None'}
                </span>
              </DetailRow>

              <DetailRow icon={<Layers size={15} />} tone="purple" label="Entity Type">
                {event.entityType || '—'}
              </DetailRow>

              <DetailRow icon={<Hash size={15} />} tone="neutral" label="Entity Name / ID">
                {event.entityName
                  ? `${event.entityName}${event.entityId ? ` (${event.entityId})` : ''}`
                  : event.entityId || '—'}
              </DetailRow>

              <DetailRow icon={<Globe size={15} />} tone="blue" label="Client IP">
                <span className="audit-pill audit-pill-mono">
                  <span className="audit-pill-dot" />
                  {event.ipAddress || '—'}
                </span>
              </DetailRow>
            </div>
          </div>

          {/* ── Device & environment ───────────────────────────────────────── */}
          <div className="audit-detail-section">
            <h3 className="audit-detail-section-title">
              <Globe size={12} />
              <span>Device &amp; Environment Context</span>
            </h3>

            <div className="audit-field-grid">
              <DetailRow icon={<Globe size={15} />} tone="blue" label="Browser">
                <span className="audit-pill">{agent.browser}</span>
              </DetailRow>

              <DetailRow icon={<Monitor size={15} />} tone="neutral" label="Operating System">
                <span className="audit-pill">{agent.os}</span>
              </DetailRow>
            </div>

            <div className="audit-ua-label">Raw User Agent</div>
            <pre className="audit-ua-box">{event.userAgent || '—'}</pre>
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
