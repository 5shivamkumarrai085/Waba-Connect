import React, { useCallback, useId, useRef } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'framer-motion'
import { X } from 'lucide-react'
import { useEscapeKey } from '../../hooks/useEscapeKey'
import { useFocusTrap } from '../../hooks/useFocusTrap'
import { useBodyScrollLock } from '../../hooks/useBodyScrollLock'
import { fadeScale, slideInPanel, transitions } from '../../utils/motion'
import './Modal.css'

/**
 * Shared dialog shell.
 *
 * Every modal in this app was hand-rolled before this: no focus trap, no scroll
 * lock, inconsistent Escape/backdrop handling, z-indexes from 50 to 999999, and
 * most had no exit animation at all because they early-returned null.
 */

export type ModalSize = 'sm' | 'md' | 'lg' | 'xl' | 'custom'
export type ModalTone = 'default' | 'destructive' | 'success'
/**
 * Where the panel sits. 'right' turns the same dialog into an edge-anchored side panel — used
 * by the audit Event Details view — so drawers inherit the focus trap, scroll lock, Escape
 * handling and backdrop-origin guard rather than hand-rolling them again.
 */
export type ModalPlacement = 'center' | 'right'

export interface ModalProps {
  isOpen: boolean
  onClose: () => void
  title?: React.ReactNode
  subtitle?: React.ReactNode
  icon?: React.ReactNode
  tone?: ModalTone
  size?: ModalSize
  placement?: ModalPlacement
  footer?: React.ReactNode
  closeOnBackdrop?: boolean
  closeOnEscape?: boolean
  showCloseButton?: boolean
  initialFocusRef?: React.RefObject<HTMLElement | null>
  /** Applied to the panel, for consumers that own their own sizing. */
  className?: string
  bodyClassName?: string
  children: React.ReactNode
}

export const Modal: React.FC<ModalProps> = ({
  isOpen,
  onClose,
  title,
  subtitle,
  icon,
  tone = 'default',
  size = 'md',
  placement = 'center',
  footer,
  closeOnBackdrop = true,
  closeOnEscape = true,
  showCloseButton = true,
  initialFocusRef,
  className,
  bodyClassName,
  children,
}) => {
  const panelRef = useRef<HTMLDivElement>(null)
  const backdropPressRef = useRef(false)
  const reduceMotion = useReducedMotion()

  const baseId = useId()
  const titleId = `${baseId}-title`
  const subtitleId = `${baseId}-subtitle`

  useEscapeKey(isOpen && closeOnEscape, (event) => {
    event.stopPropagation()
    onClose()
  })
  useFocusTrap(panelRef, isOpen, { initialFocusRef })
  useBodyScrollLock(isOpen)

  // Only treat this as a backdrop dismissal if the press *started* on the
  // backdrop. Otherwise dragging a text selection from inside the panel and
  // releasing outside it would close the dialog — a real and irritating bug in
  // the naive `onClick={onClose}` pattern this replaces.
  const handleBackdropPointerDown = useCallback((event: React.PointerEvent) => {
    backdropPressRef.current = event.target === event.currentTarget
  }, [])

  const handleBackdropClick = useCallback(
    (event: React.MouseEvent) => {
      if (!closeOnBackdrop) return
      if (event.target !== event.currentTarget) return
      if (!backdropPressRef.current) return
      backdropPressRef.current = false
      onClose()
    },
    [closeOnBackdrop, onClose]
  )

  const hasHeader = Boolean(title || icon || showCloseButton)

  return createPortal(
    <AnimatePresence>
      {isOpen && (
        <motion.div
          className={`oc-dialog-backdrop oc-dialog-backdrop--${placement}`}
          onPointerDown={handleBackdropPointerDown}
          onClick={handleBackdropClick}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.15 }}
        >
          <motion.div
            ref={panelRef}
            role="dialog"
            aria-modal="true"
            aria-labelledby={title ? titleId : undefined}
            aria-describedby={subtitle ? subtitleId : undefined}
            tabIndex={-1}
            className={[
              'oc-dialog-panel',
              `oc-dialog-panel--${size}`,
              `oc-dialog-panel--place-${placement}`,
              className ?? '',
            ]
              .filter(Boolean)
              .join(' ')}
            variants={reduceMotion ? undefined : placement === 'right' ? slideInPanel : fadeScale}
            initial={reduceMotion ? { opacity: 0 } : 'hidden'}
            animate={reduceMotion ? { opacity: 1 } : 'visible'}
            exit={reduceMotion ? { opacity: 0 } : 'exit'}
            transition={transitions.snappy}
          >
            {hasHeader && (
              <div className={`oc-dialog-header${subtitle ? ' oc-dialog-header--bordered' : ''}`}>
                {icon && (
                  <span className={`oc-dialog-icon oc-dialog-icon--${tone}`}>{icon}</span>
                )}
                <div className="oc-dialog-heading">
                  {title && (
                    <h2 id={titleId} className="oc-dialog-title">
                      {title}
                    </h2>
                  )}
                  {subtitle && (
                    <div id={subtitleId} className="oc-dialog-subtitle">
                      {subtitle}
                    </div>
                  )}
                </div>
                {showCloseButton && (
                  <button
                    type="button"
                    className="oc-dialog-close"
                    onClick={onClose}
                    aria-label="Close dialog"
                  >
                    <X size={18} />
                  </button>
                )}
              </div>
            )}

            <div className={`oc-dialog-body${bodyClassName ? ` ${bodyClassName}` : ''}`}>
              {children}
            </div>

            {footer && <div className="oc-dialog-footer">{footer}</div>}
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>,
    document.body
  )
}

export default Modal
