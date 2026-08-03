import React from 'react'
import { motion } from 'framer-motion'
import * as Icons from 'lucide-react'
import { fadeSlideUp, transitions } from '../../utils/motion'
import { getEmptyStateQuote } from '../../utils/quotes'
import './EmptyState.css'

interface EmptyStateProps {
  iconName?: string
  message: string
}

export const EmptyState: React.FC<EmptyStateProps> = ({
  iconName = 'ShieldAlert',
  message
}) => {
  const IconComponent = (Icons as any)[iconName] || Icons.HelpCircle

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
      <p className="empty-state-message">{message}</p>
      <p className="empty-state-quote">{getEmptyStateQuote()}</p>
    </motion.div>
  )
}
