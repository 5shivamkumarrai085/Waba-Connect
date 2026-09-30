import React from 'react'
import { motion } from 'framer-motion'
import { iconByName } from '../../utils/iconRegistry'
import { fadeSlideUp, transitions } from '../../utils/motion'
import './EmptyState.css'

interface EmptyStateProps {
  iconName?: string
  /** Short bold headline shown above `message`. Omit for the original single-line look. */
  title?: string
  message: string
  /** Optional call-to-action rendered below the message, e.g. "New Contact" or "Clear Filters". */
  action?: {
    label: string
    onClick: () => void
    icon?: React.ReactNode
  }
}

export const EmptyState: React.FC<EmptyStateProps> = ({
  iconName = 'ShieldAlert',
  title,
  message,
  action
}) => {
  const IconComponent = iconByName(iconName)

  return (
    <motion.div
      className="empty-state-wrapper"
      variants={fadeSlideUp}
      initial='hidden'
      animate='visible'
      transition={transitions.normal}
    >
      <div className="empty-state-icon-box">
        <IconComponent size={44} strokeWidth={1} />
      </div>
      {title && <h3 className="empty-state-title">{title}</h3>}
      <p className="empty-state-message">{message}</p>
      {action && (
        <button type="button" className="empty-state-action" onClick={action.onClick}>
          {action.icon}
          {action.label}
        </button>
      )}
    </motion.div>
  )
}
