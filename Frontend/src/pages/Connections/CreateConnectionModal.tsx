import React, { useState } from 'react'
import { Plus, MessageSquare } from 'lucide-react'
import { Modal } from '../../components/Modal/Modal'
import './ConnectionModals.css'

interface CreateConnectionModalProps {
  isOpen?: boolean
  onClose: () => void
  onCreate: (name: string, description?: string) => Promise<void>
}

export const CreateConnectionModal: React.FC<CreateConnectionModalProps> = ({
  isOpen = true,
  onClose,
  onCreate
}) => {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    setIsSubmitting(true)
    try {
      await onCreate(name.trim(), description.trim() || undefined)
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      size="sm"
      icon={<MessageSquare size={20} />}
      title="Connect New WABA"
      subtitle="Name your new WhatsApp Business Account setup."
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
            form="create-connection-form"
            className="oc-dialog-btn oc-dialog-btn-primary"
            disabled={isSubmitting || !name.trim()}
          >
            <Plus size={16} />
            {isSubmitting ? 'Creating...' : 'Continue to Setup'}
          </button>
        </>
      }
    >
      <form id="create-connection-form" onSubmit={handleSubmit}>
        <div className="conn-modal-field">
          <label className="conn-modal-label" htmlFor="conn-create-name">
            Connection Name <span className="conn-modal-required">*</span>
          </label>
          <input
            id="conn-create-name"
            type="text"
            required
            placeholder="e.g. Support Line, Sales WABA"
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="conn-modal-input"
            data-autofocus
          />
        </div>

        <div className="conn-modal-field">
          <label className="conn-modal-label" htmlFor="conn-create-desc">
            Description (Optional)
          </label>
          <input
            id="conn-create-desc"
            type="text"
            placeholder="e.g. Primary WhatsApp for customer inquiries"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            className="conn-modal-input"
          />
        </div>
      </form>
    </Modal>
  )
}
