import React from 'react'
import { createPortal } from 'react-dom'
import { AlertTriangle } from 'lucide-react'
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
  showWarningIcon?: boolean
}

export const ConfirmationModal: React.FC<ConfirmationModalProps> = ({
  isOpen,
  title,
  message,
  confirmText = 'Confirm',
  cancelText = 'Cancel',
  onConfirm,
  onCancel,
  isDestructive = false,
  showWarningIcon = false
}) => {
  if (!isOpen) return null

  return createPortal(
    <div className="modal-overlay">
      <div className="modal-container fade-in-up">
        <div className="modal-header-row">
          {showWarningIcon && (
            <div className="modal-icon-wrapper destructive">
              <AlertTriangle size={20} />
            </div>
          )}
          <h3 className="modal-title">{title}</h3>
        </div>
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
    </div>,
    document.body
  )
}
