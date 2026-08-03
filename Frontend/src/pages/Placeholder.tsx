import React from 'react'
import { useLocation } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Info, HelpCircle } from 'lucide-react'
import { pageTransitionProps } from '../utils/motion'

export const Placeholder: React.FC = () => {
  const location = useLocation()

  // Format path: /connect-waba -> Connect Waba
  const getPageTitle = () => {
    const path = location.pathname
    const cleaned = path.replace('/', '').split('-').map(
      word => word.charAt(0).toUpperCase() + word.slice(1)
    ).join(' ')
    return cleaned || 'Page Placeholder'
  }

  return (
    <motion.div {...pageTransitionProps}>
      <div className="campaign-page-header">
        <h1>{getPageTitle()}</h1>
      </div>

      <motion.div 
        className="campaign-form-card placeholder-card"
        initial={{ opacity: 0, scale: 0.98 }}
        animate={{ opacity: 1, scale: 1 }}
        transition={{ duration: 0.25, delay: 0.1 }}
      >
        <div className="placeholder-icon-wrapper">
          <HelpCircle size={32} />
        </div>
        
        <h2 className="placeholder-title">
          {getPageTitle()} Interface Ready
        </h2>
        
        <p className="placeholder-desc">
          The frontend layout and styling shell for this module are successfully compiled. Interactive tables, graphs, and data entries will render automatically once backend ASP.NET Core APIs are connected.
        </p>

        <div className="placeholder-status-tag">
          <Info size={14} color="var(--primary)" />
          <span>No API connection detected (tables & forms showing default initial states)</span>
        </div>
      </motion.div>
    </motion.div>
  )
}
