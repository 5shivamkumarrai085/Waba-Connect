import React, { useRef } from 'react'
import { AlertTriangle } from 'lucide-react'
import { Modal } from './Modal'
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
  // Most call sites build the message from a nullable target, e.g.
  //   `Are you sure you want to delete "${deleteTarget?.name}"?`
  // The moment onCancel nulls that target the string re-renders as "undefined".
  // That was invisible while the modal unmounted instantly; now that there is a
  // real exit animation it would flash for ~150ms. Latch the last open values.
  const latched = useRef({ title, message })
  if (isOpen) latched.current = { title, message }

  return (
    <Modal
      isOpen={isOpen}
      onClose={onCancel}
      size="sm"
      tone={isDestructive ? 'destructive' : 'default'}
      icon={showWarningIcon ? <AlertTriangle size={20} /> : undefined}
      title={latched.current.title}
      showCloseButton={false}
      footer={
        <>
          <button
            type="button"
            className="oc-dialog-btn oc-dialog-btn-secondary"
            onClick={onCancel}
          >
            {cancelText}
          </button>
          <button
            type="button"
            className={`oc-dialog-btn ${
              isDestructive ? 'oc-dialog-btn-destructive' : 'oc-dialog-btn-primary'
            }`}
            onClick={onConfirm}
          >
            {confirmText}
          </button>
        </>
      }
    >
      <p className="oc-dialog-message">{latched.current.message}</p>
    </Modal>
  )
}
