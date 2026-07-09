import React from 'react'
import { Check } from 'lucide-react'
import './StatusBadge.css'

interface StatusBadgeProps {
  type: 'verified' | 'stale' | 'success' | 'warning' | string
  text?: string
}

export const StatusBadge: React.FC<StatusBadgeProps> = ({ type, text }) => {
  const badgeClass = type.toLowerCase()
  const displayVal = text || type

  const renderIcon = () => {
    if (badgeClass === 'verified') {
      return (
        <span className="status-badge-icon">
          <Check size={12} strokeWidth={3} />
        </span>
      )
    }
    return null
  }

  return (
    <span className={`status-badge ${badgeClass}`}>
      {renderIcon()}
      <span>{displayVal}</span>
    </span>
  )
}
