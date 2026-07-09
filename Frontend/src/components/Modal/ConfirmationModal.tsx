import React from 'react'
import './ConfirmationModal.css'

interface ConfirmationModalProps {
  isOpen: boolean
  title: string
  message: string
  confirmText?: string
  cancelText?: string
  onConfirm: () => void
  onCancel: () => void
  isDestructive?: boolean
}

export const ConfirmationModal: React.FC<ConfirmationModalProps> = ({
  isOpen,
  title,
  message,
  confirmText = 'Confirm',
  cancelText = 'Cancel',
  onConfirm,
  onCancel,
  isDestructive = false
}) => {
  if (!isOpen) return null

  return (
    <div className="modal-overlay">
      <div className="modal-container fade-in-up">
        <h3 className="modal-title">{title}</h3>
        <p className="modal-message">{message}</p>
        <div className="modal-actions">
          <button className="btn-modal-cancel" onClick={onCancel}>
            {cancelText}
          </button>
          <button 
            className={isDestructive ? 'btn-modal-destructive' : 'btn-modal-confirm'} 
            onClick={onConfirm}
          >
            {confirmText}
          </button>
        </div>
      </div>
    </div>
  )
}
