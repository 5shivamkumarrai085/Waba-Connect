import React, { useRef } from 'react'
import { CheckCircle, Smartphone, Key, Globe, Shield } from 'lucide-react'
import { Modal } from '../../components/Modal/Modal'
import type { Connection } from '../../types/connection'
import './ConnectionModals.css'

interface ConnectionDetailProps {
  isOpen?: boolean
  connection: Connection | null
  onClose: () => void
}

export const ConnectionDetail: React.FC<ConnectionDetailProps> = ({
  isOpen = true,
  connection,
  onClose
}) => {
  // Keep rendering the last connection while the dialog animates out, so the
  // body does not blank when the parent clears its selection on close.
  const latched = useRef(connection)
  if (connection) latched.current = connection
  const conn = latched.current

  return (
    <Modal
      isOpen={isOpen && !!conn}
      onClose={onClose}
      size="lg"
      icon={conn ? <span className="conn-detail-id">{conn.id}</span> : undefined}
      title={conn?.name}
      subtitle={conn?.description || 'WhatsApp Business Connection'}
      footer={
        <button
          type="button"
          className="oc-dialog-btn oc-dialog-btn-secondary"
          onClick={onClose}
        >
          Close
        </button>
      }
    >
      {conn && (
        <>
          <div className="conn-detail-status-row">
            <span className="conn-detail-status-label">Connection Status</span>
            <span
              className={`conn-detail-status-pill ${
                conn.isConnected
                  ? 'conn-detail-status-pill--on'
                  : 'conn-detail-status-pill--off'
              }`}
            >
              <span className="conn-detail-status-dot" />
              {conn.isConnected ? 'Connected & Active' : 'Disconnected'}
            </span>
          </div>

          <div className="conn-detail-grid">
            <div className="conn-detail-cell">
              <div className="conn-detail-cell-label">
                <Smartphone size={14} />
                Phone Number
              </div>
              <p className="conn-detail-cell-value">
                {conn.phoneNumber || 'Not Connected'}
              </p>
            </div>

            <div className="conn-detail-cell">
              <div className="conn-detail-cell-label">
                <Key size={14} />
                Phone Number ID
              </div>
              <p className="conn-detail-cell-value conn-detail-cell-value--mono">
                {conn.phoneNumberId || 'N/A'}
              </p>
            </div>

            <div className="conn-detail-cell">
              <div className="conn-detail-cell-label">
                <Globe size={14} />
                WABA Account ID
              </div>
              <p className="conn-detail-cell-value conn-detail-cell-value--mono">
                {conn.wabaId || 'N/A'}
              </p>
            </div>

            <div className="conn-detail-cell">
              <div className="conn-detail-cell-label">
                <CheckCircle size={14} />
                Verified Business Name
              </div>
              <p className="conn-detail-cell-value">
                {conn.verifiedName || conn.displayName || 'OmniConnect Business'}
              </p>
            </div>
          </div>

          <div className="conn-detail-note">
            <div className="conn-detail-note-title">
              <Shield size={14} />
              Security &amp; Scope Isolation
            </div>
            <p className="conn-detail-note-body">
              This connection is isolated. All chats, webhook callbacks, and bot flows
              routed through number{' '}
              <strong>{conn.phoneNumber || 'assigned'}</strong> are strictly segregated
              and accessible only to authorized operators.
            </p>
          </div>
        </>
      )}
    </Modal>
  )
}
