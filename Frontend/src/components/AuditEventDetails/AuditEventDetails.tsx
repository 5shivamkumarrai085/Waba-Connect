import React, { useMemo } from 'react'
import { CheckCircle2, XCircle } from 'lucide-react'
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

interface AuditEventDetailsProps {
  event: AuditLogModel | null
  onClose: () => void
  /** Returned focus target, so closing the panel puts the caret back on the row's button. */
  returnFocusRef?: React.RefObject<HTMLElement | null>
}

const Row: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
  <div className="audit-detail-row">
    <span className="audit-detail-label">{label}</span>
    <span className="audit-detail-value">{children}</span>
  </div>
)

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
