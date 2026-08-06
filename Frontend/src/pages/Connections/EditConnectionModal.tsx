import React, { useRef, useState } from 'react'
import { Pencil } from 'lucide-react'
import { Modal } from '../../components/Modal/Modal'
import type { Connection } from '../../types/connection'
import './ConnectionModals.css'

interface EditConnectionModalProps {
  isOpen?: boolean
  connection: Connection | null
  onClose: () => void
  onSave: (id: number, name: string, description?: string, nickname?: string) => Promise<void>
}

export const EditConnectionModal: React.FC<EditConnectionModalProps> = ({
  isOpen = true,
  connection,
  onClose,
  onSave
}) => {
  // Latched so the form keeps its content through the exit animation.
  const latched = useRef(connection)
  if (connection) latched.current = connection
  const conn = latched.current

  const [name, setName] = useState(connection?.name ?? '')
  const [nickname, setNickname] = useState(connection?.nickname || '')
  const [description, setDescription] = useState(connection?.description || '')
  const [isSubmitting, setIsSubmitting] = useState(false)

  // Reseed the form whenever a different connection is opened. Keyed on id so
  // typing is never clobbered by an unrelated re-render.
  const seededId = useRef(connection?.id)
  if (connection && seededId.current !== connection.id) {
    seededId.current = connection.id
    setName(connection.name)
    setNickname(connection.nickname || '')
    setDescription(connection.description || '')
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim() || !nickname.trim() || !conn) return
    setIsSubmitting(true)
    try {
      await onSave(
        conn.id,
        name.trim(),
        description.trim() || undefined,
        nickname.trim()
      )
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Modal
      isOpen={isOpen && !!conn}
      onClose={onClose}
      size="md"
      icon={<Pencil size={18} />}
      title="Edit Connection"
      subtitle="Update connection details in the database."
      footer={
        <>
          <button
            type="button"
            className="oc-dialog-btn oc-dialog-btn-secondary"
            onClick={onClose}
          >
            Cancel
          </button>
          <button
            type="submit"
            form="edit-connection-form"
            className="oc-dialog-btn oc-dialog-btn-primary"
            disabled={isSubmitting || !name.trim() || !nickname.trim()}
          >
            {isSubmitting ? 'Saving...' : 'Save Changes'}
          </button>
        </>
      }
    >
      <form id="edit-connection-form" onSubmit={handleSubmit}>
        <div className="conn-modal-field">
          <label className="conn-modal-label" htmlFor="conn-edit-name">
            Connection Name <span className="conn-modal-required">*</span>
          </label>
          <input
            id="conn-edit-name"
            type="text"
            required
            placeholder="e.g. RMA Support Line"
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="conn-modal-input"
            data-autofocus
          />
          <p className="conn-modal-hint">Give a unique name to identify this connection</p>
        </div>

        <div className="conn-modal-field">
          <label className="conn-modal-label" htmlFor="conn-edit-nickname">
            Nickname <span className="conn-modal-required">*</span>
          </label>
          <input
            id="conn-edit-nickname"
            type="text"
            required
            placeholder="e.g. SALE"
            value={nickname}
            maxLength={4}
            onChange={(e) => setNickname(e.target.value.toUpperCase())}
            className="conn-modal-input conn-modal-input--tag"
          />
          <p className="conn-modal-hint">
            A short tag (max 4 characters) shown on campaigns sent from this connection
          </p>
        </div>

        <div className="conn-modal-field">
          <label className="conn-modal-label" htmlFor="conn-edit-desc">
            Description (Optional)
          </label>
          <textarea
            id="conn-edit-desc"
            rows={3}
            placeholder="e.g. Primary WhatsApp connection for customer support"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            className="conn-modal-input conn-modal-textarea"
          />
          <p className="conn-modal-hint">
            Add a short description to remember what this connection is used for.
          </p>
        </div>
      </form>
    </Modal>
  )
}
