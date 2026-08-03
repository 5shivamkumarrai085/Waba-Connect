import React from 'react'
import { motion } from 'framer-motion'
import * as Icons from 'lucide-react'
import { cardHoverProps } from '../../utils/motion'
import type { MetricCardModel } from '../../types/reporting'
import './MetricCard.css'

interface MetricCardProps {
  metric: MetricCardModel
}

export const MetricCard: React.FC<MetricCardProps> = ({ metric }) => {
  const { title, description, value, badgeText, badgeType, iconName } = metric

  // Dynamically resolve the Lucide Icon component from the icon name string
  const LucideIcon = (Icons as any)[iconName] || Icons.HelpCircle

  // Format value: Split unit from numeric values to style units separately (matches screenshot design)
  const formatValue = (val: string | number) => {
    if (typeof val === 'number') {
      return <span className="metric-val-number">{val}</span>
    }
    
    const str = String(val)
    const tokens = str.split(' ')
    
    // Check if the first token is a numeric value (e.g. "0.01", "8", "66.7", "0", "5")
    if (tokens.length > 1 && !isNaN(Number(tokens[0]))) {
      return (
        <span className="metric-val-container">
          <span className="metric-val-number">{tokens[0]}</span>{' '}
          <span className="metric-val-unit">{tokens.slice(1).join(' ')}</span>
        </span>
      )
    }
    return <span className="metric-val-number">{str}</span>
  }

  return (
    <motion.div className="metric-card" {...cardHoverProps}>
      <div className="metric-card-header">
        <div className={`metric-card-icon-box ${badgeType || ''}`}>
          <LucideIcon size={18} />
        </div>
        {badgeText && (
          <span className={`metric-badge ${badgeType || ''}`}>
            {badgeText.replace('_', ' ')}
          </span>
        )}
      </div>
      <div className="metric-card-body">
        <h3 className="metric-card-title">{title}</h3>
        <p className="metric-card-desc">{description}</p>
        <div className="metric-card-value-container">
          {formatValue(value)}
        </div>
      </div>
    </motion.div>
  )
}
