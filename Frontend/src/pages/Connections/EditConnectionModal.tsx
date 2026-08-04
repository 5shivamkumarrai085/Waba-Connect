import React, { useState } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import type { Connection } from '../../types/connection'

interface EditConnectionModalProps {
  connection: Connection
  onClose: () => void
  onSave: (id: number, name: string, description?: string, nickname?: string) => Promise<void>
}

export const EditConnectionModal: React.FC<EditConnectionModalProps> = ({
  connection,
  onClose,
  onSave
}) => {
  const [name, setName] = useState(connection.name)
  const [nickname, setNickname] = useState(connection.nickname || '')
  const [description, setDescription] = useState(connection.description || '')
  const [isSubmitting, setIsSubmitting] = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    setIsSubmitting(true)
    try {
      await onSave(connection.id, name.trim(), description.trim() || undefined, nickname.trim() || undefined)
    } finally {
      setIsSubmitting(false)
    }
  }

  return createPortal(
    <div
      style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        zIndex: 999999,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'rgba(15, 23, 42, 0.55)',
        backdropFilter: 'blur(6px)',
        WebkitBackdropFilter: 'blur(6px)',
        padding: '1rem'
      }}
    >
      <div
        style={{
          backgroundColor: '#ffffff',
          borderRadius: '1rem',
          boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.25)',
          maxWidth: '520px',
          width: '100%',
          overflow: 'hidden',
          border: '1px solid #e2e8f0',
          padding: '1.75rem 2rem'
        }}
      >
        {/* Header */}
        <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: '1.25rem' }}>
          <div>
            <h3 style={{ fontSize: '1.25rem', fontWeight: 700, color: '#0f172a', margin: 0, lineHeight: 1.3 }}>
              Edit Connection
            </h3>
            <p style={{ fontSize: '0.8125rem', color: '#64748b', margin: '0.25rem 0 0 0' }}>
              Update connection details in the database.
            </p>
          </div>
          <button
            onClick={onClose}
            type="button"
            style={{
              background: 'transparent',
              border: 'none',
              color: '#94a3b8',
              cursor: 'pointer',
              padding: '0.375rem',
              borderRadius: '0.375rem',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              transition: 'background-color 0.15s ease, color 0.15s ease'
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.backgroundColor = '#f1f5f9'
              e.currentTarget.style.color = '#334155'
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.backgroundColor = 'transparent'
              e.currentTarget.style.color = '#94a3b8'
            }}
          >
            <X size={20} />
          </button>
        </div>

        {/* Form Body */}
        <form onSubmit={handleSubmit}>
          <div style={{ marginBottom: '1.25rem' }}>
            <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 700, color: '#1e293b', marginBottom: '0.375rem' }}>
              Connection Name <span style={{ color: '#ef4444' }}>*</span>
            </label>
            <input
              type="text"
              required
              placeholder="e.g. RMA Support Line"
              value={name}
              onChange={(e) => setName(e.target.value)}
              style={{
                width: '100%',
                padding: '0.625rem 0.875rem',
                backgroundColor: '#ffffff',
                border: '1px solid #cbd5e1',
                borderRadius: '0.5rem',
                fontSize: '0.875rem',
                color: '#0f172a',
                fontWeight: 500,
                outline: 'none',
                boxSizing: 'border-box'
              }}
              autoFocus
            />
            <p style={{ fontSize: '0.75rem', color: '#64748b', margin: '0.375rem 0 0 0' }}>
              Give a unique name to identify this connection
            </p>
          </div>

          <div style={{ marginBottom: '1.25rem' }}>
            <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 700, color: '#1e293b', marginBottom: '0.375rem' }}>
              Nickname (Optional)
            </label>
            <input
              type="text"
              placeholder="e.g. SALE"
              value={nickname}
              maxLength={4}
              onChange={(e) => setNickname(e.target.value.toUpperCase())}
              style={{
                width: '100%',
                maxWidth: '160px',
                padding: '0.625rem 0.875rem',
                backgroundColor: '#ffffff',
                border: '1px solid #cbd5e1',
                borderRadius: '0.5rem',
                fontSize: '0.875rem',
                color: '#0f172a',
                fontWeight: 700,
                letterSpacing: '0.05em',
                outline: 'none',
                boxSizing: 'border-box'
              }}
            />
            <p style={{ fontSize: '0.75rem', color: '#64748b', margin: '0.375rem 0 0 0' }}>
              A short tag (max 4 characters) shown on campaigns sent from this connection
            </p>
          </div>

          <div style={{ marginBottom: '1.5rem' }}>
            <label style={{ display: 'block', fontSize: '0.8125rem', fontWeight: 700, color: '#1e293b', marginBottom: '0.375rem' }}>
              Description (Optional)
            </label>
            <textarea
              rows={3}
              placeholder="e.g. Primary WhatsApp connection for customer support"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              style={{
                width: '100%',
                padding: '0.625rem 0.875rem',
                backgroundColor: '#ffffff',
                border: '1px solid #cbd5e1',
                borderRadius: '0.5rem',
                fontSize: '0.875rem',
                color: '#0f172a',
                outline: 'none',
                resize: 'vertical',
                boxSizing: 'border-box'
              }}
            />
            <p style={{ fontSize: '0.75rem', color: '#64748b', margin: '0.375rem 0 0 0' }}>
              Add a short description to remember what this connection is used for.
            </p>
          </div>

          {/* Footer Buttons */}
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              paddingTop: '1.25rem',
              borderTop: '1px solid #f1f5f9',
              marginTop: '1.5rem'
            }}
          >
            <button
              type="button"
              onClick={onClose}
              style={{
                padding: '0.625rem 1.25rem',
                fontSize: '0.875rem',
                fontWeight: 600,
                color: '#334155',
                backgroundColor: '#ffffff',
                border: '1px solid #cbd5e1',
                borderRadius: '0.5rem',
                cursor: 'pointer',
                transition: 'background-color 0.15s ease'
              }}
              onMouseEnter={(e) => { e.currentTarget.style.backgroundColor = '#f8fafc' }}
              onMouseLeave={(e) => { e.currentTarget.style.backgroundColor = '#ffffff' }}
            >
              Cancel
            </button>

            <button
              type="submit"
              disabled={isSubmitting || !name.trim()}
              style={{
                padding: '0.625rem 1.5rem',
                fontSize: '0.875rem',
                fontWeight: 600,
                color: '#ffffff',
                backgroundColor: '#2563eb',
                border: 'none',
                borderRadius: '0.5rem',
                cursor: isSubmitting || !name.trim() ? 'not-allowed' : 'pointer',
                opacity: isSubmitting || !name.trim() ? 0.6 : 1,
                boxShadow: '0 1px 2px rgba(37, 99, 235, 0.2)',
                transition: 'background-color 0.15s ease'
              }}
              onMouseEnter={(e) => {
                if (!isSubmitting && name.trim()) e.currentTarget.style.backgroundColor = '#1d4ed8'
              }}
              onMouseLeave={(e) => {
                if (!isSubmitting && name.trim()) e.currentTarget.style.backgroundColor = '#2563eb'
              }}
            >
              {isSubmitting ? 'Saving...' : 'Save Changes'}
            </button>
          </div>
        </form>
      </div>
    </div>,
    document.body
  )
}
