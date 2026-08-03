import React, { useEffect, useCallback } from 'react'
import { createPortal } from 'react-dom'
import { motion, AnimatePresence } from 'framer-motion'
import { AlertTriangle } from 'lucide-react'
import { fadeScale, transitions, buttonHoverProps } from '../../utils/motion'
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
  const handleEscape = useCallback((e: KeyboardEvent) => {
    if (e.key === 'Escape') {
      onCancel()
    }
  }, [onCancel])

  useEffect(() => {
    if (isOpen) {
      document.addEventListener('keydown', handleEscape)
      return () => document.removeEventListener('keydown', handleEscape)
    }
  }, [isOpen, handleEscape])

  return createPortal(
    <AnimatePresence>
      {isOpen && (
        <motion.div
          className="modal-overlay"
          onClick={onCancel}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.15 }}
        >
          <motion.div
            className="modal-container"
            onClick={(e) => e.stopPropagation()}
            variants={fadeScale}
            initial="hidden"
            animate="visible"
            exit="exit"
            transition={transitions.snappy}
          >
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
              <motion.button
                className="btn-modal-cancel"
                onClick={onCancel}
                {...buttonHoverProps}
              >
                {cancelText}
              </motion.button>
              <motion.button
                className={isDestructive ? 'btn-modal-destructive' : 'btn-modal-confirm'}
                onClick={onConfirm}
                {...buttonHoverProps}
              >
                {confirmText}
              </motion.button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>,
    document.body
  )
}
